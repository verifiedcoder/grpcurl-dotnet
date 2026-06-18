using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GrpCurl.Net.Studio.Theming;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
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
            var viewModel = _services.GetRequiredService<MainWindowViewModel>();

            // Apply the persisted theme and keep it live as the shared service changes.
            new ThemeManager(this).Attach(_services.GetRequiredService<IThemeService>());

            // Apply editor font/size/indent to all AvaloniaEdit instances, live (FR-152).
            new EditorOptionsManager(this, _services.GetRequiredService<ISettingsStore>()).Attach();

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // FR-146: reopen the previously open tabs (per the FR-151 startup setting) once the UI thread and
            // dispatcher are running. Fire-and-forget: the restored tabs stream into the bound document list.
            _ = _services.GetRequiredService<DocumentsViewModel>().RestoreSessionOnStartupAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
