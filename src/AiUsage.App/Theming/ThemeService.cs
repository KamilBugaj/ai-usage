using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using AiUsage.Core.Config;

namespace AiUsage.App.Theming;

/// <summary>
/// Drives the app palette at runtime. Colours live as keyed SolidColorBrush resources
/// (defined in App.axaml); views bind to them with {DynamicResource ...} so Apply()
/// updates the whole UI live — enabling the settings colour preview.
/// </summary>
public static class ThemeService
{
    private sealed record Palette(
        string Bg, string Surface, string Surface2, string Track,
        string Text, string Subtext, string Muted, string Faint,
        string Accent, string Success, string Warning, string Danger, bool Dark);

    public static readonly IReadOnlyList<string> Presets = ["Mocha", "Latte", "Nord"];

    // Accent options offered as swatches in settings; override the preset's accent.
    public static readonly IReadOnlyList<string> AccentSwatches =
        ["#89B4FA", "#A6E3A1", "#F38BA8", "#CBA6F7", "#FAB387", "#94E2D5", "#F9E2AF"];

    private static readonly Dictionary<string, Palette> _palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Catppuccin Mocha — current dark palette
        ["Mocha"] = new("#1E1E2E", "#313244", "#45475A", "#45475A", "#CDD6F4", "#BAC2DE",
                        "#6C7086", "#585B70", "#89B4FA", "#A6E3A1", "#F9E2AF", "#F38BA8", true),
        // Catppuccin Latte — light
        ["Latte"] = new("#EFF1F5", "#E6E9EF", "#CCD0DA", "#CCD0DA", "#4C4F69", "#5C5F77",
                        "#8C8FA1", "#9CA0B0", "#1E66F5", "#40A02B", "#DF8E1D", "#D20F39", false),
        // Nord — dark, bluish
        ["Nord"]  = new("#2E3440", "#3B4252", "#434C5E", "#434C5E", "#ECEFF4", "#D8DEE9",
                        "#7B88A1", "#616E88", "#88C0D0", "#A3BE8C", "#EBCB8B", "#BF616A", true),
    };

    public static void Apply(ThemeConfig? cfg)
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;

        var preset = cfg?.Preset ?? "Mocha";
        if (!_palettes.TryGetValue(preset, out var p)) p = _palettes["Mocha"];
        var accent = string.IsNullOrWhiteSpace(cfg?.Accent) ? p.Accent : cfg!.Accent!;

        Set(app, "BgBrush", p.Bg);
        Set(app, "SurfaceBrush", p.Surface);
        Set(app, "Surface2Brush", p.Surface2);
        Set(app, "TrackBrush", p.Track);
        Set(app, "TextBrush", p.Text);
        Set(app, "SubtextBrush", p.Subtext);
        Set(app, "MutedBrush", p.Muted);
        Set(app, "FaintBrush", p.Faint);
        Set(app, "AccentBrush", accent);
        Set(app, "SuccessBrush", p.Success);
        Set(app, "WarningBrush", p.Warning);
        Set(app, "DangerBrush", p.Danger);
        Set(app, "OnAccentBrush", p.Bg); // text/icon colour on accent-filled buttons

        app.RequestedThemeVariant = p.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static void Set(Avalonia.Application app, string key, string hex)
        => app.Resources[key] = new SolidColorBrush(Color.Parse(hex));
}
