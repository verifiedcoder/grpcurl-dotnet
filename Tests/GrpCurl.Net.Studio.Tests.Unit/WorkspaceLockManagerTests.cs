using GrpCurl.Net.Studio.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>SPEC-040 §8: the advisory single-writer workspace lock — acquire, stale rules, take-over, release.</summary>
public sealed class WorkspaceLockManagerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "grpcn-lock-" + Guid.NewGuid().ToString("N"));

    public WorkspaceLockManagerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string Ws => Path.Combine(_dir, "workspace.gcnws.json");

    private static WorkspaceLockManager Manager(int pid = 100, string machine = "host-a", DateTimeOffset? now = null, Func<int, bool>? alive = null)
        => new(pid, machine, "1.0", () => now ?? T0, alive ?? (_ => true));

    [Fact]
    public void Acquire_on_a_free_path_takes_the_lock()
    {
        var manager = Manager();

        var result = manager.Acquire(Ws);

        result.Acquired.ShouldBeTrue();
        result.Holder.ShouldBeNull();
        manager.StillOwned(Ws).ShouldBeTrue();
        File.Exists(WorkspaceLockManager.LockPathFor(Ws)).ShouldBeTrue();
    }

    [Fact]
    public void A_live_foreign_lock_blocks_acquisition()
    {
        _ = Manager(pid: 100).Acquire(Ws); // first instance holds it

        var second = Manager(pid: 200, alive: _ => true); // pid 100 still alive
        var result = second.Acquire(Ws);

        result.Acquired.ShouldBeFalse();
        _ = result.Holder.ShouldNotBeNull();
        result.Holder!.Pid.ShouldBe(100);
    }

    [Fact]
    public void A_lock_held_by_a_dead_pid_on_the_same_machine_is_stale_and_reacquired()
    {
        _ = Manager(pid: 100).Acquire(Ws);

        var second = Manager(pid: 200, alive: pid => pid != 100); // pid 100 is gone
        var result = second.Acquire(Ws);

        result.Acquired.ShouldBeTrue();
        second.StillOwned(Ws).ShouldBeTrue();
    }

    [Fact]
    public void A_lock_from_a_different_machine_is_not_treated_as_stale_by_pid()
    {
        _ = Manager(pid: 100, machine: "host-a").Acquire(Ws);

        // Different machine: we cannot verify the PID, so a recent lock is honoured (not stale).
        var second = Manager(pid: 100, machine: "host-b", alive: _ => false);
        var result = second.Acquire(Ws);

        result.Acquired.ShouldBeFalse();
        result.Holder!.Machine.ShouldBe("host-a");
    }

    [Fact]
    public void A_lock_older_than_24h_is_stale()
    {
        _ = Manager(now: T0).Acquire(Ws);

        var later = Manager(pid: 200, now: T0.AddHours(25));
        later.Acquire(Ws).Acquired.ShouldBeTrue();
    }

    [Fact]
    public void A_corrupt_lock_counts_as_no_lock()
    {
        File.WriteAllText(WorkspaceLockManager.LockPathFor(Ws), "{ not json");

        Manager().Acquire(Ws).Acquired.ShouldBeTrue();
    }

    [Fact]
    public void Take_over_steals_the_lock_so_the_previous_holder_no_longer_owns_it()
    {
        var first = Manager(pid: 100);
        _ = first.Acquire(Ws);

        var second = Manager(pid: 200);
        second.TakeOver(Ws);

        second.StillOwned(Ws).ShouldBeTrue();
        first.StillOwned(Ws).ShouldBeFalse(); // detects the loss
    }

    [Fact]
    public void Release_deletes_our_lock_but_leaves_a_foreign_lock_alone()
    {
        var first = Manager(pid: 100);
        _ = first.Acquire(Ws);
        first.Release(Ws);
        File.Exists(WorkspaceLockManager.LockPathFor(Ws)).ShouldBeFalse();

        _ = first.Acquire(Ws); // re-acquire
        var foreign = Manager(pid: 200);
        foreign.Release(Ws); // not ours — must not delete
        File.Exists(WorkspaceLockManager.LockPathFor(Ws)).ShouldBeTrue();
    }
}
