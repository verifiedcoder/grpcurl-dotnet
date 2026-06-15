using Avalonia;
using GrpCurl.Net.Studio.Composition;
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
        using var host = Host.CreateApplicationBuilder(args)
            .ConfigureStudioServices()
            .Build();

        host.Start();

        // Load persisted settings before the shell view model is built, so the saved theme
        // is reflected on first paint (the view model reads ISettingsStore.Current in its ctor).
        host.Services.GetRequiredService<ISettingsStore>().LoadAsync().GetAwaiter().GetResult();

        try
        {
            BuildAvaloniaApp(host.Services)
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider? serviceProvider)
        => AppBuilder.Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
