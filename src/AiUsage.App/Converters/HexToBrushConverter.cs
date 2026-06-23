using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AiUsage.App.Converters;

// Converts a "#RRGGBB" string to a SolidColorBrush (used for accent swatches).
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex)
            && Color.TryParse(hex, out var c))
            return new SolidColorBrush(c);
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
