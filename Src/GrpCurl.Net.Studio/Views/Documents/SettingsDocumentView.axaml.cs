using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GrpCurl.Net.Studio.Views.Documents;

public sealed partial class SettingsDocumentView : UserControl
{
    public SettingsDocumentView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
