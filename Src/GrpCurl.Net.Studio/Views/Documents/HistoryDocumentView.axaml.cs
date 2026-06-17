using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GrpCurl.Net.Studio.ViewModels.Documents;

namespace GrpCurl.Net.Studio.Views.Documents;

public sealed partial class HistoryDocumentView : UserControl
{
    public HistoryDocumentView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // FR-123: double-click a row to replay it into a new invocation tab.
    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: HistoryRowViewModel row }
            && DataContext is HistoryDocumentViewModel vm
            && vm.ReplayCommand.CanExecute(row))
        {
            vm.ReplayCommand.Execute(row);
        }
    }
}
