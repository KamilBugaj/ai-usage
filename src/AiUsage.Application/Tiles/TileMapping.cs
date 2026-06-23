namespace AiUsage.Application.Tiles;

/// <summary>How granular a "resets in …" countdown should read.</summary>
public enum ResetFormat
{
    /// <summary>Days + hours, then hours + minutes, then minutes (Claude session/weekly).</summary>
    Full,
    /// <summary>Hours + minutes, then minutes — no day component (ChatGPT 5h window).</summary>
    HoursMinutes,
    /// <summary>Days, else hours — coarse (Copilot monthly window).</summary>
    Coarse
}

/// <summary>
/// Pure, UI-agnostic mapping from a usage/limit value to the strings a tile displays.
/// Extracted from the per-provider sinks so the formatting can be unit-tested without
/// Avalonia and shared instead of copy-pasted.
/// </summary>
public static class TileMapping
{
    /// <summary>Utilisation (0..1) as a whole percentage. Rounded (was truncated for some providers).</summary>
    public static int Percent(double utilization) => (int)Math.Round(utilization * 100);

    /// <summary>True when the utilisation (0..1) is at or over the configured % threshold.</summary>
    public static bool AlertActive(double utilization, double? threshold)
        => threshold is { } t && utilization * 100 >= t;

    /// <summary>
    /// "in 2h 14m" / "in 3d 4h" / "soon" — a human countdown to <paramref name="resetsAt"/>,
    /// shaped by <paramref name="format"/>. Returns "soon" once the window has elapsed.
    /// </summary>
    public static string ResetsIn(DateTimeOffset resetsAt, ResetFormat format)
        => ResetsIn(resetsAt, DateTimeOffset.UtcNow, format);

    // Overload with an explicit "now" so tests are deterministic.
    public static string ResetsIn(DateTimeOffset resetsAt, DateTimeOffset now, ResetFormat format)
    {
        var delta = resetsAt - now;
        if (delta <= TimeSpan.Zero) return "soon";

        return format switch
        {
            ResetFormat.Full =>
                delta.TotalDays  >= 1 ? $"in {(int)delta.TotalDays}d {delta.Hours}h"
              : delta.TotalHours >= 1 ? $"in {(int)delta.TotalHours}h {delta.Minutes}m"
              :                         $"in {delta.Minutes}m",

            ResetFormat.HoursMinutes =>
                delta.TotalHours >= 1 ? $"in {(int)delta.TotalHours}h {delta.Minutes}m"
              :                         $"in {delta.Minutes}m",

            ResetFormat.Coarse =>
                delta.TotalDays >= 1 ? $"in {(int)delta.TotalDays}d"
              :                        $"in {(int)delta.TotalHours}h",

            _ => "soon"
        };
    }
}
