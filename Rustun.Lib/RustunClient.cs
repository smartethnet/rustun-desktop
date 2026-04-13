using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Rustun.Lib.Crypto;
using Rustun.Lib.Message;
using Serilog;
using System.Net;

namespace Rustun.Lib;

public class RustunClient
{
    public const int DefaultTimeout = 3000;

    private readonly string ip;
    private readonly int port;
    private readonly string identity;
    private readonly string cryptoAlgorithm;
    private readonly string secret;

    private IChannel? channel;
    private static IEventLoopGroup group = new MultithreadEventLoopGroup();

    public string Identity => identity;

    public RustunClient(string ip, int port, string identity, string cryptoAlgorithm, string secret)
    {
        this.ip = ip;
        this.port = port;
        this.identity = identity;
        this.cryptoAlgorithm = cryptoAlgorithm;
        this.secret = secret;
    }

    private RustunCrypto CreateCrypto() =>
        cryptoAlgorithm.ToUpperInvariant() switch
        {
            "AES" => new RustunAes256Crypto(secret),
            "XOR" => new RustunXorCrypto(secret),
            "CHACHA20" => new RustunChacha20Crypto(secret),
            _ => new RustunCrypto()
        };

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IPAddress.TryParse(ip, out var address))
        {
            throw new FormatException($"Invalid IP address: '{ip}'.");
        }

        var crypto = CreateCrypto();

        try
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
            channel = await bootstrap.ConnectAsync(new IPEndPoint(address, port));
        }
        catch
        {
            // 连接失败时确保资源被正确释放
            await DisposeNetworkResourcesAsync();

            // 重新抛出异常以通知调用者
            throw;
        }
    }

    public Task StopAsync() => DisposeNetworkResourcesAsync();

    private async Task DisposeNetworkResourcesAsync()
    {
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
    }

    public Task onConnected()
    {
        Log.Information($"Connect to server successful");
        return Task.CompletedTask;
    }

    public Task onDisconnected()
    {
        Log.Information($"Disconnect from server");
        return Task.CompletedTask;
    }

    public Task onError(Exception? error)
    {
        Log.Error($"An error occurred in RustunClient: {error?.Message}");
        return Task.CompletedTask;
    }

    public Task onDataMessage(byte[] data)
    {
        Log.Information($"Received data message: {data.Length} bytes");
        return Task.CompletedTask;
    }

    public Task onHandshakeMessage(HandshakeMessage message)
    {
        Log.Information($"Received handshake message: Identity={message.Identity}");
        return Task.CompletedTask;
    }

    public Task onHandshakeReplyMessage(HandshakeReplyMessage message)
    {
        Log.Information($"Received handshake reply message: PrivateIp={message.PrivateIp}, Mask={message.Mask}, Gateway={message.Gateway} PeerDetails={message.PeerDetails}");
        return Task.CompletedTask;
    }

    public Task onKeepAliveMessage(KeepAliveMessage message)
    {
        Log.Information($"Received keep-alive message: Identity={message.Identity}, Ipv6={message.Ipv6}, Port={message.Port}, StunIp={message.StunIp}, StunPort={message.StunPort}, PeerDetails={message.PeerDetails}");
        return Task.CompletedTask;
    }

    public Task onProbeHolePunchMessage(ProbeHolePunchMessage message)
    {
        Log.Information($"Received probe hole punch message: Identity={message.Identity}");
        return Task.CompletedTask;
    }

    public Task onProbeIpv6Message(ProbeIpv6Message message)
    {
        Log.Information($"Received probe IPv6 message: Identity={message.Identity}");
        return Task.CompletedTask;
    }
}
