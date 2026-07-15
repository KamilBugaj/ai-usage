using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AiUsage.App.Infrastructure;

/// <summary>
/// Draws the tray icon at runtime so the tallest bar can turn red while any provider is
/// over its alert threshold. Mirrors the static asset drawn by packaging/icon/generate.py
/// (same 256-unit design grid, background and bar geometry) — keep the two in sync.
/// The palette is the icon's own (tailwind-ish violet), deliberately not theme-bound:
/// the tray icon is a brand mark and must read the same under every app theme.
/// </summary>
internal static class TrayIconRenderer
{
    private const string Background = "#0f1117";
    private const string Bar = "#7c3aed";        // the two shorter bars
    private const string BarTall = "#a76eff";    // tallest bar, normal state
    private const string BarAlert = "#ef4444";   // tallest bar, threshold reached

    // Design grid is 256 units wide (from generate.py): x, y, width, height.
    // Bars bottom-align at y=220; the last entry is the tallest (the alert one).
    private static readonly (double X, double Y, double W, double H)[] Bars =
    [
        (52, 168, 44, 52),
        (106, 128, 44, 92),
        (160, 80, 44, 140),
    ];

    private static WindowIcon? _normal;
    private static WindowIcon? _alert;

    /// <summary>Cached icon for the given alert state. Must be called on the UI thread.</summary>
    public static WindowIcon Get(bool alert)
        => alert ? _alert ??= Render(true) : _normal ??= Render(false);

    private static WindowIcon Render(bool alert, int size = 64)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var s = size / 256.0;

            ctx.DrawRectangle(
                new SolidColorBrush(Color.Parse(Background)), null,
                new RoundedRect(new Rect(0, 0, size, size), 54 * s));

            for (var i = 0; i < Bars.Length; i++)
            {
                var isTallest = i == Bars.Length - 1;
                var colour = isTallest ? (alert ? BarAlert : BarTall) : Bar;
                var (x, y, w, h) = Bars[i];
                ctx.DrawRectangle(
                    new SolidColorBrush(Color.Parse(colour)), null,
                    new RoundedRect(new Rect(x * s, y * s, w * s, h * s), 6 * s));
            }
        }

        // WindowIcon copies the stream, so the bitmap can be disposed straight after.
        using var png = new MemoryStream();
        bitmap.Save(png);
        png.Position = 0;
        return new WindowIcon(png);
    }
}
