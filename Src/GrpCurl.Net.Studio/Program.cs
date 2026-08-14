using Avalonia;
using GrpCurl.Net.Studio.Composition;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrpCurl.Net.Studio;

internal static class Program
{
    /// <summary>
    ///     The whole document-shutdown budget (PRD-005): settling producers, persisting the session and
    ///     draining the tabs share it. Long enough for an RPC to observe its token and unwind, short
    ///     enough that a wedged operation cannot keep the process alive; exceeding it is logged.
    /// </summary>
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(5);

    // Avalonia configuration. Called by the visual designer and by the headless test harness
    // via the parameterless overload; the runtime entry point passes the host's services.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(serviceProvider: null);

    [STAThread]
    public static void Main(string[] args)
    {
        var host = Host.CreateApplicationBuilder(args)
            .ConfigureStudioServices()
            .Build();

        host.Start();

        // Load persisted settings and the workspace before the shell view model is built, so
        // the saved theme and connections are present on first paint (the view models read
        // ISettingsStore.Current / IWorkspaceStore.Current in their constructors).
        _ = host.Services.GetRequiredService<ISettingsStore>().LoadAsync().GetAwaiter().GetResult();
        _ = host.Services.GetRequiredService<IWorkspaceStore>().LoadAsync().GetAwaiter().GetResult();

        try
        {
            _ = BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // After the UI lifetime ends, Avalonia's SynchronizationContext is still installed on this
            // (main) thread but its dispatcher no longer pumps. The sync-over-async cleanup below would
            // otherwise deadlock: an await that resumes on that context posts a continuation the dead
            // dispatcher never runs, so GetResult() blocks forever — the window closes but the process
            // hangs (observed on Windows). Clearing the context makes continuations resume on the thread
            // pool instead. The try/catch keeps a shutdown hiccup from stranding the process.
            SynchronizationContext.SetSynchronizationContext(null);

            try
            {
                // FR-146: capture the final open-tab state on the way out (catches edits made after the
                // last debounced persist). SPEC-040 §8: release the advisory workspace lock on clean close.
                var documents = host.Services.GetRequiredService<DocumentsViewModel>();

                // PRD-005: one phased operation, because the phases depend on each other. Admission
                // closes and producers already in flight settle *first*, then the session is snapshotted,
                // then the documents are drained and disposed. Flushing before that wait — as this code
                // used to — could snapshot an empty collection while startup restore was still loading
                // and write it over the very session being restored (round 10, finding 1).
                //
                // Sync-over-async is safe here: the Avalonia context was cleared above, so continuations
                // resume on the thread pool.
                var shutdown = documents.ShutdownAsync(ShutdownBudget).GetAwaiter().GetResult();

                if (!shutdown.Drained || !shutdown.SessionPersisted)
                {
                    var log = host.Services.GetRequiredService<IDiagnosticsLog>();

                    if (!shutdown.Drained)
                    {
                        log.Log(
                            DiagnosticsLevel.Warning,
                            "shutdown",
                            $"Timed out after {ShutdownBudget.TotalSeconds:0.#}s waiting for work in "
                            + $"{shutdown.Documents} open tab(s) to unwind; disposing services anyway.");
                    }

                    if (!shutdown.SessionPersisted)
                    {
                        // Deliberate: the tabs on disk are the truth when a restore did not finish, and
                        // overwriting them with a mid-restore snapshot is worse than not writing at all.
                        log.Log(
                            DiagnosticsLevel.Information,
                            "shutdown",
                            "Final session snapshot skipped; the previously saved session was kept.");
                    }
                }

                host.Services.GetRequiredService<IWorkspaceStore>().ReleaseLock();
                host.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Best-effort shutdown cleanup; never let it keep the process alive.
            }
            finally
            {
                host.Dispose();
            }
        }

        // Belt-and-suspenders: even with clean cleanup, Avalonia/native shutdown can leave a
        // non-background thread alive after the last window closes. All state is persisted above, so
        // force a clean exit to guarantee termination on every platform.
        Environment.Exit(0);
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? serviceProvider)
        => AppBuilder.Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
