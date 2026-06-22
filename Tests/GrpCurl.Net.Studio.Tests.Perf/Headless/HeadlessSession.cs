using Avalonia.Headless;

namespace GrpCurl.Net.Studio.Tests.Perf.Headless;

/// <summary>
///     One <see cref="HeadlessUnitTestSession" /> (a single managed UI thread) shared across the perf
///     assembly's rendered tests. Mirrors the UI suite's harness; xUnit v3's Avalonia integration targets
///     v2, so the session is hand-driven via <see cref="HeadlessTestBase" />.
/// </summary>
public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public void Dispose()
    {
        // HeadlessUnitTestSession.Dispose() intermittently throws during teardown in the multi-assembly
        // solution run (a known harness race). Tests have already reported by now, so a disposal hiccup
        // must not fail an otherwise-green run.
        try
        {
            Session.Dispose();
        }
        catch (NullReferenceException)
        {
            // Process is exiting; the managed UI thread is torn down regardless.
        }
    }
}

[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSessionFixture>
{
    public const string Name = "Headless";
}

[Collection(HeadlessCollection.Name)]
public abstract class HeadlessTestBase(HeadlessSessionFixture fixture)
{
    /// <summary>Runs <paramref name="body" /> on the headless UI thread and awaits completion.</summary>
    protected Task RunOnUiThread(Action body) => fixture.Session.Dispatch(body, CancellationToken.None);
}
