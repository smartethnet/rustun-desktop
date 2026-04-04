using System.Text.Json.Serialization;

namespace Rustun.Lib.Message;

public class ProbeHolePunchMessage
{
    [JsonPropertyName("identity")]
    public string Identity { get; set; } = string.Empty;
}
