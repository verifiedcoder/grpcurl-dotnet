using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Panes;
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

        // UI-thread + OS-edge abstractions (real dispatcher + settings store; the remaining
        // OS-edge services are stubbed until the features that need them land).
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, NoopFilePickerService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ILauncherService, LauncherService>();

        // Connection layer (E1.1).
        services.AddSingleton<IWorkspaceStore, JsonWorkspaceStore>();
        services.AddSingleton<IConnectionRegistry, ConnectionRegistry>();

        // Descriptor/explorer layer (E1.2).
        services.AddSingleton<IConnectionSelection, ConnectionSelection>();
        services.AddSingleton<IDescriptorService, DescriptorService>();

        // Document/describe layer (E1.3) — DocumentsViewModel is the IDocumentHost.
        services.AddSingleton<DocumentsViewModel>();
        services.AddSingleton<IDocumentHost>(sp => sp.GetRequiredService<DocumentsViewModel>());

        // Invocation layer (E1.4).
        services.AddSingleton<IInvocationService, InvocationService>();
        services.AddSingleton<IInvocationRunner, InvocationRunner>();
        services.AddSingleton<IRequestValidator, RequestValidator>();

        // View models — shell root + pane placeholders.
        services.AddSingleton<ConnectionsPaneViewModel>();
        services.AddSingleton<ServiceExplorerViewModel>();
        services.AddSingleton<ConsoleViewModel>();
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return builder;
    }
}
