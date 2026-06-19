using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace GrpCurl.Net.Studio.Services;

/// <summary>The outcome of trying to acquire a workspace lock: either we hold it now, or a live foreign holder does.</summary>
internal sealed record WorkspaceLockAcquisition(bool Acquired, WorkspaceLockInfo? Holder);

/// <summary>
///     SPEC-040 §8: the advisory single-writer lock for a workspace file. Acquiring writes
///     <c>&lt;file&gt;.lock</c> with this process's identity; a live foreign lock blocks acquisition (the
///     caller opens read-only and may take over). A lock is stale — and silently re-acquired — when its PID
///     is no longer alive on the same machine, or it is older than 24 hours. The current process's identity
///     and clock are injected so the rules are deterministically testable.
/// </summary>
internal sealed class WorkspaceLockManager
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private readonly int _pid;
    private readonly string _machine;
    private readonly string _appVersion;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<int, bool> _isPidAlive;

    public WorkspaceLockManager(int pid, string machine, string appVersion, Func<DateTimeOffset> now, Func<int, bool> isPidAlive)
    {
        _pid = pid;
        _machine = machine;
        _appVersion = appVersion;
        _now = now;
        _isPidAlive = isPidAlive;
    }

    public static WorkspaceLockManager Default()
        => new(Environment.ProcessId, Environment.MachineName, ReadAppVersion(), () => DateTimeOffset.UtcNow, IsProcessAlive);

    public static string LockPathFor(string workspacePath) => workspacePath + ".lock";

    /// <summary>Acquires the lock unless a live foreign holder exists; a free, own, or stale lock is (re)written to self.</summary>
    public WorkspaceLockAcquisition Acquire(string workspacePath)
    {
        var lockPath = LockPathFor(workspacePath);
        var existing = Read(lockPath);

        if (existing is not null && !IsMine(existing) && !IsStale(existing))
        {
            return new WorkspaceLockAcquisition(false, existing); // a live foreign instance holds it
        }

        Write(lockPath, Self());
        return new WorkspaceLockAcquisition(true, null);
    }

    /// <summary>Rewrites the lock to this process unconditionally (the "Take over" action).</summary>
    public void TakeOver(string workspacePath) => Write(LockPathFor(workspacePath), Self());

    /// <summary>True when the on-disk lock is still ours — false once another instance has taken over.</summary>
    public bool StillOwned(string workspacePath) => Read(LockPathFor(workspacePath)) is { } info && IsMine(info);

    /// <summary>The current on-disk lock holder, or null when there is no lock.</summary>
    public static WorkspaceLockInfo? Holder(string workspacePath) => Read(LockPathFor(workspacePath));

    /// <summary>Deletes the lock if we own it (clean release); a foreign lock is left untouched.</summary>
    public void Release(string workspacePath)
    {
        var lockPath = LockPathFor(workspacePath);

        if (Read(lockPath) is { } info && IsMine(info))
        {
            Delete(lockPath);
        }
    }

    private WorkspaceLockInfo Self() => new(_pid, _machine, _now(), _appVersion);

    private bool IsMine(WorkspaceLockInfo info)
        => info.Pid == _pid && string.Equals(info.Machine, _machine, StringComparison.OrdinalIgnoreCase);

    private bool IsStale(WorkspaceLockInfo info)
        => (string.Equals(info.Machine, _machine, StringComparison.OrdinalIgnoreCase) && !_isPidAlive(info.Pid))
           || _now() - info.AcquiredUtc > MaxAge;

    private static WorkspaceLockInfo? Read(string lockPath)
    {
        if (!File.Exists(lockPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(lockPath), WorkspaceLockJsonContext.Default.WorkspaceLockInfo);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null; // a corrupt lock counts as no lock (stale), per SPEC-040 §8
        }
    }

    private static void Write(string lockPath, WorkspaceLockInfo info)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockPath))!);
        var tempPath = lockPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(info, WorkspaceLockJsonContext.Default.WorkspaceLockInfo));
        File.Move(tempPath, lockPath, overwrite: true);
    }

    private static void Delete(string lockPath)
    {
        try
        {
            File.Delete(lockPath);
        }
        catch (IOException)
        {
            // Best effort: the stale-lock rule reclaims it later if this leaves a leftover.
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false; // no such process
        }
    }

    private static string ReadAppVersion()
        => (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
