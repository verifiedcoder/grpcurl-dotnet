using System.Globalization;
using Avalonia.Data.Converters;

namespace GrpCurl.Net.Studio.Converters;

/// <summary>
///     Maps an "is secret" boolean to a <see cref="char" /> mask for a header value field: a bullet
///     when secret, the null char (no masking) otherwise (FR-068).
/// </summary>
public sealed class SecretMaskConverter : IValueConverter
{
    public static readonly SecretMaskConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? '•' : '\0';

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
