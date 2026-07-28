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
    // WithInterFont mirrors Program.BuildAvaloniaApp: it registers the embedded font collection the app
    // actually renders with, so text layout never has to fall back to enumerating the host's system fonts.
    // On a headless Windows runner that fallback is what surfaced as an intermittent
    // KeyNotFoundException 'fonts:SystemFonts' out of FontManager while a Window was being constructed.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont();
}
