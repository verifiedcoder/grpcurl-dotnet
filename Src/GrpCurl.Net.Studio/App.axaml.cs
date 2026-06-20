using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GrpCurl.Net.Studio.Theming;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;
using GrpCurl.Net.Studio.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GrpCurl.Net.Studio;

public sealed partial class App : Application
{
    private readonly IServiceProvider? _services;

    // Parameterless ctor is required by the XAML designer and the headless test app builder;
    // the runtime passes the host's service provider so the shell can be composed from DI.
    public App()
        : this(null)
    {
    }

    public App(IServiceProvider? services) => _services = services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _services is not null)
        {
            // Guarantee the process terminates once shutdown starts, even if the lifetime never returns
            // to Program.Main's cleanup (e.g. a hung disposal or a lingering native thread on Windows).
            desktop.ShutdownRequested += (_, _) => ProcessExitGuard.Arm(TimeSpan.FromSeconds(4));

            var viewModel = _services.GetRequiredService<MainWindowViewModel>();

            // Apply the persisted theme and keep it live as the shared service changes.
            new ThemeManager(this).Attach(_services.GetRequiredService<IThemeService>());

            // Apply editor font/size/indent to all AvaloniaEdit instances, live (FR-152).
            new EditorOptionsManager(this, _services.GetRequiredService<ISettingsStore>()).Attach();

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // FR-146: reopen the previously open tabs (per the FR-151 startup setting) once the UI thread and
            // dispatcher are running. Fire-and-forget: the restored tabs stream into the bound document list.
            _ = _services.GetRequiredService<DocumentsViewModel>().RestoreSessionOnStartupAsync();

            // FR-156: when the user opted into checking on launch, compare against the latest release in the
            // background (offline-safe) and surface a status-bar link if a newer version exists. No auto-apply.
            _ = CheckForUpdateOnLaunchAsync(_services, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task CheckForUpdateOnLaunchAsync(IServiceProvider services, MainWindowViewModel viewModel)
    {
        var settings = services.GetRequiredService<ISettingsStore>().Current.Updates;

        if (!settings.CheckOnLaunch)
        {
            return;
        }

        var result = await services.GetRequiredService<IUpdateService>().CheckForUpdateAsync(settings.Channel);

        if (result is { Availability: UpdateAvailability.UpdateAvailable, LatestVersion: { } version })
        {
            viewModel.ShowUpdateAvailable(version);
        }
    }
}
