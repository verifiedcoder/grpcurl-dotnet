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
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
