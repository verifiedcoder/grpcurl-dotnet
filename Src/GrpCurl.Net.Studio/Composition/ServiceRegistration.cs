using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrpCurl.Net.Studio.Composition;

/// <summary>
///     Registers the Studio service graph and view models with the Generic Host container.
/// </summary>
internal static class ServiceRegistration
{
    public static HostApplicationBuilder ConfigureStudioServices(this HostApplicationBuilder builder)
    {
        var services = builder.Services;

        // UI-thread + OS-edge abstractions (real dispatcher; stub the rest for the skeleton).
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
        services.AddSingleton<IDialogService, NoopDialogService>();
        services.AddSingleton<IFilePickerService, NoopFilePickerService>();
        services.AddSingleton<IClipboardService, NoopClipboardService>();

        // View models.
        services.AddSingleton<MainWindowViewModel>();

        return builder;
    }
}
