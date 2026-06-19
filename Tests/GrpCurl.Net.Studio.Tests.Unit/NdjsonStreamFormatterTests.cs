using Google.Protobuf.WellKnownTypes;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class NdjsonStreamFormatterTests
{
    [Fact]
    public void Message_row_matches_the_cli_ndjson_envelope_with_raw_body()
    {
        var ev = new StreamEventModel(StreamEventKind.MessageReceived, 7, DateTimeOffset.UnixEpoch, 0, "preview", RawMessage: new Empty());

        var line = NdjsonStreamFormatter.Format(ev, _ => """{ "echo": 42 }""");

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        root.GetProperty("kind").GetString().ShouldBe("message");
        root.GetProperty("index").GetInt32().ShouldBe(7);
        root.GetProperty("timestamp").GetString().ShouldNotBeNullOrEmpty();
        root.GetProperty("message").GetProperty("echo").GetInt32().ShouldBe(42); // embedded raw, not re-quoted
    }

    [Fact]
    public void Sent_row_uses_the_sent_kind()
    {
        var ev = new StreamEventModel(StreamEventKind.MessageSent, 0, DateTimeOffset.UnixEpoch, 0, "p", RawMessage: new Empty());
        var line = NdjsonStreamFormatter.Format(ev, _ => "{}");

        JsonDocument.Parse(line).RootElement.GetProperty("kind").GetString().ShouldBe("sent");
    }

    [Fact]
    public void Status_row_carries_code_and_status()
    {
        var ev = new StreamEventModel(StreamEventKind.Status, -1, DateTimeOffset.UnixEpoch, 0, "x",
            Status: new InvocationStatusModel(5, "NotFound", "missing"));

        var line = NdjsonStreamFormatter.Format(ev, _ => "{}");

        using var doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("kind").GetString().ShouldBe("status");
        doc.RootElement.GetProperty("code").GetInt32().ShouldBe(5);
        doc.RootElement.GetProperty("message").GetString().ShouldBe("missing");
    }

    [Fact]
    public void Every_line_is_valid_standalone_json()
    {
        var ev = new StreamEventModel(StreamEventKind.Headers, -1, DateTimeOffset.UnixEpoch, 0, "headers (2)");
        var line = NdjsonStreamFormatter.Format(ev, _ => "{}");

        line.ShouldNotContain("\n");
        _ = Should.NotThrow(() => JsonDocument.Parse(line));
    }
}
