using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AiUsage.App.Converters;

/// <summary>Maps a 0..1 fraction to a 0..360° arc sweep angle.</summary>
public sealed class FractionToSweepConverter : IValueConverter
{
    public static readonly FractionToSweepConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var f = value is double d ? d : 0.0;
        return Math.Clamp(f, 0.0, 1.0) * 360.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
