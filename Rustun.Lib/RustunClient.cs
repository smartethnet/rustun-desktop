using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using NetWintun;
using Rustun.Lib.Crypto;
using Rustun.Lib.Message;
using Rustun.Lib.Packet;
using Serilog;
using System.Management;
using System.Net;

namespace Rustun.Lib;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:验证平台兼容性", Justification = "<挂起>")]
public class RustunClient
{
    public const int DefaultTimeout = 3000;
    public const string AdapterName = "Rustun";
    public const string TunnelType = "Wintun";

    private readonly IPAddress address;
    private readonly int port;
    private readonly string identity;
    private readonly RustunCrypto crypto;

    private IChannel? channel;
    private TaskCompletionSource<HandshakeReplyMessage>? handshakeAckCompletion;
    private CancellationTokenSource? handshakeStopCts;
    private static IEventLoopGroup group = new MultithreadEventLoopGroup();

    private CancellationTokenSource? trafficCts;
    private Task? trafficTask;

    private Adapter? Adapter;
    private Guid? AdapterId;
    private Session? Session;

    public event EventHandler? OnConnected;
    public event EventHandler? OnDisconnected;
    public event EventHandler<Exception?>? OnError;

    public string Identity => identity;

    public RustunClient(string ip, int port, string identity, string cryptoAlgorithm, string secret)
    {
        this.port = port;
        if (!IPAddress.TryParse(ip, out var address))
        {
            throw new FormatException($"Invalid IP address: '{ip}'.");
        }
        this.address = address;
        this.identity = identity;

        // 初始化加密算法
        this.crypto = cryptoAlgorithm.ToUpperInvariant() switch
        {
            "AES" => new RustunAes256Crypto(secret),
            "XOR" => new RustunXorCrypto(secret),
            "CHACHA20" => new RustunChacha20Crypto(secret),
            _ => new RustunCrypto()
        };
    }

    /// <summary>
    /// 连接服务器
    /// </summary>
    /// <param name="address"></param>
    /// <param name="port"></param>
    /// <param name="crypto"></param>
    /// <returns></returns>
    private async Task<IChannel> connectServerAsync(IPAddress address, int port, RustunCrypto crypto)
    {
        // 配置客户端 Bootstrap
        var bootstrap = new Bootstrap();
        bootstrap.Group(group)
            .Channel<TcpSocketChannel>()
            .Handler(new ActionChannelInitializer<IChannel>(ch =>
            {
                var pipeline = ch.Pipeline;
                // 添加编码解码器
                pipeline.AddLast(new RustunPacketDecoder(crypto));
                pipeline.AddLast(new RustunPacketEncoder(crypto));

                // 添加心跳检测
                pipeline.AddLast(new IdleStateHandler(0, 10, 0));
                pipeline.AddLast(new RustunHeartbeatClientHandler(identity));

                // 添加客户端消息处理
                pipeline.AddLast(new RustunClientHandler(this));
            }));

        // 设置 TCP 选项
        bootstrap.Option(ChannelOption.TcpNodelay, true);
        bootstrap.Option(ChannelOption.SoKeepalive, true);
        bootstrap.Option(ChannelOption.SoTimeout, DefaultTimeout);
        bootstrap.Option(ChannelOption.ConnectTimeout, TimeSpan.FromMilliseconds(DefaultTimeout));

        // 连接服务器
        return await bootstrap.ConnectAsync(new IPEndPoint(address, port));
    }

    /// <summary>
    /// 启动
    /// </summary>
    /// <returns></returns>
    public async Task StartAsync()
    {
        // 创建握手响应完成源
        handshakeAckCompletion = new TaskCompletionSource<HandshakeReplyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            // 连接服务器
            channel = await connectServerAsync(address, port, crypto);

            // 等待握手响应（StopAsync 可通过 handshakeStopCts 取消此等待）
            handshakeStopCts = new CancellationTokenSource();
            using var handshakeTimeoutCts = new CancellationTokenSource(DefaultTimeout);
            using var handshakeLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(handshakeStopCts.Token, handshakeTimeoutCts.Token);
            HandshakeReplyMessage response;
            try
            {
                response = await handshakeAckCompletion.Task.WaitAsync(handshakeLinkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (handshakeStopCts.IsCancellationRequested)
                {
                    throw new OperationCanceledException("Handshake wait was cancelled by StopAsync.");
                }

                throw new TimeoutException($"Handshake acknowledgment timed out after {DefaultTimeout} ms.");
            }

            // 创建虚拟网卡
            createVirtualNetworkAdapter();

            // Config adapter network
            var interfaceId = AdapterId?.ToString() ?? throw new InvalidOperationException("Adapter ID is null after creation.");
            setVirtualNetworkAdapterIpAddress(interfaceId, response.PrivateIp, response.Mask);
            setVirtualNetworkAdapterGateway(interfaceId, response.Gateway);

            // 开始转发网卡流量到服务器
            transferTraffic();

            // 触发连接成功事件
            OnConnected?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // 连接失败时确保资源被正确释放
            await DisposeNetworkResourcesAsync();

            // 重新抛出异常以通知调用者
            throw;
        }
        finally
        {
            handshakeStopCts?.Dispose();
            handshakeStopCts = null;
            handshakeAckCompletion = null;
        }
    }

    /// <summary>
    /// 停止
    /// </summary>
    /// <returns></returns>
    public async Task StopAsync()
    {
        Log.Information($"Stopping RustunClient...");
        handshakeStopCts?.Cancel();
        trafficCts?.Cancel();
        await DisposeNetworkResourcesAsync();
        Log.Information($"RustunClient stopped.");
    }

    /// <summary>
    /// 转发网卡流量到服务端
    /// </summary>
    private void transferTraffic()
    {
        // 监听网卡流量并转发到服务器
        if (Session == null || channel == null)
        {
            Log.Warning($"Cannot transfer traffic: Session or channel is not initialized.");
            return;
        }

        trafficCts?.Cancel();
        trafficCts?.Dispose();
        trafficCts = new CancellationTokenSource();

        var token = trafficCts.Token;
        trafficTask = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && Session != null && channel != null && channel.Active)
                {
                    var packet = await Session.ReceivePacketAsync();
                    if (packet != null)
                    {
                        RustunPacket dataPacket = new RustunPacket(RustunPacketType.Data, packet);
                        await channel.WriteAndFlushAsync(dataPacket);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // expected on stop
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in traffic transfer loop.");
            }
        });
    }

    /// <summary>
    /// 创建虚拟网卡和会话
    /// </summary>
    private void createVirtualNetworkAdapter()
    {
        // 创建网卡
        AdapterId = Guid.NewGuid();
        Adapter = Adapter.Create(AdapterName, TunnelType, AdapterId);
        Log.Information($"Created adapter with name: {AdapterName}, id: {AdapterId}");

        // 创建会话
        Session = Adapter.StartSession(Wintun.Constants.MaxRingCapacity);
        Log.Information($"Started session on adapter {AdapterName}");
    }

    /// <summary>
    /// 设置虚拟网卡的 IP 地址和子网掩码
    /// </summary>
    /// <param name="uuid"></param>
    /// <param name="ip"></param>
    /// <param name="mask"></param>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="Exception"></exception>
    private void setVirtualNetworkAdapterIpAddress(string uuid, string ip, string mask)
    {
        // 获取所有网络适配器配置
        using var mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
        using var adapters = mc.GetInstances();
        foreach (ManagementObject item in adapters)
        {
            using (item)
            {
                var objectSettingId = item["SettingID"];
                if (objectSettingId is string settingId)
                {
                    string adapterId = "{" + uuid.ToUpper() + "}";
                    if (settingId == adapterId)
                    {
                        var newIP = item.GetMethodParameters("EnableStatic");
                        if (newIP == null)
                        {
                            throw new InvalidOperationException("EnableStatic method not found on adapter configuration.");
                        }

                        newIP["IPAddress"] = new string[] { ip ?? throw new ArgumentNullException(nameof(ip)) };
                        newIP["SubnetMask"] = new string[] { mask ?? throw new ArgumentNullException(nameof(mask)) };

                        var result = item.InvokeMethod("EnableStatic", newIP, null);
                        if (result == null)
                        {
                            throw new InvalidOperationException("EnableStatic invocation returned null.");
                        }

                        var returnObj = result["returnvalue"];
                        int returnValue = Convert.ToInt32(returnObj ?? 0);
                        if (returnValue != 0 && returnValue != 1)
                        {
                            throw new Exception("Set IP address and subnet mask error: " + returnValue);
                        }

                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 设置虚拟网卡网关地址
    /// </summary>
    /// <param name="uuid"></param>
    /// <param name="gateway"></param>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="Exception"></exception>
    private void setVirtualNetworkAdapterGateway(string uuid, string gateway)
    {
        // 获取所有网络适配器配置
        using var mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
        using var adapters = mc.GetInstances();
        foreach (ManagementObject item in adapters)
        {
            using (item)
            {
                var objectSettingId = item["SettingID"];
                if (objectSettingId is string settingId)
                {
                    string adapterId = "{" + uuid.ToUpper() + "}";
                    if (settingId == adapterId)
                    {
                        var newGateway = item.GetMethodParameters("SetGateways");
                        if (newGateway == null)
                        {
                            throw new InvalidOperationException("SetGateways method not found on adapter configuration.");
                        }
                        newGateway["DefaultIPGateway"] = new string[] { gateway ?? throw new ArgumentNullException(nameof(gateway)) };
                        var result = item.InvokeMethod("SetGateways", newGateway, null);
                        if (result == null)
                        {
                            throw new InvalidOperationException("SetGateways invocation returned null.");
                        }
                        var returnObj = result["returnvalue"];
                        int returnValue = Convert.ToInt32(returnObj ?? 0);
                        if (returnValue != 0 && returnValue != 1)
                        {
                            throw new Exception("Set gateway error: " + returnValue);
                        }

                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    /// <returns></returns>
    private async Task DisposeNetworkResourcesAsync()
    {
        // 关闭流量转发
        trafficCts?.Cancel();
        var tt = trafficTask;
        trafficTask = null;
        if (tt != null)
        {
            try
            {
                var completed = await Task.WhenAny(tt, Task.Delay(TimeSpan.FromSeconds(1)));
                if (!ReferenceEquals(completed, tt))
                {
                    Log.Warning("Traffic transfer task did not stop within timeout; continuing shutdown.");
                }
                else
                {
                    await tt;
                }
            }
            catch
            {
                // ignore background loop errors during shutdown
            }
        }
        trafficCts?.Dispose();
        trafficCts = null;

        // 关闭网络连接
        var ch = channel;
        channel = null;
        if (ch != null)
        {
            try
            {
                await ch.CloseAsync();
            }
            catch
            {
                // 关闭阶段忽略异常，继续释放 EventLoopGroup
            }
        }

        // 关闭虚拟网卡会话
        if (Session != null)
        {
            try
            {
                Session.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error($"Error disposing session: {ex.Message}");
            }
            finally
            {
                Session = null;
            }
        }

        // 关闭虚拟网卡
        if (Adapter != null)
        {
            try
            {
                Adapter.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error($"Error disposing adapter: {ex.Message}");
            }
            finally
            {
                Adapter = null;
                AdapterId = null;
            }
        }
    }

    /// <summary>
    /// 连接服务器成功
    /// </summary>
    /// <returns></returns>
    public Task onConnected()
    {
        Log.Information($"Connect to server successful");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 与服务器断开连接
    /// </summary>
    /// <returns></returns>
    public async Task onDisconnected()
    {
        // 如果在断开连接时握手尚未完成，设置异常以通知等待的 StartAsync 调用
        _ = handshakeAckCompletion?.TrySetException(
            new InvalidOperationException("Disconnected before handshake acknowledgment was received."));

        // 释放网络资源
        Log.Information($"Disconnect from server");
        await DisposeNetworkResourcesAsync();

        // 触发断开连接事件
        OnDisconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 发生了错误
    /// </summary>
    /// <param name="error"></param>
    /// <returns></returns>
    public Task onError(Exception? error)
    {
        _ = handshakeAckCompletion?.TrySetException(error ?? new InvalidOperationException("Unknown error."));
        Log.Error($"An error occurred in RustunClient: {error?.Message}");

        // 触发错误事件
        OnError?.Invoke(this, error);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 收到从服务器发送过来的数据包
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public Task onDataMessage(byte[] data)
    {
        if (Session != null)
        {
            Session.SendPacket(data);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 收到握手消息，客户端无需处理握手消息，直接返回即可，握手响应会通过 onHandshakeReplyMessage 方法返回
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public Task onHandshakeMessage(HandshakeMessage message)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 收到握手确认消息，设置握手响应完成源的结果以通知 StartAsync 方法继续执行后续步骤（如创建虚拟网卡和开始转发流量）
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public Task onHandshakeReplyMessage(HandshakeReplyMessage message)
    {
        _ = handshakeAckCompletion?.TrySetResult(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 收到心跳消息
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public Task onKeepAliveMessage(KeepAliveMessage message)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 收到ProbeHolePunchMessage消息
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public Task onProbeHolePunchMessage(ProbeHolePunchMessage message)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 收到ProbeIpv6Message消息
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public Task onProbeIpv6Message(ProbeIpv6Message message)
    {
        return Task.CompletedTask;
    }
}
