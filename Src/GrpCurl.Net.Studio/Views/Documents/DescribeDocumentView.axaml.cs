using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GrpCurl.Net.Studio.ViewModels.Documents;
using GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

namespace GrpCurl.Net.Studio.Views.Documents;

public sealed partial class DescribeDocumentView : UserControl
{
    public DescribeDocumentView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Ctrl+click on a type link opens it in a new tab (FR-051), pre-empting the in-tab navigate.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || DataContext is not DescribeDocumentViewModel viewModel)
        {
            return;
        }

        if (e.Source is not Visual visual)
        {
            return;
        }

        var button = visual as Button ?? visual.FindAncestorOfType<Button>();

        if (button is not null && button.Classes.Contains("link") && button.CommandParameter is TypeRef typeRef)
        {
            if (viewModel.OpenInNewTabCommand.CanExecute(typeRef))
            {
                viewModel.OpenInNewTabCommand.Execute(typeRef);
            }

            e.Handled = true;
        }
    }
}
