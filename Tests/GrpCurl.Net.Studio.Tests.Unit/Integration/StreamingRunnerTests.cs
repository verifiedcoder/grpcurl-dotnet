using System.Runtime.CompilerServices;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.Unit.Fixtures;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit.Integration;

/// <summary>
///     L2 service-layer E2E for server-streaming (E2.1 PR-A): drives the real
///     <see cref="InvocationRunner" /> over <see cref="InvocationService" />'s new streaming pipeline
///     against the in-process TestServer — the GUI-service → Core → real gRPC path.
/// </summary>
[Collection(StudioPlaintextServerCollection.Name)]
public sealed class StreamingRunnerTests(StudioPlaintextServerFixture server)
{
    private static IInvocationRunner Runner() => new InvocationRunner(new InvocationService());

    private static SavedConnection Conn(string address) => new()
    {
        Name = "test", Address = address, Transport = TransportMode.Plaintext, DescriptorMode = DescriptorMode.Reflection
    };

    private StreamRequestModel Request(string method, params HeaderEntry[] headers)
        => new(Conn(server.Address), method, headers);

    private static async IAsyncEnumerable<string> Once(string json)
    {
        yield return json;
        await Task.CompletedTask;
    }

    private async Task<List<StreamEventModel>> Collect(StreamRequestModel request, string json, CancellationToken ct)
    {
        var events = new List<StreamEventModel>();
        await foreach (var ev in Runner().InvokeStreamingAsync(request, Once(json), ct))
        {
            events.Add(ev);
        }

        return events;
    }

    [Fact]
    public async Task Server_streaming_yields_headers_then_messages_then_ok()
    {
        var events = await Collect(
            Request("testing.TestService/StreamingOutputCall"),
            """{ "response_parameters": [{ "size": 4 }, { "size": 8 }, { "size": 16 }] }""",
            TestContext.Current.CancellationToken);

        events[0].Kind.ShouldBe(StreamEventKind.Headers);

        var messages = events.Where(e => e.Kind == StreamEventKind.MessageReceived).ToList();
        messages.Count.ShouldBe(3);
        messages.Select(m => m.Index).ShouldBe([0L, 1L, 2L]);
        messages.ShouldAllBe(m => m.RawMessage != null);

        var terminal = events[^1];
        terminal.Kind.ShouldBe(StreamEventKind.Status);
        terminal.Status!.Code.ShouldBe(0);
        terminal.Error.ShouldBeNull();
    }

    [Fact]
    public async Task A_late_failure_yields_received_messages_then_an_error_status()
    {
        var events = await Collect(
            Request("testing.TestService/StreamingOutputCall", new HeaderEntry { Name = "fail-late", Value = "5" }),
            """{ "response_parameters": [{ "size": 4 }, { "size": 8 }] }""",
            TestContext.Current.CancellationToken);

        events.Count(e => e.Kind == StreamEventKind.MessageReceived).ShouldBe(2);

        var terminal = events[^1];
        terminal.Kind.ShouldBe(StreamEventKind.Status);
        terminal.Status!.Code.ShouldBe(5); // NotFound
        terminal.Error.ShouldNotBeNull();
        terminal.Error!.StatusCode.ShouldBe(5);
    }

    [Fact]
    public async Task An_early_failure_yields_an_error_status_with_no_messages()
    {
        var events = await Collect(
            Request("testing.TestService/StreamingOutputCall", new HeaderEntry { Name = "fail-early", Value = "7" }),
            """{ "response_parameters": [{ "size": 4 }] }""",
            TestContext.Current.CancellationToken);

        events.ShouldNotContain(e => e.Kind == StreamEventKind.MessageReceived);
        events[^1].Kind.ShouldBe(StreamEventKind.Status);
        events[^1].Status!.Code.ShouldBe(7); // PermissionDenied
        events[^1].Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Cancellation_mid_stream_preserves_already_received_messages()
    {
        using var cts = new CancellationTokenSource();
        var received = new List<StreamEventModel>();

        // Spaced responses so cancellation lands mid-stream after at least one message.
        var stream = Runner().InvokeStreamingAsync(
            Request("testing.TestService/StreamingOutputCall"),
            Once("""{ "response_parameters": [{ "size": 4, "interval_us": 60000 }, { "size": 4, "interval_us": 60000 }, { "size": 4, "interval_us": 60000 }, { "size": 4, "interval_us": 60000 }] }"""),
            cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var ev in stream)
            {
                received.Add(ev);
                if (ev.Kind == StreamEventKind.MessageReceived)
                {
                    cts.Cancel(); // cancel right after the first received message
                }
            }
        });

        received.ShouldContain(e => e.Kind == StreamEventKind.MessageReceived); // preserved
    }

    [Fact]
    public async Task Unknown_method_yields_a_single_error_status_row()
    {
        var events = await Collect(
            Request("testing.TestService/NoSuchStream"), "{}", TestContext.Current.CancellationToken);

        events.ShouldHaveSingleItem().Kind.ShouldBe(StreamEventKind.Status);
        events[0].Error.ShouldNotBeNull();
        events[0].Error!.Headline.ShouldContain("not found");
    }
}
