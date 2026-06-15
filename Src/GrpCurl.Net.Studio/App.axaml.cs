using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GrpCurl.Net.Studio.ViewModels;
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
