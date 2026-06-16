using System.Text;
using System.Text.Json;
using Google.Protobuf;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Renders a streaming event as one NDJSON line (FR-086/087). Message rows match the CLI's
///     <c>--output json</c> envelope shape (<c>{"kind":"message","index":N,"timestamp":"…","message":{…}}</c>)
///     so captures/exports are machine-consumable parity anchors; meta rows carry their own envelope.
///     The message body is embedded as raw JSON (already-formatted compact), never re-quoted.
/// </summary>
public static class NdjsonStreamFormatter
{
    public static string Format(StreamEventModel ev, Func<IMessage, string> compactFormat)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", Kind(ev.Kind));

            if (ev.Kind is StreamEventKind.MessageReceived or StreamEventKind.MessageSent)
            {
                writer.WriteNumber("index", ev.Index);
            }

            writer.WriteString("timestamp", ev.WallClock.ToString("O"));

            if (ev.RawMessage is { } message)
            {
                writer.WritePropertyName("message");
                writer.WriteRawValue(compactFormat(message));
            }
            else if (ev.Status is { } status)
            {
                writer.WriteNumber("code", status.Code);
                writer.WriteString("status", status.CodeName);

                if (!string.IsNullOrEmpty(status.Detail))
                {
                    writer.WriteString("message", status.Detail);
                }
            }
            else if (!string.IsNullOrEmpty(ev.Preview))
            {
                writer.WriteString("message", ev.Preview);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Kind(StreamEventKind kind) => kind switch
    {
        StreamEventKind.MessageReceived => "message",
        StreamEventKind.MessageSent => "sent",
        StreamEventKind.Headers => "headers",
        StreamEventKind.Status => "status",
        StreamEventKind.Warning => "warning",
        _ => "event"
    };
}
