using AiUsage.App.ViewModels;
using AiUsage.Application.Polling;
using AiUsage.Application.Tiles;
using AiUsage.Core.Adapters.ChatGptWeb;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.App.Features.ChatGpt;

internal sealed class ChatGptFeature : IProviderFeature
{
    private readonly IBrowserFetcher? _fetcher;

    public ChatGptFeature(IBrowserFetcher? fetcher = null) => _fetcher = fetcher;

    public TileViewModelBase Wire(AppConfig config, IPollScheduler scheduler)
    {
        var tile = new ChatGptTileViewModel { Title = "ChatGPT", IsLoading = false };

        if (_fetcher is null)
        {
            tile.StatusLine = "Not connected — open Settings and connect ChatGPT";
            return tile;
        }

        // Show "Loading…" until the first poll resolves. Without this the tile sits blank
        // through the poll loop's warm-up (it only surfaces an error after 3 failures).
        tile.IsLoading = true;
        scheduler.Schedule(
            new ChatGptWebAdapter(_fetcher, config.ChatGptWeb),
            // ChatGPT's window depends on the plan: Plus/Pro report a ~5h primary (+ a weekly
            // secondary this single-bar tile ignores), while free accounts report a single
            // ~30d window. The two are mutually exclusive, so accepting both needs no
            // priority — and dropping either would strand the tile on "Loading…".
            new SingleBarTileSink(tile, new SingleBarTileSpec(
                w => w == LimitWindow.Monthly ? ResetFormat.Coarse : ResetFormat.HoursMinutes,
                "Limit",
                s => s.Window == LimitWindow.Monthly
                    ? s.ResetsAt - s.ResetsAt.AddMonths(-1)
                    : TimeSpan.FromHours(5),
                AcceptWindows: [LimitWindow.Session5h, LimitWindow.Monthly])),
            tile);
        return tile;
    }
}
