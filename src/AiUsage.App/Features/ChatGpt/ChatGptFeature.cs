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

        scheduler.Schedule(
            new ChatGptWebAdapter(_fetcher, config.ChatGptWeb),
            new SingleBarTileSink(tile, new SingleBarTileSpec(
                ResetFormat.HoursMinutes, "Limit",
                _ => TimeSpan.FromHours(5), OnlyWindow: LimitWindow.Session5h)),
            tile);
        return tile;
    }
}
