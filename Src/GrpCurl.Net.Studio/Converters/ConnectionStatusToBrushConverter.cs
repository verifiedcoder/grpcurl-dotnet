using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.Converters;

/// <summary>
///     Maps a <see cref="ConnectionStatus" /> to the matching semantic <c>Conn.*</c> brush so the
///     sidebar status dot follows the theme (SPEC-020 §3.2).
/// </summary>
public sealed class ConnectionStatusToBrushConverter : IValueConverter
{
    public static readonly ConnectionStatusToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ConnectionStatus.Connected => "Conn.Connected",
            ConnectionStatus.Connecting => "Conn.Connecting",
            ConnectionStatus.Error => "Conn.Failed",
            _ => "Conn.Idle"
        };

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
