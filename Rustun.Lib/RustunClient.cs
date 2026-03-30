using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Rustun.Lib.Crypto;
using System;
using System.Collections.Generic;
using System.Text;

namespace Rustun.Lib
{
    public class RustunClient
    {
        private string ip;
        private int port;
        private string identity;
        private string crypto;
        private string secret;

        private IChannel? channel;
        private IEventLoopGroup? group;

        public RustunClient(string ip, int port, string identity, string crypto, string secret)
        {
            this.ip = ip;
            this.port = port;
            this.identity = identity;
            this.crypto = crypto;
            this.secret = secret;
        }

        private RustunCrypto GetRustunCrypto()
        {
            switch (crypto)
            {
                case "AES":
                    return new RustunAes256Crypto(secret);
                case "XOR":
                    return new RustunXorCrypto(secret);
                case "Chacha20":
                    return new RustunChacha20Crypto(secret);
                default:
                    return new RustunCrypto();
            }
        }

        public async void Start()
        {
            // 配置加密器
            var crypto = GetRustunCrypto();

            // 配置Netty
            this.group = new MultithreadEventLoopGroup();
            var bootstrap = new Bootstrap();
            bootstrap.Group(group)
                .Channel<TcpSocketChannel>()
                .Handler(new ActionChannelInitializer<IChannel>(channel =>
                {
                    var pipeline = channel.Pipeline;

                    // 添加解码器和编码器
                    pipeline.AddLast(new RustunPacketDecoder(crypto));
                    pipeline.AddLast(new RustunPacketEncoder(crypto));

                    // 添加心跳处理器
                    pipeline.AddLast(new RustunHeartbeatClientHandler(identity));

                    // 添加业务处理器
                }));

            // 连接服务器
            bootstrap.Option(ChannelOption.TcpNodelay, true);
            bootstrap.Option(ChannelOption.SoKeepalive, true);
            bootstrap.Option(ChannelOption.SoTimeout, 5000);
            bootstrap.Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(5));
            this.channel = await bootstrap.ConnectAsync(ip, port);
        }

        public async void Stop()
        {
            if (channel != null)
            {
                await channel.CloseAsync();
            }
            if (group != null)
            {
                await group.ShutdownGracefullyAsync();
            }
        }
    }
}
