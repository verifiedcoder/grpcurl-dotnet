using Avalonia;
using Avalonia.Media;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Theming;

/// <summary>
///     Pushes the FR-152 editor settings (font family/size, indentation) into application resources so
///     every AvaloniaEdit instance picks them up via <c>DynamicResource</c> — live, app-wide. Re-applies
///     whenever settings are persisted. App-layer only (touches Avalonia); the ViewModels stay UI-free.
/// </summary>
internal sealed class EditorOptionsManager
{
    private readonly Application _application;
    private readonly ISettingsStore _settings;

    public EditorOptionsManager(Application application, ISettingsStore settings)
    {
        _application = application;
        _settings = settings;
    }

    public void Attach()
    {
        Apply();
        _settings.Changed += (_, _) => Apply();
    }

    private void Apply()
    {
        var editor = _settings.Current.Editor;
        _application.Resources["Editor.FontFamily"] = FontFamily.Parse(editor.FontFamily);
        _application.Resources["Editor.FontSize"] = editor.FontSize;
        _application.Resources["Editor.IndentationSize"] = editor.IndentWidth;
    }
}
