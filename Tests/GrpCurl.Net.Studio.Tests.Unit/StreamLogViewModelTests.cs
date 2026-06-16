using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class StreamLogViewModelTests
{
    private static StreamLogViewModel Log(int capacity = 10) => new(capacity, _ => "formatted");

    private static StreamEventModel Received(long index, long elapsedMs = 0)
        => new(StreamEventKind.MessageReceived, index, DateTimeOffset.Now, elapsedMs, $"msg {index}");

    [Fact]
    public void Append_tracks_true_counters_per_kind()
    {
        var log = Log();
        log.Append(Received(0));
        log.Append(Received(1));
        log.Append(new StreamEventModel(StreamEventKind.MessageSent, 0, DateTimeOffset.Now, 0, "out"));
        log.Append(new StreamEventModel(StreamEventKind.Headers, -1, DateTimeOffset.Now, 0, "headers"));

        log.TotalReceived.ShouldBe(2);
        log.TotalSent.ShouldBe(1);
        log.TotalRows.ShouldBe(4);
        log.Rows.Count.ShouldBe(4);
        log.IsTruncated.ShouldBeFalse();
    }

    [Fact]
    public void Ring_buffer_drops_oldest_from_view_but_keeps_true_totals()
    {
        var log = Log(capacity: 10);

        for (var i = 0; i < 50; i++)
        {
            log.Append(Received(i));
        }

        log.Rows.Count.ShouldBe(10);          // only the last 10 are visible
        log.TotalReceived.ShouldBe(50);       // true count preserved
        log.TotalRows.ShouldBe(50);
        log.IsTruncated.ShouldBeTrue();
        log.TruncationNotice.ShouldBe("showing last 10 of 50 — older rows dropped from view");
        log.Rows[^1].Index.ShouldBe(49L);     // newest retained
    }

    [Fact]
    public void Delta_is_the_gap_since_the_previous_row()
    {
        var log = Log();
        log.Append(Received(0, elapsedMs: 100));
        log.Append(Received(1, elapsedMs: 175));

        log.Rows[0].DeltaMs.ShouldBe(100);
        log.Rows[1].DeltaMs.ShouldBe(75);
    }

    [Fact]
    public void Reset_clears_rows_and_counters()
    {
        var log = Log();
        log.Append(Received(0));
        log.Reset();

        log.Rows.ShouldBeEmpty();
        log.TotalReceived.ShouldBe(0);
        log.TotalRows.ShouldBe(0);
    }
}
