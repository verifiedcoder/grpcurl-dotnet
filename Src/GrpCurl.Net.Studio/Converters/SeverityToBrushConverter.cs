using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using System.Globalization;

namespace GrpCurl.Net.Studio.Converters;

/// <summary>
///     Maps an FR-091 <see cref="StatusSeverity" /> to its semantic status brush. The pill text
///     always carries the status name (a11y); only the colour varies by severity.
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    public static readonly SeverityToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is StatusSeverity severity
            ? severity switch
            {
                StatusSeverity.Ok => "Status.Success",
                StatusSeverity.Cancelled => "Status.Neutral",
                StatusSeverity.Transient => "Status.Transient",
                StatusSeverity.Caller => "Status.Caller",
                StatusSeverity.Server => "Status.Server",
                _ => "Status.Neutral"
            }
            : "Status.Neutral";

        if (Application.Current is { } app
            && app.TryGetResource(key, app.ActualThemeVariant, out var resource)
            && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
