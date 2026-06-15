namespace GrpCurl.Net.Studio.ViewModels.Models;

/// <summary>
///     UI-framework-agnostic theme selection. Defined here (not as Avalonia's
///     <c>ThemeVariant</c>) so the ViewModels project stays free of any UI dependency; the
///     app layer's <c>ThemeManager</c> maps these onto Avalonia's theme variant.
/// </summary>
public enum AppTheme
{
    System,
    Light,
    Dark
}
