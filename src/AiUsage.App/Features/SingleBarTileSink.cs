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
    ResetFormat Format,
    string StatusPrefix,
    Func<DateTimeOffset, TimeSpan> Window,
    LimitWindow? OnlyWindow = null);

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
        // Some adapters emit several windows (e.g. ChatGPT 5h + weekly); show only the one
        // this tile tracks. When OnlyWindow is null, accept whatever the adapter emits.
        if (_spec.OnlyWindow is { } only && s.Window != only) return Task.CompletedTask;

        var pct       = TileMapping.Percent(s.Utilization);
        var resetsIn  = TileMapping.ResetsIn(s.ResetsAt, _spec.Format);
        var updatedAt = $"updated {DateTime.Now:HH:mm}";
        var window    = _spec.Window(s.ResetsAt);

        Dispatcher.UIThread.Post(() =>
        {
            _tile.IsLoading          = false;
            _tile.HasLimit           = true;
            _tile.Utilization        = s.Utilization;
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
