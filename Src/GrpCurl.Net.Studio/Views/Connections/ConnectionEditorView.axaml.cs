using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GrpCurl.Net.Studio.Views.Connections;

public sealed partial class ConnectionEditorView : UserControl
{
    public ConnectionEditorView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
