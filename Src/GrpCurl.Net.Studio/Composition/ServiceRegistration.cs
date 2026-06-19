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
        _ = services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        _ = services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        _ = services.AddSingleton<IThemeService, ThemeService>();
        _ = services.AddSingleton<IDialogService, DialogService>();
        _ = services.AddSingleton<IFilePickerService, FilePickerService>();
        _ = services.AddSingleton<IClipboardService, ClipboardService>();
        _ = services.AddSingleton<IRevealGate, RevealGate>();
        _ = services.AddSingleton<ILauncherService, LauncherService>();
        _ = services.AddSingleton<IProtocService, ProtocService>();
        _ = services.AddSingleton<IUpdateService, UpdateService>(); // FR-156: version + releases URL for Settings → Updates

        // Diagnostics log (FR-155 / SPEC-030 §9): rolling NDJSON sink + a Microsoft.Extensions.Logging
        // provider, so ILogger output is captured for Settings → Diagnostics.
        _ = services.AddSingleton<IDiagnosticsLog, FileDiagnosticsLog>();
        _ = services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, DiagnosticsLoggerProvider>();

        // Connection layer (E1.1). The secret store picks its backend once at startup and logs the choice
        // (backend name only, SEC-025/#10) to the diagnostics log; the live backend is surfaced in
        // Settings → Security (SEC-024).
        _ = services.AddSingleton<ISecretStore>(sp => new SecretStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrpCurlNet.Studio"),
            log: message => sp.GetRequiredService<IDiagnosticsLog>()
                .Log(ViewModels.Models.Diagnostics.DiagnosticsLevel.Information, "SecretStore", message)));

        _ = services.AddSingleton<IWorkspaceStore, JsonWorkspaceStore>();

        // UI session (FR-146): machine-local record of open tabs, restored on launch per the startup setting.
        _ = services.AddSingleton<ISessionStore, JsonSessionStore>();

        // History (E3.3): append-only NDJSON store of redacted invocations + the recorder (SPEC-040 §5).
        _ = services.AddSingleton<IHistoryStore, JsonHistoryStore>();
        _ = services.AddSingleton<IHistoryRecorder, HistoryRecorder>();

        // Environments (E3.2): ${VAR} resolution, active env → OS, secrets via ISecretStore (FR-130..134).
        _ = services.AddSingleton<IEnvironmentService, EnvironmentService>();
        _ = services.AddSingleton<IEnvironmentStore, EnvironmentStore>(); // PR-B: workspace-level CRUD over environments

        // Saved requests (FR-145): workspace-level CRUD over named invocation requests (sidebar + tabs).
        _ = services.AddSingleton<ISavedRequestStore, SavedRequestStore>();
        _ = services.AddSingleton<ISavedRequestSnippetIO, SavedRequestSnippetIO>(); // FR-166: single-request snippet export/import

        // TLS profile resolution (E2.2): turns a connection's profile reference + the PKCS12
        // password secret into the (profile, password) pair the channel mapper consumes.
        _ = services.AddSingleton<ITlsProfileResolver, TlsProfileResolver>();
        _ = services.AddSingleton<ITlsProfileStore, TlsProfileStore>();
        _ = services.AddSingleton<IConnectionRegistry, ConnectionRegistry>();

        // Descriptor/explorer layer (E1.2).
        _ = services.AddSingleton<IConnectionSelection, ConnectionSelection>();
        _ = services.AddSingleton<IDescriptorService, DescriptorService>();

        // Document/describe layer (E1.3) — DocumentsViewModel is the IDocumentHost.
        _ = services.AddSingleton<DocumentsViewModel>();
        _ = services.AddSingleton<IDocumentHost>(sp => sp.GetRequiredService<DocumentsViewModel>());

        // Invocation layer (E1.4).
        _ = services.AddSingleton<IInvocationService, InvocationService>();
        _ = services.AddSingleton<IInvocationRunner, InvocationRunner>();
        _ = services.AddSingleton<IRequestValidator, RequestValidator>();

        // GraphQL layer (P4 / SPEC-015) — the single seam over the Gql2Grpc bridge.
        _ = services.AddSingleton<IGraphQlService, GraphQlService>();

        // View models — shell root + pane placeholders.
        _ = services.AddSingleton<ConnectionsPaneViewModel>();
        _ = services.AddSingleton<ServiceExplorerViewModel>();
        _ = services.AddSingleton<InspectorViewModel>();
        _ = services.AddSingleton<IInspector>(sp => sp.GetRequiredService<InspectorViewModel>());
        _ = services.AddSingleton<ConsoleViewModel>();
        _ = services.AddSingleton<WorkspaceSessionViewModel>(); // E3.1: workspace status + save/reload (wired to the shell in PR-D)
        _ = services.AddSingleton<EnvironmentSwitcherViewModel>(); // E3.2: status-bar environment switcher (FR-133/138)
        _ = services.AddSingleton<MainWindowViewModel>();

        return builder;
    }
}
