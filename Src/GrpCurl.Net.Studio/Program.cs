using Avalonia;
using GrpCurl.Net.Studio.Composition;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrpCurl.Net.Studio;

internal static class Program
{
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
            // FR-146: capture the final open-tab state on the way out (catches edits made after the last
            // debounced persist). SPEC-040 §8: release the advisory workspace lock on clean close.
            host.Services.GetRequiredService<DocumentsViewModel>().FlushSessionAsync().GetAwaiter().GetResult();
            host.Services.GetRequiredService<IWorkspaceStore>().ReleaseLock();
            host.StopAsync().GetAwaiter().GetResult();
            host.Dispose();
        }

        // Avalonia/native platform shutdown can leave a non-background thread alive after the last
        // window closes — observed on Windows as the window vanishing while the process keeps running
        // and has to be killed manually. All persistence, the workspace lock, and the host have been
        // torn down above, so force a clean process exit to guarantee termination on every platform.
        Environment.Exit(0);
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? serviceProvider)
        => AppBuilder.Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
