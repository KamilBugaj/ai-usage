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

    public Task EmitCreditsAsync(IReadOnlyList<CreditSnapshot> credits)
    {
        var prepaid  = credits.FirstOrDefault(c => c.Kind == CreditKind.Prepaid);
        var included = credits.FirstOrDefault(c => c.Kind == CreditKind.Included);

        var prepaidLabel  = prepaid  is null ? "" : TileMapping.Credits(prepaid.Amount, prepaid.Currency);
        var includedLabel = included is null ? "" : TileMapping.Credits(included.Amount, included.Currency);
        var expires = included?.ExpiresAt is { } at
            ? $"expires {TileMapping.ResetsIn(at, ResetFormat.Coarse)}"
            : null; // null (not "") so the row shows no empty tooltip

        Dispatcher.UIThread.Post(() =>
        {
            _tile.HasCredits            = prepaid is not null;
            _tile.CreditsLabel          = prepaidLabel;
            _tile.HasIncludedCredit     = included is not null;
            _tile.IncludedCreditLabel   = includedLabel;
            _tile.IncludedCreditExpires = expires;
        });

        return Task.CompletedTask;
    }
}
