using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using Rustun.Lib.Crypto;
using Rustun.Lib.Packet;
using Serilog;

namespace Rustun.Lib;

public class RustunPacketEncoder : MessageToByteEncoder<RustunPacket>
{
    private readonly RustunCrypto crypto;

    public RustunPacketEncoder(RustunCrypto crypto)
    {
        this.crypto = crypto;
    }

    protected override void Encode(IChannelHandlerContext context, RustunPacket message, IByteBuffer output)
    {
        WriteUInt32(output, message.Magic);
        output.WriteByte(message.Version);
        output.WriteByte(message.Type);

        var plain = message.Data ?? [];
        var cipher = crypto.Encrypt(plain);
        if (cipher.Length > ushort.MaxValue)
        {
            throw new EncoderException($"Encrypted payload length {cipher.Length} exceeds {ushort.MaxValue}.");
        }

        output.WriteUnsignedShort((ushort)cipher.Length);
        output.WriteBytes(cipher);
        Log.Debug("Encoded packet: Magic={Magic}, Version={Version}, Type={Type}, Length={Length}, Data={Data}",
            message.Magic, message.Version, message.Type, cipher.Length, BitConverter.ToString(cipher));
    }

    private static void WriteUInt32(IByteBuffer output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }
}
