using System.Diagnostics;

namespace GrpCurl.Net.Studio;

/// <summary>
///     Last-resort guarantee that the process actually terminates once shutdown begins. The normal
///     exit path (Program.Main's cleanup followed by <see cref="System.Environment.Exit(int)" />) wins
///     this race in the common case and the watchdog thread dies with the process before it fires. It
///     only force-terminates if graceful shutdown hangs — e.g. a stuck disposal or a non-background
///     native thread — so the window can never close while <c>GrpCurl.Net.Studio.exe</c> lingers.
/// </summary>
internal static class ProcessExitGuard
{
    private static int _armed;

    /// <summary>Arms the watchdog once; subsequent calls are no-ops. Safe to call from any thread.</summary>
    public static void Arm(TimeSpan grace)
    {
        if (Interlocked.Exchange(ref _armed, 1) == 1)
        {
            return;
        }

        var watchdog = new Thread(() =>
        {
            Thread.Sleep(grace);

            // Still alive after the grace period: graceful exit hung. Terminate at the OS level —
            // this cannot be blocked by managed finalizers or a deadlocked thread.
            using var current = Process.GetCurrentProcess();
            current.Kill();
        })
        {
            IsBackground = true,
            Name = "studio-exit-guard",
        };

        watchdog.Start();
    }
}
