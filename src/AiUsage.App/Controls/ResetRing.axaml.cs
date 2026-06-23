using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiUsage.App.Controls;

/// <summary>
/// Circular countdown ring: the arc fills (0→360°) as a usage window elapses toward its reset.
/// Used both inline (next to a bar) and standalone (small tiles).
/// </summary>
public partial class ResetRing : UserControl
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<ResetRing, double>(nameof(Fraction));

    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<ResetRing, double>(nameof(Diameter), 18);

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ResetRing, double>(nameof(Thickness), 3);

    public static readonly StyledProperty<IBrush?> RingBrushProperty =
        AvaloniaProperty.Register<ResetRing, IBrush?>(nameof(RingBrush));

    public static readonly StyledProperty<string> CenterTextProperty =
        AvaloniaProperty.Register<ResetRing, string>(nameof(CenterText), "");

    public static readonly StyledProperty<double> CenterFontSizeProperty =
        AvaloniaProperty.Register<ResetRing, double>(nameof(CenterFontSize), 10);

    public ResetRing() => InitializeComponent();

    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double Diameter { get => GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }
    public double Thickness { get => GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
    public string CenterText { get => GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }
    public double CenterFontSize { get => GetValue(CenterFontSizeProperty); set => SetValue(CenterFontSizeProperty, value); }
}
