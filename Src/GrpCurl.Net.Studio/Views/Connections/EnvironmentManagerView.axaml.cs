using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GrpCurl.Net.Studio.Views.Connections;

public sealed partial class EnvironmentManagerView : UserControl
{
    public EnvironmentManagerView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
