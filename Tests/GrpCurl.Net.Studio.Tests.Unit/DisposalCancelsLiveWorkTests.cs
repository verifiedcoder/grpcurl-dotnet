using GrpCurl.Net.Studio.TestSupport;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.GraphQl;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     PRD-005 review, findings 1, 2 and 4: disposal must stop work that is actually running, not only
///     release idle handles.
///     <para>
///         The original PRD-005 tests all disposed quiescent objects, which is why three defects got
///         through them: a tab's in-flight RPC is owned by the toolkit-generated command's token source,
///         not by any field the view model disposes; the capture writer's disposed check could not
///         protect a write already past it; and application shutdown never reached document disposal at
///         all. Every case here starts real work first and waits until the service has actually received
///         it, so none can pass by racing ahead of the thing it means to observe.
///     </para>
/// </summary>
public sealed class DisposalCancelsLiveWorkTests
{
    private static readonly TimeSpan Bounded = TimeSpan.FromSeconds(10);

    private static SavedConnection Conn() => new() { Name = "c", Address = "h:1" };

    [Fact]
    public async Task Disposing_an_invocation_tab_cancels_its_in_flight_call()
    {
        var token = TestContext.Current.CancellationToken;
        var received = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new FakeInvocationRunner
        {
            OnInvoke = async (InvocationRequestModel request, CancellationToken ct) =>
            {
                _ = request;
                _ = received.TrySetResult(ct);

                await release.Task;

                return new InvocationResultModel(
                    Ok: true, ResponseJson: "{}", ResponseHeaders: [], ResponseTrailers: [],
                    Status: new InvocationStatusModel(0, "OK", string.Empty),
                    Timing: new TimingModel([], 0, 0), ErrorMessage: null);
            }
        };

        var tab = CreateInvocationTab(runner);

        var invocation = tab.InvokeCommand.ExecuteAsync(null);

        // Wait until the runner actually holds the token — otherwise disposing before the call starts
        // would "pass" without proving anything.
        var callToken = await received.Task.WaitAsync(Bounded, token);

        callToken.IsCancellationRequested.ShouldBeFalse();

        tab.Dispose();

        callToken.IsCancellationRequested.ShouldBeTrue("closing a tab must cancel the call it started");

        release.SetResult();

        // And the command must actually unwind, not merely lose its UI reference.
        await invocation.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task Disposing_a_graphql_tab_cancels_its_in_flight_execution()
    {
        var token = TestContext.Current.CancellationToken;
        var received = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var graphql = new FakeGraphQlService
        {
            OnExecute = async (_, _, ct) =>
            {
                _ = received.TrySetResult(ct);

                await release.Task;

                return new GraphQlExecutionResult(Ok: true, EnvelopeJson: "{}", ConfigurationErrors: []);
            }
        };

        var tab = new GraphQlDocumentViewModel(
            Conn(), graphql, new ImmediateUiDispatcher(), new FakeClipboardService())
        {
            Document = "{ __typename }"
        };

        var execution = tab.ExecuteCommand.ExecuteAsync(null);

        var callToken = await received.Task.WaitAsync(Bounded, token);

        callToken.IsCancellationRequested.ShouldBeFalse();

        tab.Dispose();

        callToken.IsCancellationRequested.ShouldBeTrue("closing a tab must cancel the execution it started");

        release.SetResult();

        await execution.WaitAsync(Bounded, token);
    }

    [Fact]
    public async Task Disposing_the_capture_writer_mid_write_does_not_throw_at_the_pump()
    {
        var token = TestContext.Current.CancellationToken;
        var inWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Pauses inside WriteLineAsync, so disposal lands strictly between the writer's disposed check
        // and its use of the underlying writer — the interleaving a flag alone cannot cover.
        var writer = new StreamCaptureWriter(new PausingTextWriter(inWrite, release), _ => "{}");

        var write = writer.WriteAsync(new StreamEventModel(StreamEventKind.MessageReceived, 0, DateTimeOffset.UtcNow, 0, "preview"));

        await inWrite.Task.WaitAsync(Bounded, token);

        writer.Dispose();

        release.SetResult();

        // The pump's write must complete rather than fault: this is a fire-and-forget task, so an
        // ObjectDisposedException here is an unobserved teardown failure.
        await Should.NotThrowAsync(async () => await write.WaitAsync(Bounded, token));
    }

    [Fact]
    public async Task Shutdown_disposes_tabs_that_were_never_closed()
    {
        var token = TestContext.Current.CancellationToken;
        var received = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new FakeInvocationRunner
        {
            OnInvoke = async (InvocationRequestModel request, CancellationToken ct) =>
            {
                _ = request;
                _ = received.TrySetResult(ct);

                await release.Task;

                return new InvocationResultModel(
                    Ok: true, ResponseJson: "{}", ResponseHeaders: [], ResponseTrailers: [],
                    Status: new InvocationStatusModel(0, "OK", string.Empty),
                    Timing: new TimingModel([], 0, 0), ErrorMessage: null);
            }
        };

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), runner,
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService());

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];
        var invocation = tab.InvokeCommand.ExecuteAsync(null);

        // Live work is the observable. An earlier version asserted only that the collection was intact
        // and that a later Dispose did not throw — both true whether or not shutdown disposed anything,
        // so it passed with the defect present.
        var callToken = await received.Task.WaitAsync(Bounded, token);

        callToken.IsCancellationRequested.ShouldBeFalse();

        // The shutdown path, not the close path: quitting with tabs open reached no disposal at all
        // before this (review finding 4).
        var shutdown = docs.DisposeOpenDocumentsAsync(Bounded);

        callToken.IsCancellationRequested.ShouldBeTrue("shutdown must release what the open tabs own");

        // And it must still be waiting. The runner is parked and will not return until this test says
        // so, so a shutdown that only cancelled would already be finished — which is exactly what the
        // previous version of this fix did (re-review finding 1). The delay only gives that version
        // time to be wrong; the correct one cannot complete early however long we wait.
        await Task.Delay(250, token);

        shutdown.IsCompleted.ShouldBeFalse("shutdown must wait for the cancelled call to unwind");

        release.SetResult();

        var result = await shutdown.WaitAsync(Bounded, token);

        result.Drained.ShouldBeTrue("the call unwound well inside the timeout");
        result.Documents.ShouldBe(1);

        // Unwound before shutdown returned — the property the whole two-phase change exists for.
        invocation.IsCompleted.ShouldBeTrue();

        // The tabs stay in the collection on purpose — clearing them would schedule a persist that
        // overwrites the session snapshot taken moments earlier.
        _ = docs.Documents.ShouldHaveSingleItem();

        // Idempotent: a later explicit close of the same tab must not throw.
        Should.NotThrow(tab.Dispose);
    }

    /// <summary>
    ///     The honest half of the bounded policy: work that ignores its token must not keep the process
    ///     alive, and the result must say the drain did not finish rather than reporting success.
    /// </summary>
    [Fact]
    public async Task Shutdown_gives_up_on_work_that_ignores_cancellation_and_says_so()
    {
        var token = TestContext.Current.CancellationToken;
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var runner = new FakeInvocationRunner
        {
            OnInvoke = async (InvocationRequestModel request, CancellationToken ct) =>
            {
                _ = request;
                _ = ct; // deliberately ignored: this is the uncooperative operation
                _ = received.TrySetResult();

                await release.Task;

                return new InvocationResultModel(
                    Ok: true, ResponseJson: "{}", ResponseHeaders: [], ResponseTrailers: [],
                    Status: new InvocationStatusModel(0, "OK", string.Empty),
                    Timing: new TimingModel([], 0, 0), ErrorMessage: null);
            }
        };

        var docs = new DocumentsViewModel(
            new FakeDescriptorService(), new ImmediateUiDispatcher(), new FakeClipboardService(), runner,
            new FakeDialogService(), new FakeLauncherService(), new FakeRequestValidator(),
            new InMemorySettingsStore(), new FakeThemeService());

        docs.OpenInvocation(Conn(), "pkg.Svc/Go", "{}");

        var tab = (InvocationDocumentViewModel)docs.Documents[0];
        var invocation = tab.InvokeCommand.ExecuteAsync(null);

        await received.Task.WaitAsync(Bounded, token);

        // Bounded by the timeout, not by the operation: an implementation that waited unconditionally
        // would hang here and fail on the outer WaitAsync rather than passing quietly.
        var result = await docs
            .DisposeOpenDocumentsAsync(TimeSpan.FromMilliseconds(200))
            .WaitAsync(Bounded, token);

        result.Drained.ShouldBeFalse("work was still running when shutdown stopped waiting");
        result.Documents.ShouldBe(1);

        // Idempotence only. That the timed-out path still disposes the tabs is covered on the drained
        // path by the test above and holds by construction here — both run the same disposal loop —
        // but this call does not prove it, and is not claimed to.
        Should.NotThrow(tab.Dispose);

        release.SetResult();

        await invocation.WaitAsync(Bounded, token);
    }

    private static InvocationDocumentViewModel CreateInvocationTab(FakeInvocationRunner runner)
        => new(
            Conn(), "pkg.Svc/Go", "{}", runner, new FakeDescriptorService(), new ImmediateUiDispatcher(),
            new FakeClipboardService(), new FakeDialogService(), new FakeLauncherService(),
            new FakeRequestValidator());

    /// <summary>
    ///     A writer that parks inside <c>WriteLineAsync</c> until released, and — like the real
    ///     <see cref="StreamWriter" /> the product uses — throws once disposed.
    ///     <para>
    ///         The throwing part is load-bearing and was missing at first: a fake that tolerates
    ///         use-after-dispose makes this test pass against the very implementation it exists to
    ///         reject. Caught by ablating, not by reading.
    ///     </para>
    /// </summary>
    private sealed class PausingTextWriter(TaskCompletionSource entered, TaskCompletionSource release) : TextWriter
    {
        private bool _disposed;

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override async Task WriteLineAsync(string? value)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _ = entered.TrySetResult();

            await release.Task;

            // Re-checked on resume: this is the moment a disposal that landed mid-write becomes visible.
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public override Task FlushAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;

            base.Dispose(disposing);
        }
    }
}
