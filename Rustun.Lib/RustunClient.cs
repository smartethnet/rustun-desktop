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
    public const int DefaultTimeout = 5000;

    private readonly string ip;
    private readonly int port;
    private readonly string identity;
    private readonly string cryptoAlgorithm;
    private readonly string secret;

    private IChannel? channel;
    private IEventLoopGroup? group;

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
        group = new MultithreadEventLoopGroup();

        try
        {
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

            bootstrap.Option(ChannelOption.TcpNodelay, true);
            bootstrap.Option(ChannelOption.SoKeepalive, true);
            bootstrap.Option(ChannelOption.SoTimeout, DefaultTimeout);
            bootstrap.Option(ChannelOption.ConnectTimeout, TimeSpan.FromMilliseconds(DefaultTimeout));

            channel = await bootstrap.ConnectAsync(new IPEndPoint(address, port));
        }
        catch
        {
            await DisposeNetworkResourcesAsync();
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

        var g = group;
        group = null;
        if (g != null)
        {
            await g.ShutdownGracefullyAsync();
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

    public Task onError(Exception? error) => Task.CompletedTask;
    public Task onDataMessage(byte[] data) => Task.CompletedTask;
    public Task onHandshakeMessage(HandshakeMessage message) => Task.CompletedTask;
    public Task onHandshakeReplyMessage(HandshakeReplyMessage message) => Task.CompletedTask;
    public Task onKeepAliveMessage(KeepAliveMessage message) => Task.CompletedTask;
    public Task onProbeHolePunchMessage(ProbeHolePunchMessage message) => Task.CompletedTask;
    public Task onProbeIpv6Message(ProbeIpv6Message message) => Task.CompletedTask;
}
