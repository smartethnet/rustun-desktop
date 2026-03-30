using System;
using System.Collections.Generic;
using System.Text;

namespace Rustun.Lib.Packet
{
    /// <summary>
    /// 自定义 TCP 报文
    /// | ---- magic (4 bytes) ---- | ---- version (1 byte) ---- | ---- type (1 byte) ---- | ---- payload length (2 bytes) ---- | ---- data (n bytes) ---- |
    /// </summary>
    public class RustunPacket
    {
        public UInt32 Magic { get; set; } = 0x91929394;
        public byte Version { get; set; } = 0x01;
        public byte Type { get; set; }
        public UInt16 Length { get; set; }
        public byte[]? Data { get; set; }

        public RustunPacket(byte type, byte[]? data) 
        { 
            this.Type = type;
            this.Length = (UInt16)(data?.Length ?? 0);
            this.Data = data;
        }
    }
}
