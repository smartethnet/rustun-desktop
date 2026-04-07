using System.Text.Json.Serialization;

namespace Rustun.Lib.Message;

public class HandshakeReplyMessage
{
    [JsonPropertyName("private_ip")]
    public string PrivateIp { get; set; } = string.Empty;

    [JsonPropertyName("mask")]
    public string Mask { get; set; } = string.Empty;

    [JsonPropertyName("gateway")]
    public string Gateway { get; set; } = string.Empty;

    [JsonPropertyName("peer_details")]
    public List<PeerDetail> PeerDetails { get; set; } = [];


}