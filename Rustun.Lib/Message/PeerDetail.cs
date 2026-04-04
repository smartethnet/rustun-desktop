using System.Text.Json.Serialization;

namespace Rustun.Lib.Message;

public class PeerDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("identity")]
    public string Identity { get; set; } = string.Empty;

    [JsonPropertyName("private_ip")]
    public string PrivateIp { get; set; } = string.Empty;

    [JsonPropertyName("ciders")]
    public List<string> Ciders { get; set; } = [];

    [JsonPropertyName("ipv6")]
    public string Ipv6 { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("stun_ip")]
    public string StunIp { get; set; } = string.Empty;

    [JsonPropertyName("stun_port")]
    public int StunPort { get; set; }

    [JsonPropertyName("last_active")]
    public long LastActive { get; set; }
}
