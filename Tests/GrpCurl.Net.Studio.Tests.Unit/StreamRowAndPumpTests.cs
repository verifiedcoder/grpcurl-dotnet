using System.Runtime.CompilerServices;
using Google.Protobuf.WellKnownTypes;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

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

    private static async IAsyncEnumerable<int> Range(int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task Pump_applies_every_item_in_batches()
    {
        var applied = new List<int>();
        var batches = 0;

        await new StreamDispatchPump().RunAsync(
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

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await new StreamDispatchPump().RunAsync(
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
}
