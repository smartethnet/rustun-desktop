using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;
using Rustun.Lib.Message;
using Rustun.Lib.Packet;
using System.Text;
using System.Text.Json;

namespace Rustun.Lib
{
    public class RustunHeartbeatClientHandler : ChannelHandlerAdapter
    {
        private string identity;

        public RustunHeartbeatClientHandler(string identity)
        {
            this.identity = identity;
        }

        public override void UserEventTriggered(IChannelHandlerContext context, object evt)
        {
            if (evt is IdleStateEvent idleStateEvent)
            {
                // create a heartbeat packet with the identity as data
                var message = new KeepAliveMessage();
                message.Identity = identity;

                // 发送心跳包
                var data = JsonSerializer.Serialize(message);
                var heartbeatPacket = new RustunPacket(RustunPacketType.Heartbeat, Encoding.UTF8.GetBytes(data));
                context.WriteAndFlushAsync(heartbeatPacket);
            }
        }
    }
}
