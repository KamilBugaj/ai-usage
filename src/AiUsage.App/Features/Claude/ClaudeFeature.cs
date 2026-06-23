using AiUsage.App.ViewModels;
using AiUsage.Application.Polling;
using AiUsage.Core.Adapters.ClaudeWeb;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.App.Features.Claude;

internal sealed class ClaudeFeature : IProviderFeature
{
    private readonly IBrowserFetcher? _fetcher;

    public ClaudeFeature(IBrowserFetcher? fetcher = null) => _fetcher = fetcher;

    public TileViewModelBase Wire(AppConfig config, IPollScheduler scheduler)
    {
        var tile = new ClaudeTileViewModel { Title = "Claude.ai" };

        if (_fetcher is null)
        {
            tile.StatusLine = "Not connected — use tray menu \"Connect Claude.ai\"";
            tile.IsLoading = false;
            return tile;
        }

        var cfg = config.ClaudeWeb; // may be null — adapter handles that
        scheduler.Schedule(new ClaudeWebAdapter(_fetcher, cfg), new ClaudeTileSink(tile), tile);
        return tile;
    }
}
