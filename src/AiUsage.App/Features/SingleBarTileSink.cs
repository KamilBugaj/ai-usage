using Avalonia.Threading;
using AiUsage.App.ViewModels;
using AiUsage.Application.Tiles;
using AiUsage.Core.Models;

namespace AiUsage.App.Features;

/// <summary>
/// How a single-bar provider tile renders a snapshot: the countdown granularity, the
/// status-line prefix ("Limit" / "Premium"), how to derive the window length from the reset
/// time, and (optionally) which limit window to show when the adapter emits several.
/// </summary>
internal sealed record SingleBarTileSpec(
    Func<LimitWindow, ResetFormat> Format,
    string StatusPrefix,
    Func<LimitSnapshot, TimeSpan> Window,
    LimitWindow[]? AcceptWindows = null);

/// <summary>
/// Drives a single-bar tile (ChatGPT, Copilot) from usage snapshots. Replaces the per-provider
/// sinks, which differed only by the values captured in <see cref="SingleBarTileSpec"/>.
/// </summary>
internal sealed class SingleBarTileSink : IUsageSink
{
    private readonly SingleBarTileViewModel _tile;
    private readonly SingleBarTileSpec _spec;

    public SingleBarTileSink(SingleBarTileViewModel tile, SingleBarTileSpec spec)
    {
        _tile = tile;
        _spec = spec;
    }

    public Task EmitAsync(LimitSnapshot s)
    {
        // Some adapters emit several windows; show only the ones this tile tracks. The list
        // must cover every plan the provider can report — a snapshot that is silently dropped
        // leaves the tile on "Loading…" forever, because only a real emit clears IsLoading.
        // When AcceptWindows is null, accept whatever the adapter emits.
        if (_spec.AcceptWindows is { } accepted && !accepted.Contains(s.Window))
            return Task.CompletedTask;

        var pct       = TileMapping.Percent(s.Utilization);
        var resetsIn  = TileMapping.ResetsIn(s.ResetsAt, _spec.Format(s.Window));
        var updatedAt = $"updated {DateTime.Now:HH:mm}";
        var window    = _spec.Window(s);

        Dispatcher.UIThread.Post(() =>
        {
            _tile.IsLoading          = false;
            _tile.HasLimit           = true;
            _tile.Utilization        = s.Utilization;
            _tile.UtilizationLabel   = $"{pct}%";
            _tile.StatusLine         = $"{_spec.StatusPrefix}: {pct}% used";
            _tile.ResetsAt           = $"resets {resetsIn}";
            _tile.UpdatedAt          = updatedAt;
            _tile.SessionResetsAtUtc = s.ResetsAt;
            _tile.SessionWindow      = window;
            _tile.RecomputeResetFractions();

            _tile.AlertActive = TileMapping.AlertActive(s.Utilization, _tile.AlertThreshold);
        });

        return Task.CompletedTask;
    }
}
