using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GrpCurl.Net.Studio.Views.Connections;

public sealed partial class TlsProfileManagerView : UserControl
{
    public TlsProfileManagerView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
