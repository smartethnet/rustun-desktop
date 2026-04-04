using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using Rustun.Lib.Crypto;
using Rustun.Lib.Packet;

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
        // 与 BitConverter.GetBytes(uint) 在 little-endian 下一致，避免每次分配 4 字节数组
        WriteUInt32LittleEndian(output, message.Magic);
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
    }

    private static void WriteUInt32LittleEndian(IByteBuffer output, uint value)
    {
        output.WriteByte((byte)value);
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 24));
    }
}
