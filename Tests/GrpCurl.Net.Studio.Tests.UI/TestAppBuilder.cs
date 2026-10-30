using Avalonia;
using Avalonia.Headless;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Entry point for the headless test session. Builds the real <see cref="App" /> (with a
///     null service provider, so it skips desktop main-window creation) on Avalonia's headless
///     platform — no display server needed on any OS.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = Environment.GetEnvironmentVariable("GRPCN_TRUST_SCREENSHOT_DIR") is not null
            || Environment.GetEnvironmentVariable("GRPCN_IMPORT_SCREENSHOT_DIR") is not null;
        var builder = AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
        return capture ? builder.UseSkia() : builder;
    }
}
