using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.ViewModels.Connections;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     PRD-005: closing a tab disposes it, so the resources a tab owns do not outlive it.
///     <para>
///         The assertion throughout is behavioural rather than a disposed flag: an invocation tab
///         subscribes to <see cref="IEnvironmentService.ActiveChanged" />, and that service is a
///         container singleton — so before this fix every closed tab stayed reachable from it for the
///         life of the process, and kept reacting. Firing the real service after the close is what
///         proves the subscription is gone; a flag would only prove <c>Dispose</c> ran.
///     </para>
///     <para>
///         What is observed is the <c>PropertyChanged</c> notification, not the preview value.
///         <c>ResolvedPreview</c> is computed on every read — it re-invokes the row's resolver — so its
///         value tracks the active environment whether or not the tab is still subscribed, and asserting
///         on it could not distinguish the two. The notification only happens because the tab's
///         <c>ActiveChanged</c> handler calls <c>RefreshResolvedPreview</c>, so it is the signal that
///         actually dies with the subscription.
///     </para>
/// </summary>
public sealed class DocumentCloseDisposalTests
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    [Fact]
    public void Closing_an_invocation_tab_unhooks_it_from_the_environment_singleton()
    {
        var (docs, env) = Create();

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];
        var row = new HeaderRowViewModel { Name = "x-region", Value = "${HOST}" };

        tab.Headers.Add(row);

        var refreshes = 0;

        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HeaderRowViewModel.ResolvedPreview))
            {
                refreshes++;
            }
        };

        // Live tab: the singleton reaches it, so the switch refreshes the row's preview binding.
        env.SetActive("e1");

        refreshes.ShouldBeGreaterThan(0, "a live tab must track the active environment");
        row.ResolvedPreview.ShouldBe("api:443");

        tab.CloseCommand.Execute(null);

        docs.Documents.ShouldBeEmpty();

        var afterClose = refreshes;

        // Closed tab: the same signal must no longer reach it. Without the close-flow disposal, or
        // without the ActiveChanged unhook in the tab's own Dispose, this still fires.
        env.SetActive(null);

        refreshes.ShouldBe(afterClose, "a closed tab must not keep tracking the active environment");
    }

    [Fact]
    public void Close_all_unhooks_every_tab_from_the_environment_singleton()
    {
        var (docs, env) = Create();

        docs.OpenInvocation(Conn(), "pkg.Svc/One", "{}");
        docs.OpenInvocation(Conn(), "pkg.Svc/Two", "{}");

        var rows = docs.Documents
            .OfType<InvocationDocumentViewModel>()
            .Select(tab =>
            {
                var row = new HeaderRowViewModel { Name = "x-region", Value = "${HOST}" };

                tab.Headers.Add(row);

                return row;
            })
            .ToList();

        rows.Count.ShouldBe(2);

        var refreshes = 0;

        foreach (var row in rows)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(HeaderRowViewModel.ResolvedPreview))
                {
                    refreshes++;
                }
            };
        }

        env.SetActive("e1");

        refreshes.ShouldBeGreaterThanOrEqualTo(2, "both live tabs must track the active environment");

        // CloseAll is the workspace-switch path and had no test at all before this.
        docs.CloseAll();

        docs.Documents.ShouldBeEmpty();

        var afterClose = refreshes;

        env.SetActive(null);

        refreshes.ShouldBe(afterClose, "no closed tab may keep tracking the active environment");
    }

    [Fact]
    public async Task Closing_a_describe_tab_mid_load_cancels_the_lookup()
    {
        var token = TestContext.Current.CancellationToken;
        var received = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Genuinely mid-load. The first version of this test used Task.FromResult, so the describe had
        // already completed during construction and "closing mid-load" was never exercised — the review
        // caught the comment claiming otherwise.
        var descriptors = new FakeDescriptorService
        {
            OnDescribe = async (connection, symbol, ct) =>
            {
                _ = connection;
                _ = received.TrySetResult(ct);

                await release.Task;

                return DescribeResult.Success(new MessageDescription(symbol, symbol, "f.proto", [], [], "{}"));
            }
        };

        var docs = Create(descriptors);

        docs.OpenDescribe(Conn(), "pkg.Alpha");

        var tab = docs.Documents[0];
        var loadToken = await received.Task.WaitAsync(Bounded, token);

        loadToken.IsCancellationRequested.ShouldBeFalse();

        Should.NotThrow(() => tab.CloseCommand.Execute(null));

        loadToken.IsCancellationRequested.ShouldBeTrue("closing a describe tab must cancel its lookup");

        docs.Documents.ShouldBeEmpty();

        release.SetResult();
    }

    /// <param name="resolveMethods">
    ///     Whether the descriptor fake answers describe calls. Off for the invocation cases on purpose:
    ///     a successful resolve repopulates <c>Headers</c>, which would discard the row those tests add
    ///     and make them assert against a collection the tab had already replaced.
    /// </param>
    private static DocumentsViewModel Create(FakeDescriptorService descriptors)
    {
        var (docs, _) = CreateWithEnvironment(descriptors);

        return docs;
    }

    private static (DocumentsViewModel Docs, EnvironmentService Env) Create(bool resolveMethods = false)
        => CreateWithEnvironment(resolveMethods
            ? new FakeDescriptorService
            {
                OnDescribe = (_, symbol, _) => Task.FromResult(
                    DescribeResult.Success(new MessageDescription(symbol, symbol, "f.proto", [], [], "{}")))
            }
            : new FakeDescriptorService());

    private static (DocumentsViewModel Docs, EnvironmentService Env) CreateWithEnvironment(FakeDescriptorService descriptors)
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

        var docs = new DocumentsViewModel(
            descriptors, new ImmediateUiDispatcher(), new FakeClipboardService(), new FakeInvocationRunner(),
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService(), environment: env);

        return (docs, env);
    }
}
