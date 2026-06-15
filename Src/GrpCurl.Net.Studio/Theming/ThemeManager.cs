using System.ComponentModel;
using Avalonia;
using Avalonia.Styling;
using GrpCurl.Net.Studio.ViewModels;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.Theming;

/// <summary>
///     Bridges the UI-framework-agnostic <see cref="AppTheme" /> selection on
///     <see cref="MainWindowViewModel" /> to Avalonia's <see cref="ThemeVariant" />. This is
///     the only place the enum is mapped to a UI type, keeping the ViewModels project free of
///     any Avalonia dependency. Applies the persisted theme on attach and live thereafter.
/// </summary>
internal sealed class ThemeManager
{
    private readonly Application _application;

    public ThemeManager(Application application) => _application = application;

    public void Attach(MainWindowViewModel viewModel)
    {
        Apply(viewModel.SelectedTheme);

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedTheme))
            {
                Apply(viewModel.SelectedTheme);
            }
        };
    }

    private void Apply(AppTheme theme) => _application.RequestedThemeVariant = Map(theme);

    private static ThemeVariant Map(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };
}
