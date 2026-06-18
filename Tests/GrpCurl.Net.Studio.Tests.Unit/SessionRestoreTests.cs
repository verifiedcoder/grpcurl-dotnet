using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Models.Session;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>FR-146: capture the open tabs to the UI session and restore them as drafts on launch.</summary>
public sealed class SessionRestoreTests
{
    private static DocumentsViewModel Create(ISessionStore session, IWorkspaceStore workspace, InMemorySettingsStore? settings = null)
    {
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = (_, symbol, _) => Task.FromResult(
                DescribeResult.Success(new MessageDescription(symbol, symbol, "f.proto", [], [], "{}")))
        };

        return new DocumentsViewModel(
            descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(), settings ?? new InMemorySettingsStore(),
            new FakeThemeService(), workspace: workspace, session: session, sessionDebounce: TimeSpan.Zero);
    }

    private static (FakeWorkspaceStore Workspace, SavedConnection Connection) Workspace()
    {
        var connection = new SavedConnection { Id = "conn-1", Name = "prod", Address = "h:1" };
        return (new FakeWorkspaceStore(new WorkspaceModel { Id = "ws-1", Name = "W", Connections = [connection] }), connection);
    }

    [Fact]
    public void BuildSession_captures_invocation_and_describe_tabs()
    {
        var (workspace, connection) = Workspace();
        var docs = Create(new FakeSessionStore(), workspace);

        docs.OpenInvocation(connection, "pkg.Svc/Go");
        var tab = docs.Documents.OfType<InvocationDocumentViewModel>().Single();
        tab.RequestJson = "{ \"x\": 1 }";
        tab.BodyFormat = RequestBodyFormat.Text;
        tab.Deadline = "15s";
        tab.EmitDefaults = true;
        tab.Headers.Add(new GrpCurl.Net.Studio.ViewModels.Connections.HeaderRowViewModel(
            new HeaderEntry { Name = "authorization", Value = "${TOKEN}" }));
        docs.OpenDescribe(connection, "pkg.Svc");

        var state = docs.BuildSession();

        state.WorkspaceId.ShouldBe("ws-1");
        state.Tabs.Count.ShouldBe(2);

        var captured = state.Tabs[0];
        captured.Kind.ShouldBe(SessionTabKind.Invocation);
        captured.ConnectionId.ShouldBe("conn-1");
        captured.Symbol.ShouldBe("pkg.Svc/Go");
        captured.Body.ShouldBe("{ \"x\": 1 }");
        captured.BodyFormat.ShouldBe(RequestBodyFormat.Text);
        captured.Deadline.ShouldBe("15s");
        captured.EmitDefaults.ShouldBeTrue();
        captured.Headers.ShouldHaveSingleItem().Name.ShouldBe("authorization");

        state.Tabs[1].Kind.ShouldBe(SessionTabKind.Describe);
        state.Tabs[1].Symbol.ShouldBe("pkg.Svc");
    }

    [Fact]
    public void BuildSession_redacts_sensitive_header_literal_value()
    {
        var (workspace, connection) = Workspace();
        var docs = Create(new FakeSessionStore(), workspace);

        docs.OpenInvocation(connection, "pkg.Svc/Go");
        var tab = docs.Documents.OfType<InvocationDocumentViewModel>().Single();

        // A sensitive-named header typed as a literal secret (not a ${VAR} reference).
        tab.Headers.Add(new GrpCurl.Net.Studio.ViewModels.Connections.HeaderRowViewModel(
            new HeaderEntry { Name = "authorization", Value = "Bearer super-secret-token" }));

        var state = docs.BuildSession();

        var header = state.Tabs[0].Headers.ShouldHaveSingleItem();
        header.Name.ShouldBe("authorization");
        header.Value.ShouldBeEmpty();          // secret bytes never reach ui-state.json
        header.RequiresValue.ShouldBeTrue();   // restored tab re-prompts (FR-123)
    }

    [Fact]
    public void BuildSession_keeps_envvar_and_nonsensitive_header_values()
    {
        var (workspace, connection) = Workspace();
        var docs = Create(new FakeSessionStore(), workspace);

        docs.OpenInvocation(connection, "pkg.Svc/Go");
        var tab = docs.Documents.OfType<InvocationDocumentViewModel>().Single();

        // ${VAR}-referencing sensitive header and a non-sensitive literal header both pass through.
        tab.Headers.Add(new GrpCurl.Net.Studio.ViewModels.Connections.HeaderRowViewModel(
            new HeaderEntry { Name = "authorization", Value = "${TOKEN}" }));
        tab.Headers.Add(new GrpCurl.Net.Studio.ViewModels.Connections.HeaderRowViewModel(
            new HeaderEntry { Name = "x-tenant", Value = "acme" }));

        var state = docs.BuildSession();

        var headers = state.Tabs[0].Headers!;
        headers.Single(h => h.Name == "authorization").Value.ShouldBe("${TOKEN}");
        headers.Single(h => h.Name == "x-tenant").Value.ShouldBe("acme");
    }

    [Fact]
    public async Task RestoreSessionAsync_reopens_tabs_as_drafts_for_the_matching_workspace()
    {
        var (workspace, _) = Workspace();
        var session = new FakeSessionStore
        {
            State = new SessionState
            {
                WorkspaceId = "ws-1",
                ActiveTabIndex = 1,
                Tabs =
                [
                    new SessionTab(SessionTabKind.Invocation, "conn-1", "pkg.Svc/Go",
                        Body: "{ \"a\": 2 }", BodyFormat: RequestBodyFormat.Text, Deadline: "9s",
                        Headers: [new SessionHeader("authorization", "${TOKEN}", false, false)]),
                    new SessionTab(SessionTabKind.Describe, "conn-1", "pkg.Svc")
                ]
            }
        };
        var docs = Create(session, workspace);

        await docs.RestoreSessionAsync(TestContext.Current.CancellationToken);

        docs.Documents.Count.ShouldBe(2);
        var invocation = docs.Documents[0].ShouldBeOfType<InvocationDocumentViewModel>();
        invocation.MethodSymbol.ShouldBe("pkg.Svc/Go");
        invocation.RequestJson.ShouldBe("{ \"a\": 2 }");
        invocation.BodyFormat.ShouldBe(RequestBodyFormat.Text);
        invocation.Deadline.ShouldBe("9s");
        invocation.Headers.ShouldHaveSingleItem().Value.ShouldBe("${TOKEN}");
        docs.Documents[1].ShouldBeOfType<DescribeDocumentViewModel>().CurrentSymbol.ShouldBe("pkg.Svc");
        docs.SelectedDocument.ShouldBe(docs.Documents[1]); // active tab index restored
    }

    [Fact]
    public async Task RestoreSessionAsync_ignores_a_session_for_a_different_workspace()
    {
        var (workspace, _) = Workspace();
        var session = new FakeSessionStore
        {
            State = new SessionState { WorkspaceId = "other-ws", Tabs = [new SessionTab(SessionTabKind.Describe, "conn-1", "pkg.Svc")] }
        };
        var docs = Create(session, workspace);

        await docs.RestoreSessionAsync(TestContext.Current.CancellationToken);

        docs.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreSessionAsync_skips_a_tab_whose_connection_was_deleted()
    {
        var (workspace, _) = Workspace();
        var session = new FakeSessionStore
        {
            State = new SessionState
            {
                WorkspaceId = "ws-1",
                Tabs =
                [
                    new SessionTab(SessionTabKind.Describe, "gone", "pkg.Svc"),
                    new SessionTab(SessionTabKind.Describe, "conn-1", "pkg.Other")
                ]
            }
        };
        var docs = Create(session, workspace);

        await docs.RestoreSessionAsync(TestContext.Current.CancellationToken);

        docs.Documents.ShouldHaveSingleItem().ShouldBeOfType<DescribeDocumentViewModel>().CurrentSymbol.ShouldBe("pkg.Other");
    }

    [Fact]
    public async Task RestoreSessionOnStartupAsync_restores_when_the_setting_is_restore_last_workspace()
    {
        var (workspace, _) = Workspace();
        var session = new FakeSessionStore
        {
            State = new SessionState { WorkspaceId = "ws-1", Tabs = [new SessionTab(SessionTabKind.Describe, "conn-1", "pkg.Svc")] }
        };
        var settings = new InMemorySettingsStore();
        settings.Current.General.Startup = StartupBehavior.RestoreLastWorkspace;
        var docs = Create(session, workspace, settings);

        await docs.RestoreSessionOnStartupAsync(TestContext.Current.CancellationToken);

        docs.Documents.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RestoreSessionOnStartupAsync_is_a_noop_when_the_setting_is_start_empty()
    {
        var (workspace, _) = Workspace();
        var session = new FakeSessionStore
        {
            State = new SessionState { WorkspaceId = "ws-1", Tabs = [new SessionTab(SessionTabKind.Describe, "conn-1", "pkg.Svc")] }
        };
        var settings = new InMemorySettingsStore();
        settings.Current.General.Startup = StartupBehavior.StartEmpty;
        var docs = Create(session, workspace, settings);

        await docs.RestoreSessionOnStartupAsync(TestContext.Current.CancellationToken);

        docs.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task FlushSessionAsync_persists_the_current_tabs()
    {
        var (workspace, connection) = Workspace();
        var session = new FakeSessionStore();
        var docs = Create(session, workspace);

        docs.OpenDescribe(connection, "pkg.Svc");
        await docs.FlushSessionAsync(TestContext.Current.CancellationToken);

        session.LastSaved.ShouldNotBeNull();
        session.LastSaved!.WorkspaceId.ShouldBe("ws-1");
        session.LastSaved.Tabs.ShouldHaveSingleItem().Symbol.ShouldBe("pkg.Svc");
    }
}
