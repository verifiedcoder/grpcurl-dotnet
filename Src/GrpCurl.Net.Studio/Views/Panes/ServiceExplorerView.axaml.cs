using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GrpCurl.Net.Studio.ViewModels.Explorer;

namespace GrpCurl.Net.Studio.Views.Panes;

public sealed partial class ServiceExplorerView : UserControl
{
    public ServiceExplorerView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Double-click/Enter on a service/method/type opens its describe tab (FR-027).
    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Control { DataContext: { } context })
        {
            return;
        }

        switch (context)
        {
            case ServiceNodeViewModel service:
                Execute(service.DescribeCommand, service.FullName);
                break;
            case MethodNodeViewModel method:
                // FR-027: double-click/Enter on a method opens a new request, not describe.
                Execute(method.NewRequestCommand, method.Method.FullName);
                break;
            case TypeLeafNodeViewModel type:
                Execute(type.DescribeCommand, type.FullName);
                break;
        }
    }

    private static void Execute(ICommand command, string parameter)
    {
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }
}
