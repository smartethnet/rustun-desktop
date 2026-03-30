using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using Rustun.Lib.Crypto;
using Rustun.Lib.Packet;

namespace Rustun.Lib
{
    public class RustunPacketEncoder : MessageToByteEncoder<RustunPacket>
    {

        private RustunCrypto crypto;

        public RustunPacketEncoder(RustunCrypto crypto)
        {
            this.crypto = crypto;
        }

        protected override void Encode(IChannelHandlerContext context, RustunPacket message, IByteBuffer output)
        {
            // magic
            output.WriteBytes(BitConverter.GetBytes(message.Magic));
            // version
            output.WriteByte(message.Version);
            // type
            output.WriteByte(message.Type);
            // length
            output.WriteUnsignedShort(message.Length);
            // data
            if (message.Data != null)
            {
                output.WriteBytes(crypto.Encrypt(message.Data));
            }
        }
    }
}
