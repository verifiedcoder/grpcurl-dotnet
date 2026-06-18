using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Services.Secrets;
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
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IRevealGate, RevealGate>();
        services.AddSingleton<ILauncherService, LauncherService>();
        services.AddSingleton<IProtocService, ProtocService>();
        services.AddSingleton<IUpdateService, UpdateService>(); // FR-156: version + releases URL for Settings → Updates

        // Diagnostics log (FR-155 / SPEC-030 §9): rolling NDJSON sink + a Microsoft.Extensions.Logging
        // provider, so ILogger output is captured for Settings → Diagnostics.
        services.AddSingleton<IDiagnosticsLog, FileDiagnosticsLog>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, DiagnosticsLoggerProvider>();

        // Connection layer (E1.1). The secret store picks its backend once at startup and logs the choice
        // (backend name only, SEC-025/#10) to the diagnostics log; the live backend is surfaced in
        // Settings → Security (SEC-024).
        services.AddSingleton<ISecretStore>(sp => new SecretStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrpCurlNet.Studio"),
            log: message => sp.GetRequiredService<IDiagnosticsLog>()
                .Log(ViewModels.Models.Diagnostics.DiagnosticsLevel.Information, "SecretStore", message)));

        services.AddSingleton<IWorkspaceStore, JsonWorkspaceStore>();

        // UI session (FR-146): machine-local record of open tabs, restored on launch per the startup setting.
        services.AddSingleton<ISessionStore, JsonSessionStore>();

        // History (E3.3): append-only NDJSON store of redacted invocations + the recorder (SPEC-040 §5).
        services.AddSingleton<IHistoryStore, JsonHistoryStore>();
        services.AddSingleton<IHistoryRecorder, HistoryRecorder>();

        // Environments (E3.2): ${VAR} resolution, active env → OS, secrets via ISecretStore (FR-130..134).
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IEnvironmentStore, EnvironmentStore>(); // PR-B: workspace-level CRUD over environments

        // Saved requests (FR-145): workspace-level CRUD over named invocation requests (sidebar + tabs).
        services.AddSingleton<ISavedRequestStore, SavedRequestStore>();
        services.AddSingleton<ISavedRequestSnippetIO, SavedRequestSnippetIO>(); // FR-166: single-request snippet export/import

        // TLS profile resolution (E2.2): turns a connection's profile reference + the PKCS12
        // password secret into the (profile, password) pair the channel mapper consumes.
        services.AddSingleton<ITlsProfileResolver, TlsProfileResolver>();
        services.AddSingleton<ITlsProfileStore, TlsProfileStore>();
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
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<IInspector>(sp => sp.GetRequiredService<InspectorViewModel>());
        services.AddSingleton<ConsoleViewModel>();
        services.AddSingleton<WorkspaceSessionViewModel>(); // E3.1: workspace status + save/reload (wired to the shell in PR-D)
        services.AddSingleton<EnvironmentSwitcherViewModel>(); // E3.2: status-bar environment switcher (FR-133/138)
        services.AddSingleton<MainWindowViewModel>();

        return builder;
    }
}
