using Google.Protobuf.WellKnownTypes;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Runtime.CompilerServices;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class StreamRowAndPumpTests
{
    [Fact]
    public void Full_json_is_formatted_only_when_the_row_is_expanded()
    {
        var formatCount = 0;
        var ev = new StreamEventModel(StreamEventKind.MessageReceived, 0, DateTimeOffset.Now, 0, "preview", RawMessage: new Empty());
        var row = new StreamRowViewModel(ev, 0, _ => { formatCount++; return "FULL"; });

        row.FullJson.ShouldBeNull();
        formatCount.ShouldBe(0); // not formatted yet (lazy)

        row.IsExpanded = true;

        row.FullJson.ShouldBe("FULL");
        formatCount.ShouldBe(1);

        row.IsExpanded = false;
        row.IsExpanded = true;
        formatCount.ShouldBe(1); // cached — formatted once
    }

    [Theory]
    [InlineData(StreamEventKind.MessageReceived, true)]
    [InlineData(StreamEventKind.MessageSent, true)]
    [InlineData(StreamEventKind.Headers, false)]
    [InlineData(StreamEventKind.Status, false)]
    public void IsMessage_flags_message_rows(StreamEventKind kind, bool isMessage)
    {
        var row = new StreamRowViewModel(new StreamEventModel(kind, 0, DateTimeOffset.Now, 0, "x"), 0, _ => "f");
        row.IsMessage.ShouldBe(isMessage);
    }

    // ── FR-088: per-row context actions ──────────────────────────────────────

    private static StreamRowViewModel MessageRow(out FakeClipboardService clipboard, out FakeInspector inspector, long index = 3)
    {
        clipboard = new FakeClipboardService();
        inspector = new FakeInspector();
        var ev = new StreamEventModel(StreamEventKind.MessageReceived, index, DateTimeOffset.Now, 0, "preview", RawMessage: new Empty());
        var services = new StreamRowServices(clipboard, _ => "{\"compact\":true}", inspector);
        return new StreamRowViewModel(ev, 0, _ => "{ \"full\": true }", services);
    }

    [Fact]
    public async Task Copy_message_json_copies_the_pretty_body()
    {
        var row = MessageRow(out var clipboard, out _);

        row.HasMessage.ShouldBeTrue();
        row.CopyMessageJsonCommand.CanExecute(null).ShouldBeTrue();
        await row.CopyMessageJsonCommand.ExecuteAsync(null);

        clipboard.Text.ShouldBe("{ \"full\": true }");
    }

    [Fact]
    public async Task Copy_as_ndjson_copies_one_envelope_line()
    {
        var row = MessageRow(out var clipboard, out _);

        await row.CopyAsNdjsonCommand.ExecuteAsync(null);

        _ = clipboard.Text.ShouldNotBeNull();
        clipboard.Text!.ShouldContain("\"kind\":\"message\"");
        clipboard.Text.ShouldContain("\"index\":3");
        clipboard.Text.ShouldContain("\"compact\":true");
    }

    [Fact]
    public void Open_in_viewer_routes_the_message_into_the_inspector()
    {
        var row = MessageRow(out _, out var inspector, index: 7);

        row.OpenInViewerCommand.CanExecute(null).ShouldBeTrue();
        row.OpenInViewerCommand.Execute(null);

        var shown = inspector.Last.ShouldBeOfType<GrpCurl.Net.Studio.ViewModels.Panes.MessageContent>();
        shown.Title.ShouldBe("Message #7");
        shown.Json.ShouldBe("{ \"full\": true }");
    }

    [Fact]
    public void Meta_rows_cannot_copy_a_body_or_open_a_viewer()
    {
        var ev = new StreamEventModel(StreamEventKind.Status, -1, DateTimeOffset.Now, 0, "OK");
        var services = new StreamRowServices(new FakeClipboardService(), _ => "{}", new FakeInspector());
        var row = new StreamRowViewModel(ev, 0, _ => "f", services);

        row.HasMessage.ShouldBeFalse();
        row.CopyMessageJsonCommand.CanExecute(null).ShouldBeFalse();
        row.OpenInViewerCommand.CanExecute(null).ShouldBeFalse();
    }

    private static async IAsyncEnumerable<int> Range(int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<int> CountingRange(int count, Action onYield, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            onYield();
            yield return i;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Pump_applies_every_item_in_batches()
    {
        var applied = new List<int>();
        var batches = 0;

        await StreamDispatchPump.RunAsync(
            Range(100, TestContext.Current.CancellationToken),
            batch => { batches++; applied.AddRange(batch); return Task.CompletedTask; },
            TestContext.Current.CancellationToken);

        applied.Count.ShouldBe(100);
        applied.ShouldBe(Enumerable.Range(0, 100).ToList());
        batches.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Pump_surfaces_cancellation_after_applying_what_arrived()
    {
        using var cts = new CancellationTokenSource();
        var applied = new List<int>();

        _ = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await StreamDispatchPump.RunAsync(
                Range(1_000_000, cts.Token),
                batch =>
                {
                    applied.AddRange(batch);
                    if (applied.Count >= 1)
                    {
                        cts.Cancel();
                    }

                    return Task.CompletedTask;
                },
                cts.Token));

        applied.ShouldNotBeEmpty(); // already-applied items preserved
    }

    [Fact]
    public async Task Pump_backpressures_a_slow_consumer_with_a_bounded_queue()
    {
        var produced = 0;
        var firstBatchReceived = new TaskCompletionSource();
        var releaseConsumer = new TaskCompletionSource();

        var run = StreamDispatchPump.RunAsync(
            CountingRange(1000, () => Interlocked.Increment(ref produced), TestContext.Current.CancellationToken),
            async _batch =>
            {
                _ = firstBatchReceived.TrySetResult();
                await releaseConsumer.Task; // hold the UI thread so the producer cannot drain ahead
            },
            TestContext.Current.CancellationToken,
            capacity: 1);

        // Once the consumer has taken the first batch and is blocked, give the producer ample time
        // to race ahead. A bounded(1) Wait queue caps how far past the consumer it can get; the old
        // unbounded queue would let it produce all 1000 regardless of consumer speed.
        await firstBatchReceived.Task;
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Volatile.Read(ref produced).ShouldBeLessThan(1000);

        releaseConsumer.SetResult();
        await run;

        // After releasing, everything still drains through.
        Volatile.Read(ref produced).ShouldBe(1000);
    }
}
