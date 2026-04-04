using System.Text.Json.Serialization;

namespace Rustun.Lib.Message;

public class HandshakeMessage
{
    [JsonPropertyName("identity")]
    public string Identity { get; set; } = string.Empty;
}
