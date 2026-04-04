using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Channels;
using Rustun.Lib.Crypto;
using Rustun.Lib.Packet;

namespace Rustun.Lib
{
    public class RustunPacketDecoder : ByteToMessageDecoder
    {
        private const int HeaderLength = 8;
        private bool closed = false;

        private RustunCrypto crypto;

        public RustunPacketDecoder(RustunCrypto crypto)
        {
            this.crypto = crypto;
        }

        protected override void Decode(IChannelHandlerContext context, IByteBuffer input, List<object> output)
        {
            // 判断是否已关闭
            if (closed) return;

            // 确保有足够的数据读取协议头
            if (input.ReadableBytes < HeaderLength)
            {
                return; // 等待更多数据
            }

            // 标记当前读取位置，如果数据不完整可以回退
            input.MarkReaderIndex();

            // 读取魔数 (4 bytes)
            var magic = input.ReadUnsignedInt();

            // 验证魔数
            if (magic != RustunPacket.DefaultMagic)
            {
                closed = true;
                context.CloseAsync();
                throw new CorruptedFrameException($"Invalid magic number: {magic}");
            }

            // 读取版本号 (1 byte)
            var version = input.ReadByte();

            // 验证版本号
            if (version != RustunPacket.DefaultVersion)
            {
                closed = true;
                context.CloseAsync();
                throw new CorruptedFrameException($"Invalid version: {version}");
            }

            // 读取消息类型 (1 byte)
            var type = input.ReadByte();

            // 读取数据长度 (2 bytes，最大 65535；协议头未预留更大长度字段)
            var length = input.ReadUnsignedShort();

            // 检查是否有足够的数据体
            if (input.ReadableBytes < length)
            {
                input.ResetReaderIndex(); // 数据不完整，回退到标记位置
                return; // 等待更多数据
            }

            // 读取数据部分
            var data = new byte[length];
            input.ReadBytes(data);

            // 解密数据
            data = crypto.Decrypt(data);

            // 创建协议消息对象
            var packet = new RustunPacket(type, data);

            // 将解码后的消息添加到输出列表，传递给下一个处理器
            output.Add(packet);
        }
    }
}
