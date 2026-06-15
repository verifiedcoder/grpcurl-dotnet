using Avalonia.Threading;
using GrpCurl.Net.Studio.Services;
using GrpCurl.Net.Studio.Tests.UI.Headless;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.Views;

namespace GrpCurl.Net.Studio.Tests.UI;

/// <summary>
///     Proves the shell renders headless on every CI OS — the E0.2 acceptance criterion.
///     PR-A asserts the window shows with its title; PR-B extends this to the named zones and
///     theme switching.
/// </summary>
public sealed class ShellRenderTests(HeadlessSessionFixture fixture) : HeadlessTestBase(fixture)
{
    [Fact]
    public Task Shell_window_shows_headless_with_title() => RunOnUiThread(() =>
    {
        var viewModel = new MainWindowViewModel(new AvaloniaUiDispatcher(), new InMemorySettingsStore());
        var window = new MainWindow { DataContext = viewModel };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.Title.ShouldNotBeNullOrWhiteSpace();
        window.IsVisible.ShouldBeTrue();
    });
}
