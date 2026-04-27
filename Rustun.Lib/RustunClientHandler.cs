using DotNetty.Transport.Channels;
using Rustun.Lib.Message;
using Rustun.Lib.Packet;
using Serilog;
using System.Text;
using System.Text.Json;

namespace Rustun.Lib;

public class RustunClientHandler : SimpleChannelInboundHandler<RustunPacket>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly RustunClient _client;

    public RustunClientHandler(RustunClient client)
    {
        _client = client;
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, RustunPacket msg)
    {
        Log.Debug($"Receive packet: Magic={BitConverter.ToString(BitConverter.GetBytes(msg.Magic))} Version={msg.Version} Type={msg.Type} DataLength={msg.Length}");
        switch (msg.Type)
        {
            case RustunPacketType.Handshake:
                DispatchJson<HandshakeMessage>(msg.Data, _client.onHandshakeMessage, "Handshake");
                break;
            case RustunPacketType.HandshakeAck:
                DispatchJson<HandshakeReplyMessage>(msg.Data, _client.onHandshakeReplyMessage, "HandshakeAck");
                break;
            case RustunPacketType.Data:
                HandleDataPacket(msg.Data);
                break;
            case RustunPacketType.Heartbeat:
                DispatchJson<KeepAliveMessage>(msg.Data, _client.onKeepAliveMessage, "Heartbeat");
                break;
            case RustunPacketType.ProbeIpv6:
                DispatchJson<ProbeIpv6Message>(msg.Data, _client.onProbeIpv6Message, "ProbeIpv6");
                break;
            case RustunPacketType.ProbeHolePunch:
                DispatchJson<ProbeHolePunchMessage>(msg.Data, _client.onProbeHolePunchMessage, "ProbeHolePunch");
                break;
            default:
                _ = _client.onError(new NotSupportedException($"Unsupported packet type: 0x{msg.Type:X2}."));
                break;
        }
    }

    private void DispatchJson<T>(byte[]? data, Func<T, Task> notify, string label)
    {
        if (data is not { Length: > 0 })
        {
            _ = _client.onError(new InvalidOperationException($"{label}: empty payload."));
            return;
        }
        Log.Debug($"{label}: {Encoding.UTF8.GetString(data)}");

        try
        {
            var model = JsonSerializer.Deserialize<T>(data, JsonOptions);
            if (model is null)
            {
                _ = _client.onError(new InvalidOperationException($"{label}: JSON deserialized to null."));
                return;
            }

            _ = notify(model);
        }
        catch (JsonException ex)
        {
            _ = _client.onError(new InvalidOperationException($"{label}: invalid JSON.", ex));
        }
    }

    private void HandleDataPacket(byte[]? data)
    {
        if (data is not { Length: > 0 })
        {
            _ = _client.onError(new InvalidOperationException("Data: empty payload."));
            return;
        }

        _ = _client.onDataMessage(data);
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        _ = _client.onError(exception);
        _ = context.CloseAsync();
    }

    public override void ChannelActive(IChannelHandlerContext context)
    {
        var handshakeMessage = new HandshakeMessage { Identity = _client.Identity };
        var json = JsonSerializer.Serialize(handshakeMessage, JsonOptions);
        var handshakePacket = new RustunPacket(RustunPacketType.Handshake, Encoding.UTF8.GetBytes(json));
        _ = context.WriteAndFlushAsync(handshakePacket);
        Log.Information($"Send handshake message: {json}");

        _ = _client.onConnected();
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        _ = _client.onDisconnected();
        _ = context.CloseAsync();
    }
}
