namespace Rustun.Lib.Packet;

/// <summary>
/// 自定义 TCP 报文
/// | ---- magic (4 bytes) ---- | ---- version (1 byte) ---- | ---- type (1 byte) ---- | ---- payload length (2 bytes) ---- | ---- data (n bytes) ---- |
/// </summary>
public class RustunPacket
{
    public const uint DefaultMagic = 0x91929394;
    public const byte DefaultVersion = 0x01;

    public uint Magic { get; set; } = DefaultMagic;
    public byte Version { get; set; } = DefaultVersion;
    public byte Type { get; set; }
    public ushort Length { get; set; }
    public byte[]? Data { get; set; }

    public RustunPacket(byte type, byte[]? data)
    {
        Type = type;
        Length = (ushort)(data?.Length ?? 0);
        Data = data;
    }
}
