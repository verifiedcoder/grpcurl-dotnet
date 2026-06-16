using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GrpCurl.Net.Studio.Views.Connections;

public sealed partial class TlsProfileEditorView : UserControl
{
    public TlsProfileEditorView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
