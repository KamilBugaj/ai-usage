using Avalonia.Threading;
using AiUsage.Application.Tiles;
using AiUsage.Core.Models;

namespace AiUsage.App.Features.Claude;

internal sealed class ClaudeTileSink : IUsageSink
{
    private readonly ClaudeTileViewModel _tile;

    public ClaudeTileSink(ClaudeTileViewModel tile) { _tile = tile; }

    public Task EmitLimitsAsync(IReadOnlyList<LimitSnapshot> limits)
    {
        // Both rows are written on every poll, so a window that has gone (e.g. no weekly
        // limit after a plan change) is cleared instead of lingering at its last value.
        var session = limits.FirstOrDefault(s => s.Window == LimitWindow.Session5h);
        var weekly  = limits.FirstOrDefault(s => s.Window == LimitWindow.Weekly7d);
        var updatedAt = $"updated {DateTime.Now:HH:mm}";

        static string Label(LimitSnapshot? s) => s is null ? "" : $"{TileMapping.Percent(s.Utilization)}%";
        static string Resets(LimitSnapshot? s)
            => s is null ? "" : $"resets {TileMapping.ResetsIn(s.ResetsAt, ResetFormat.Full)}";

        Dispatcher.UIThread.Post(() =>
        {
            _tile.IsLoading = false;
            _tile.UpdatedAt = updatedAt;

            _tile.HasLimit            = session is not null;
            _tile.Utilization         = session?.Utilization ?? 0;
            _tile.UtilizationLabel    = Label(session);
            _tile.ResetsAt            = Resets(session);
            _tile.SessionResetsAtUtc  = session?.ResetsAt;
            _tile.SessionWindow       = TimeSpan.FromHours(5);

            _tile.HasWeeklyLimit      = weekly is not null;
            _tile.WeeklyUtilization   = weekly?.Utilization ?? 0;
            _tile.WeeklyLabel         = Label(weekly);
            _tile.WeeklyResetsAt      = Resets(weekly);
            _tile.WeeklyResetsAtUtc   = weekly?.ResetsAt;
            _tile.WeeklyWindow        = TimeSpan.FromDays(7);

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
