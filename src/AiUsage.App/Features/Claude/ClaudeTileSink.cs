using Avalonia.Threading;
using AiUsage.Application.Tiles;
using AiUsage.Core.Models;

namespace AiUsage.App.Features.Claude;

internal sealed class ClaudeTileSink : IUsageSink
{
    private readonly ClaudeTileViewModel _tile;

    public ClaudeTileSink(ClaudeTileViewModel tile) { _tile = tile; }

    public Task EmitAsync(LimitSnapshot s)
    {
        var pct      = TileMapping.Percent(s.Utilization);
        var resetsIn = TileMapping.ResetsIn(s.ResetsAt, ResetFormat.Full);
        var updatedAt = $"updated {DateTime.Now:HH:mm}";

        Dispatcher.UIThread.Post(() =>
        {
            _tile.IsLoading = false;
            _tile.UpdatedAt = updatedAt;

            if (s.Window == LimitWindow.Session5h)
            {
                _tile.HasLimit            = true;
                _tile.Utilization         = s.Utilization;
                _tile.UtilizationLabel    = $"{pct}%";
                _tile.ResetsAt            = $"resets {resetsIn}";
                _tile.SessionResetsAtUtc  = s.ResetsAt;
                _tile.SessionWindow       = TimeSpan.FromHours(5);
            }
            else if (s.Window == LimitWindow.Weekly7d)
            {
                _tile.HasWeeklyLimit      = true;
                _tile.WeeklyUtilization   = s.Utilization;
                _tile.WeeklyLabel         = $"{pct}%";
                _tile.WeeklyResetsAt      = $"resets {resetsIn}";
                _tile.WeeklyResetsAtUtc   = s.ResetsAt;
                _tile.WeeklyWindow        = TimeSpan.FromDays(7);
            }
            _tile.RecomputeResetFractions();

            // Alert when either window crosses the configured % threshold.
            _tile.AlertActive = TileMapping.AlertActive(
                Math.Max(_tile.Utilization, _tile.WeeklyUtilization), _tile.AlertThreshold);
        });

        return Task.CompletedTask;
    }
}
