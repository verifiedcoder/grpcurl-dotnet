namespace GrpCurl.Net.Studio.Tests.UI.Headless;

/// <summary>
///     Base class for headless UI tests. Runs each test body on Avalonia's managed UI thread
///     through the shared <see cref="HeadlessUnitTestSession" />, so tests may construct and
///     drive controls directly.
/// </summary>
[Collection(HeadlessCollection.Name)]
public abstract class HeadlessTestBase
{
    private readonly HeadlessSessionFixture _fixture;

    protected HeadlessTestBase(HeadlessSessionFixture fixture) => _fixture = fixture;

    /// <summary>Runs <paramref name="body" /> on the headless UI thread and awaits completion.</summary>
    protected Task RunOnUiThread(Action body) => _fixture.Session.Dispatch(body, CancellationToken.None);
}
