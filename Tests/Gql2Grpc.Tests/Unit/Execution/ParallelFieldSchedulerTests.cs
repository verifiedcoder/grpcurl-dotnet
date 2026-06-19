using Gql2Grpc.Execution;
using Gql2Grpc.Response;
using System.Collections.Concurrent;

namespace Gql2Grpc.Tests.Unit.Execution;

public sealed class ParallelFieldSchedulerTests
{
    private static RootFieldResult Ok(string key) => new(key, null, [], Failed: false);

    private static RootFieldResult Fail(string key) => new(key, null, [], Failed: true);

    [Fact]
    public async Task Results_preserve_document_order_regardless_of_completion_order()
    {
        string[] keys = ["a", "b", "c", "d", "e"];

        var results = await ParallelFieldScheduler.RunAsync(
            keys,
            async (key, ct) =>
            {
                // Later-declared fields finish first, exercising out-of-order completion.
                await Task.Delay(key == "a" ? 30 : 1, ct);
                return Ok(key);
            },
            TestContext.Current.CancellationToken);

        results.Select(r => r.ResponseKey).ShouldBe(keys);
    }

    [Fact]
    public async Task No_progress_sink_runs_the_worker_unchanged()
    {
        var ran = 0;

        var results = await ParallelFieldScheduler.RunAsync(
            ["only"],
            (key, _) => { ran++; return Task.FromResult(Ok(key)); },
            TestContext.Current.CancellationToken);

        ran.ShouldBe(1);
        results.Single().ResponseKey.ShouldBe("only");
    }

    [Fact]
    public async Task Progress_reports_queued_for_every_field_before_any_starts()
    {
        var events = new ConcurrentQueue<FieldExecutionProgress>();
        var startedBeforeAllQueued = false;
        string[] keys = ["a", "b", "c"];

        _ = await ParallelFieldScheduler.RunAsync(
            keys,
            (key, _) =>
            {
                // If fewer than 3 queued events exist when a worker starts, queueing was not done up front.
                if (events.Count(e => e.State == FieldExecutionState.Queued) < keys.Length)
                {
                    startedBeforeAllQueued = true;
                }

                return Task.FromResult(Ok(key));
            },
            TestContext.Current.CancellationToken,
            new ImmediateProgress(events),
            key => key);

        startedBeforeAllQueued.ShouldBeFalse();

        var queued = events.Where(e => e.State == FieldExecutionState.Queued).ToList();
        queued.Select(e => e.FieldIndex).ShouldBe([0, 1, 2]);
        queued.Select(e => e.ResponseKey).ShouldBe(keys);
    }

    [Fact]
    public async Task Each_field_transitions_queued_then_inflight_then_terminal()
    {
        var events = new ConcurrentQueue<FieldExecutionProgress>();

        _ = await ParallelFieldScheduler.RunAsync(
            ["ok", "bad"],
            (key, _) => Task.FromResult(key == "bad" ? Fail(key) : Ok(key)),
            TestContext.Current.CancellationToken,
            new ImmediateProgress(events),
            key => key);

        var okStates = events.Where(e => e.ResponseKey == "ok").Select(e => e.State).ToList();
        okStates.ShouldBe([FieldExecutionState.Queued, FieldExecutionState.InFlight, FieldExecutionState.Done]);

        var badStates = events.Where(e => e.ResponseKey == "bad").Select(e => e.State).ToList();
        badStates.ShouldBe([FieldExecutionState.Queued, FieldExecutionState.InFlight, FieldExecutionState.Failed]);
    }

    [Fact]
    public async Task Terminal_events_carry_elapsed_time_and_non_terminal_do_not()
    {
        var events = new ConcurrentQueue<FieldExecutionProgress>();

        _ = await ParallelFieldScheduler.RunAsync(
            ["x"],
            (key, _) => Task.FromResult(Ok(key)),
            TestContext.Current.CancellationToken,
            new ImmediateProgress(events),
            key => key);

        foreach (var e in events)
        {
            var terminal = e.State is FieldExecutionState.Done or FieldExecutionState.Failed;
            (e.Elapsed is not null).ShouldBe(terminal);
        }
    }

    [Fact]
    public async Task A_progress_sink_without_a_response_key_selector_is_rejected()
    {
        _ = await Should.ThrowAsync<ArgumentNullException>(async () =>
            await ParallelFieldScheduler.RunAsync(
                ["a"],
                (key, _) => Task.FromResult(Ok(key)),
                TestContext.Current.CancellationToken,
                new ImmediateProgress(new ConcurrentQueue<FieldExecutionProgress>())));
    }

    private sealed class ImmediateProgress(ConcurrentQueue<FieldExecutionProgress> sink) : IProgress<FieldExecutionProgress>
    {
        public void Report(FieldExecutionProgress value) => sink.Enqueue(value);
    }
}
