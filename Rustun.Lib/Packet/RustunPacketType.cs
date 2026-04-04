namespace Rustun.Lib.Packet;

public static class RustunPacketType
{
    public const byte Handshake = 0x01;
    public const byte HandshakeAck = 0x04;
    public const byte Data = 0x03;
    public const byte Heartbeat = 0x02;
    public const byte ProbeIpv6 = 0x06;
    public const byte ProbeHolePunch = 0x07;
}
