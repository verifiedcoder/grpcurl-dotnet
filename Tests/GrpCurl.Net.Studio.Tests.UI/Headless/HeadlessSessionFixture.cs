using Avalonia.Headless;

namespace GrpCurl.Net.Studio.Tests.UI.Headless;

/// <summary>
///     Owns a single <see cref="HeadlessUnitTestSession" /> for the whole UI-test assembly.
///     The session hosts one managed Avalonia UI thread; tests dispatch their bodies onto it
///     via <see cref="HeadlessTestBase" />. Shared (not per-test) because the session is
///     expensive and single-threaded — see <see cref="HeadlessCollection" />.
/// </summary>
public sealed class HeadlessSessionFixture : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));

    public void Dispose()
    {
        // Avalonia's HeadlessUnitTestSession.Dispose() intermittently throws a NullReferenceException
        // during teardown (a known harness race, seen only in the multi-assembly solution run). The
        // tests have already executed and reported by this point, so a disposal hiccup must not be
        // surfaced as a spurious "collection cleanup failure" that fails an otherwise-green CI run.
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
