using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace AiUsage.App.Converters;

// true → uniform border of `parameter` px (default 2), false → 0. Used for the alert outline.
public sealed class BoolToThicknessConverter : IValueConverter
{
    public static readonly BoolToThicknessConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value is true;
        var px = 2.0;
        if (parameter is string p && double.TryParse(p, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            px = v;
        return new Thickness(on ? px : 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
