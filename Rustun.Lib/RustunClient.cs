using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using NetWintun;
using Rustun.Lib.Crypto;
using Rustun.Lib.Message;
using Rustun.Lib.Packet;
using Serilog;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Management;
using System.Net;
using System.Net.Sockets;
using Vanara.PInvoke;

namespace Rustun.Lib;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:验证平台兼容性", Justification = "<挂起>")]
public class RustunClient
{
    public const int DefaultTimeout = 3000;
    public const string AdapterName = "Rustun";
    public const string TunnelType = "Wintun";

    private IChannel? channel;
    private TaskCompletionSource<HandshakeReplyMessage>? handshakeAckCompletion;
    private CancellationTokenSource? handshakeStopCts;
    private static IEventLoopGroup group = new MultithreadEventLoopGroup();

    private CancellationTokenSource? trafficCts;
    private Task? trafficTask;

    private Adapter? Adapter;
    private Guid? AdapterId;
    private Session? Session;

    /// <summary>由握手返回的 peer <c>ciders</c> 添加的 IPv4 路由，断开时通过 <see cref="DeleteIpForwardEntry2"/> 删除。</summary>
    private readonly List<IpHlpApi.MIB_IPFORWARD_ROW2> _peerCiderForwardRows = [];

    private long _bytesUploaded;
    private long _bytesDownloaded;

    /// <summary>隧道建立后由虚拟网卡发往服务器的数据字节数（不含握手/心跳等控制报文）。</summary>
    public long BytesUploaded => Interlocked.Read(ref _bytesUploaded);

    /// <summary>隧道建立后从服务器收到的数据负载字节数（不含控制报文）。</summary>
    public long BytesDownloaded => Interlocked.Read(ref _bytesDownloaded);

    public event EventHandler? OnConnected;
    public event EventHandler? OnDisconnected;
    public event EventHandler<Exception?>? OnError;
    public event EventHandler<Collection<PeerDetail>>? OnPeerDetail;

    public RustunClient()
    {

    }

    /// <summary>
    /// 连接服务器
    /// </summary>
    /// <param name="address"></param>
    /// <param name="port"></param>
    /// <param name="crypto"></param>
    /// <returns></returns>
    private async Task<IChannel> connectServerAsync(IPAddress address, int port, RustunCrypto crypto, string identity)
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
                pipeline.AddLast(new RustunClientHandler(identity, this));
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
    public async Task StartAsync(string ip, int port, string identity, string cryptoAlgorithm, string secret)
    {
        if (!IPAddress.TryParse(ip, out var address))
        {
            throw new FormatException($"Invalid IP address: '{ip}'.");
        }

        RustunCrypto crypto = cryptoAlgorithm.ToUpperInvariant() switch
        {
            "AES" => new RustunAes256Crypto(secret),
            "XOR" => new RustunXorCrypto(secret),
            "CHACHA20" => new RustunChacha20Crypto(secret),
            "Plain" => new RustunCrypto(),
            _ => throw new NotSupportedException(
                $"Unsupported encryption algorithm '{cryptoAlgorithm}'. Supported values: AES, CHACHA20, XOR, Plain."),
        };

        // 创建握手响应完成源
        handshakeAckCompletion = new TaskCompletionSource<HandshakeReplyMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            // 连接服务器
            channel = await connectServerAsync(address, port, crypto, identity);

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

            // 添加对等路由
            addPeerCiderRoutesThroughVirtualAdapter(response, AdapterId.Value);

            // 新会话流量统计从 0 开始；先通知就绪再启动转发，保证统计与「已连接」语义一致
            ResetTrafficStatistics();
            OnConnected?.Invoke(this, EventArgs.Empty);
            transferTraffic();
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
                        Interlocked.Add(ref _bytesUploaded, packet.Length);
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
        removePeerCiderRoutes();

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
    /// 将握手返回的各 Peer <c>ciders</c> 中的 IPv4 CIDR 添加为系统路由，出站接口为当前 Wintun 适配器（使访问这些网段的流量经虚拟网卡进入隧道）。
    /// </summary>
    private void addPeerCiderRoutesThroughVirtualAdapter(HandshakeReplyMessage response, Guid adapterGuid)
    {
        // 先删除之前添加的 Peer Cider 路由，避免重复添加或旧路由残留
        removePeerCiderRoutes();

        var conv = IpHlpApi.ConvertInterfaceGuidToLuid(in adapterGuid, out IpHlpApi.NET_LUID interfaceLuid);
        if (conv != 0)
        {
            Log.Warning("ConvertInterfaceGuidToLuid failed for adapter {AdapterGuid}: Win32 {Code}", adapterGuid, conv);
            return;
        }

        var nextHop = (Ws2_32.SOCKADDR_INET)new Ws2_32.SOCKADDR_IN(new Ws2_32.IN_ADDR(0u), 0);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var peer in response.PeerDetails ?? [])
        {
            foreach (var cidr in peer.Ciders ?? [])
            {
                if (string.IsNullOrWhiteSpace(cidr))
                {
                    continue;
                }

                if (!tryParseIpv4Cidr(cidr.Trim(), out var networkBytes, out var prefixLen))
                {
                    Log.Debug("Skip non-IPv4 or invalid cidr: {Cidr}", cidr);
                    continue;
                }

                var key = $"{networkBytes[0]}.{networkBytes[1]}.{networkBytes[2]}.{networkBytes[3]}/{prefixLen}";
                if (!seen.Add(key))
                {
                    continue;
                }

                IpHlpApi.InitializeIpForwardEntry(out IpHlpApi.MIB_IPFORWARD_ROW2 row);

                row.InterfaceLuid = interfaceLuid;
                row.DestinationPrefix = new IpHlpApi.IP_ADDRESS_PREFIX((Ws2_32.SOCKADDR_INET)new Ws2_32.SOCKADDR_IN(new Ws2_32.IN_ADDR(networkBytes), 0), prefixLen);
                row.NextHop = nextHop;
                row.Protocol = IpHlpApi.MIB_IPFORWARD_PROTO.MIB_IPPROTO_NETMGMT;
                row.SitePrefixLength = prefixLen;
                row.Metric = unchecked((uint)-1);

                var err = IpHlpApi.CreateIpForwardEntry2(ref row);
                if (err != 0)
                {
                    if (err == 183)
                    {
                        Log.Debug("Route already exists, skip: {Cidr}", cidr);
                        continue;
                    }

                    Log.Warning("CreateIpForwardEntry2 failed for {Cidr}: Win32 {Code}", cidr, err);
                    continue;
                }

                _peerCiderForwardRows.Add(row);
                Log.Information("Added peer cidr route {Cidr} via Wintun (LUID).", key);
            }
        }
    }

    /// <summary>
    /// 删除之前添加的 Peer Cider 路由
    /// </summary>
    private void removePeerCiderRoutes()
    {
        if (_peerCiderForwardRows.Count == 0)
        {
            return;
        }

        for (var i = _peerCiderForwardRows.Count - 1; i >= 0; i--)
        {
            var row = _peerCiderForwardRows[i];
            var err = IpHlpApi.DeleteIpForwardEntry2(ref row);
            if (err != 0 && err != 1168)
            {
                Log.Warning("DeleteIpForwardEntry2 failed: Win32 {Code}", err);
            }
        }

        _peerCiderForwardRows.Clear();
    }

    /// <summary>
    /// 尝试将 IPv4 CIDR 表示法的字符串解析为网络地址和前缀长度。
    /// </summary>
    /// <remarks>仅支持标准 IPv4 CIDR 表达式。前缀长度必须在 0 到 32 之间。解析失败时，输出参数将被重置为默认值。</remarks>
    /// <param name="cidr">要解析的 IPv4 CIDR 字符串（例如 "192.168.1.0/24"）。必须为有效的 IPv4 地址和前缀长度格式。</param>
    /// <param name="networkBytes">如果解析成功，则包含网络地址的 4 字节数组；否则为一个空数组。</param>
    /// <param name="prefixLen">如果解析成功，则包含网络前缀长度（0 到 32）；否则为 0。</param>
    /// <returns>如果解析成功，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
    private static bool tryParseIpv4Cidr(string cidr, out byte[] networkBytes, out byte prefixLen)
    {
        networkBytes = Array.Empty<byte>();
        prefixLen = 0;

        var slash = cidr.IndexOf('/');
        if (slash <= 0 || slash >= cidr.Length - 1)
        {
            return false;
        }

        if (!IPAddress.TryParse(cidr.AsSpan(0, slash), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (!byte.TryParse(cidr.AsSpan(slash + 1), out var p) || p > 32)
        {
            return false;
        }

        prefixLen = p;
        ReadOnlySpan<byte> addr = ip.GetAddressBytes();
        if (addr.Length != 4)
        {
            return false;
        }

        uint host = BinaryPrimitives.ReadUInt32BigEndian(addr);
        uint mask = p == 0 ? 0u : uint.MaxValue << (32 - p);
        uint net = host & mask;

        networkBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(networkBytes, net);
        return true;
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

        // 重置流量统计
        ResetTrafficStatistics();

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
        // 更新下载流量统计并将数据包发送到虚拟网卡会话
        Interlocked.Add(ref _bytesDownloaded, data.Length);
        if (Session != null)
        {
            Session.SendPacket(data);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重置流量统计（在每次成功连接服务器并建立会话后调用，确保流量统计反映当前会话的流量而不是累计的历史流量）
    /// </summary>
    private void ResetTrafficStatistics()
    {
        Interlocked.Exchange(ref _bytesUploaded, 0);
        Interlocked.Exchange(ref _bytesDownloaded, 0);
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
        // 设置握手响应完成源的结果，通知 StartAsync 方法继续执行
        _ = handshakeAckCompletion?.TrySetResult(message);

        // 触发 PeerDetail 事件，通知调用者当前的对等节点信息（如果有）
        OnPeerDetail?.Invoke(this, new Collection<PeerDetail>(message.PeerDetails ?? new List<PeerDetail>()));

        // 客户端无需对握手响应消息做其他处理，返回即可
        return Task.CompletedTask;
    }

    /// <summary>
    /// 收到心跳消息
    /// </summary>
    /// <param name="message"></param>
    /// <returns></returns>
    public Task onKeepAliveMessage(KeepAliveMessage message)
    {
        // 触发 PeerDetail 事件，通知调用者当前的对等节点信息（如果有）
        OnPeerDetail?.Invoke(this, new Collection<PeerDetail>(message.PeerDetails ?? new List<PeerDetail>()));

        // 客户端无需对心跳消息做其他处理，返回即可
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
