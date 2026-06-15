using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GrpCurl.Net.Studio.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
