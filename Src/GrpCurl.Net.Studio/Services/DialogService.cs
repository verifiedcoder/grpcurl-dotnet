using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Real <see cref="IDialogService" />: hosts dialog view models in modal windows, resolving
///     their views through the <see cref="ViewLocator" />. Message/confirm prompts are built
///     inline. The owner is the application's main window.
/// </summary>
internal sealed class DialogService : IDialogService
{
    private readonly ViewLocator _viewLocator = new();

    public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
        => ShowPromptAsync(title, message, ["OK"]);

    public async Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var choice = await ShowPromptAsync(title, message, ["Cancel", "OK"]);
        return choice == "OK";
    }

    public async Task<TResult?> ShowDialogAsync<TResult>(DialogViewModel<TResult> dialogViewModel)
    {
        var content = _viewLocator.Build(dialogViewModel);

        var window = new Window
        {
            Content = content,
            DataContext = dialogViewModel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        TResult? result = default;

        void OnClose(TResult? r)
        {
            result = r;
            window.Close();
        }

        dialogViewModel.CloseRequested += OnClose;

        try
        {
            await ShowModalAsync(window);
        }
        finally
        {
            dialogViewModel.CloseRequested -= OnClose;
        }

        return result;
    }

    private async Task<string> ShowPromptAsync(string title, string message, IReadOnlyList<string> buttons)
    {
        var chosen = buttons[0];

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            MinWidth = 320
        };

        foreach (var label in buttons)
        {
            var button = new Button { Content = label, MinWidth = 80 };
            button.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, label);
            button.Click += (_, _) =>
            {
                chosen = label;
                window.Close();
            };
            buttonRow.Children.Add(button);
        }

        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
                buttonRow
            }
        };

        await ShowModalAsync(window);
        return chosen;
    }

    private static async Task ShowModalAsync(Window window)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (owner is not null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }
}
