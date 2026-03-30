using System;
using System.Collections.Generic;
using System.Text;

namespace Rustun.Lib.Packet
{
    public class RustunPacketType
    {
        public static byte Handshake = 0x01;
        public static byte HandshakeAck = 0x04;
        public static byte Data = 0x03;
        public static byte Heartbeat = 0x02;
        public static byte ProbeIpv6 = 0x06;
        public static byte ProbeHolePunch = 0x07;
    }
}
