using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Rustun.Lib.Message
{
    public class ProbeIpv6Message
    {
        [JsonPropertyName("identity")]
        public string Identity { get; set; } = string.Empty;
    }
}
