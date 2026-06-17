using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     FR-066/FR-133 (follow-up #7): an invocation tab's header previews resolve <c>${VAR}</c> against the
///     active workspace environment, and switching the active environment refreshes those previews.
/// </summary>
public sealed class InvocationEnvironmentPreviewTests
{
    private static (InvocationDocumentViewModel Tab, EnvironmentService Env) Build()
    {
        var workspace = new FakeWorkspaceStore(new WorkspaceModel
        {
            Environments =
            [
                new WorkspaceEnvironment
                {
                    Id = "e1", Name = "staging",
                    Variables = [new EnvironmentVariable { Name = "HOST", Value = StringOrSecret.Plain("api:443") }]
                }
            ]
        });
        var env = new EnvironmentService(workspace, new FakeSecretStore());
        var tab = new InvocationDocumentViewModel(
            new SavedConnection { Name = "c", Address = "h:1" }, "pkg.Svc/Go", "{}",
            new FakeInvocationRunner(), new FakeDescriptorService(), new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator(), environment: env);
        return (tab, env);
    }

    [Fact]
    public void A_header_preview_resolves_against_the_active_environment()
    {
        var (tab, env) = Build();
        tab.Headers.Add(new HeaderRowViewModel { Name = "x-region", Value = "${HOST}" });

        // No active environment → falls back to the OS (unset here).
        tab.Headers[0].ResolvedPreview.ShouldBe("<unset:HOST>");

        env.SetActive("e1");

        tab.Headers[0].ResolvedPreview.ShouldBe("api:443"); // active env value
    }

    [Fact]
    public void Switching_the_active_environment_refreshes_open_header_previews()
    {
        var (tab, env) = Build();
        var row = new HeaderRowViewModel { Name = "x-region", Value = "${HOST}" };
        tab.Headers.Add(row);
        env.SetActive("e1");

        var raised = 0;
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HeaderRowViewModel.ResolvedPreview)) raised++; };

        env.SetActive(null); // FR-133: switching refreshes visible previews

        raised.ShouldBeGreaterThan(0);
        row.ResolvedPreview.ShouldBe("<unset:HOST>"); // back to OS-only
    }
}
