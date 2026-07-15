using AiUsage.App.ViewModels;
using AiUsage.Application.Polling;
using AiUsage.Application.Tiles;
using AiUsage.Core.Config;

namespace AiUsage.App.Features.Copilot;

internal sealed class CopilotFeature : IProviderFeature
{
    public TileViewModelBase Wire(AppConfig config, IPollScheduler scheduler)
    {
        var tile = new CopilotTileViewModel { Title = "GitHub Copilot", IsLoading = false };

        var cfg = config.Copilot ?? new CopilotConfig();
        if (string.IsNullOrWhiteSpace(cfg.OAuthToken))
        {
            tile.StatusLine = "Not connected — open Settings and connect GitHub Copilot";
            return tile;
        }

        tile.IsLoading = true;
        scheduler.Schedule(
            new Core.Adapters.Copilot.CopilotApiAdapter(cfg),
            new SingleBarTileSink(tile, new SingleBarTileSpec(
                _ => ResetFormat.Coarse, "Premium",
                // Monthly window — infer the actual month length from the reset timestamp.
                s => s.ResetsAt - s.ResetsAt.AddMonths(-1))),
            tile);
        return tile;
    }
}
