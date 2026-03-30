using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Rustun.Lib.Message
{
    public class HandShakeReplyMessage
    {
        [JsonPropertyName("private_ip")]
        public string PrivateIp { get; set; } = string.Empty;

        [JsonPropertyName("mask")]
        public string mask { get; set; } = string.Empty;

        [JsonPropertyName("gateway")]
        public string gateway { get; set; } = string.Empty;

        [JsonPropertyName("peer_details")]
        public List<PeerDetail> PeerDetails { get; set; } = new List<PeerDetail>();
    }
}
