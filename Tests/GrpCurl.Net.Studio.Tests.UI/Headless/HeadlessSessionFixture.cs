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

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessSessionFixture>
{
    public const string Name = "Headless";
}
