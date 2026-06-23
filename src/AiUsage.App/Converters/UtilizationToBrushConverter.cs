using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AiUsage.App.Converters;

/// <summary>
/// Colours a usage bar relative to its alert threshold:
/// green well below, yellow approaching (≥75% of threshold), red at/over.
/// Inputs: [0] utilization 0..1, [1] AlertThreshold (in %, nullable; defaults to 80).
/// Brushes are pulled live from the app resources so they follow the active theme.
/// </summary>
public sealed class UtilizationToBrushConverter : IMultiValueConverter
{
    public static readonly UtilizationToBrushConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var value     = values.Count > 0 && values[0] is double v ? v : 0.0;
        var threshold = values.Count > 1 && values[1] is double t ? t : 80.0;
        if (threshold <= 0) threshold = 80.0;

        var ratio = value / (threshold / 100.0); // 1.0 == at the alert threshold

        var key = ratio >= 1.0  ? "DangerBrush"
                : ratio >= 0.75 ? "WarningBrush"
                :                 "SuccessBrush";

        return Avalonia.Application.Current?.Resources[key] as IBrush ?? Brushes.Gray;
    }
}
