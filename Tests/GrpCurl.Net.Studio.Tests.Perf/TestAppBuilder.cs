using Avalonia;
using Avalonia.Headless;

namespace GrpCurl.Net.Studio.Tests.Perf;

/// <summary>
///     Headless entry point for the perf suite's rendered assertions. Builds the real <see cref="App" />
///     on Avalonia's headless platform — no display server, runs on every OS — so the virtualization tests
///     measure the actual control templates that ship.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
