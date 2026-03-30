using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Rustun.Lib.Message
{
    public class KeepAliveMessage
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("identity")]
        public string Identity { get; set; } = string.Empty;

        [JsonPropertyName("ipv6")]
        public string Ipv6 { get; set; } = string.Empty;

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("stun_ip")]
        public string StunIp { get; set; } = string.Empty;

        [JsonPropertyName("stun_port")]
        public int StunPort { get; set; }

        [JsonPropertyName("peer_details")]
        public List<PeerDetail> PeerDetails { get; set; } = new List<PeerDetail>();
    }
}
