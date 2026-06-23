using AiUsage.App.Features;
using AiUsage.App.Features.ChatGpt;
using AiUsage.App.Features.Claude;
using AiUsage.App.Features.Copilot;
using AiUsage.App.ViewModels;
using AiUsage.Application.Abstractions;
using AiUsage.Application.Polling;
using AiUsage.Application.Providers;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.App;

/// <summary>
/// Composition for the dashboard: builds tiles into a stable MainWindowViewModel
/// (honouring per-tile UI config — enabled / order / size / alert threshold) and hands
/// each provider's adapter+sink to a <see cref="UsageHost"/> that runs the poll loops.
/// To add a new provider: create a feature folder under Features/, implement
/// IProviderFeature, and add it to the features array below + ProviderCatalog.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly UsageHost _usageHost;

    public MainWindowViewModel ViewModel { get; }

    public AppHost(
        MainWindowViewModel viewModel, string configPath, IUiDispatcher dispatcher,
        IBrowserFetcher? claudeFetcher = null, IBrowserFetcher? chatGptFetcher = null)
    {
        ViewModel = viewModel;
        _usageHost = new UsageHost(dispatcher);

        var config = ConfigLoader.Load(configPath);
        var uiTiles = config.Ui?.Tiles ?? [];

        // Provider key → feature. Disabled providers are skipped (no polling).
        (string key, IProviderFeature feature)[] features =
        [
            ("ClaudeWeb",  new ClaudeFeature(claudeFetcher)),
            ("ChatGptWeb", new ChatGptFeature(chatGptFetcher)),
            ("Copilot",    new CopilotFeature()),
        ];

        var built = new List<(int order, TileViewModelBase tile)>();
        foreach (var (key, feature) in features)
        {
            var ui = uiTiles.FirstOrDefault(t => t.Provider == key);
            if (ui is { Enabled: false }) continue;

            var tile = feature.Wire(config, _usageHost);
            tile.Provider       = key;
            tile.Size           = ui?.Size ?? TileSize.Large;
            tile.AlertThreshold = ui?.AlertThreshold;
            built.Add((ui?.Order ?? ProviderCatalog.DefaultOrder(key), tile));
        }

        ViewModel.Tiles.Clear();
        foreach (var (_, tile) in built.OrderBy(b => b.order))
            ViewModel.Tiles.Add(tile);
        ViewModel.RefreshAlerts();
    }

    /// <summary>Wakes all poll loops immediately, skipping the remaining delay.</summary>
    public void RefreshAll() => _usageHost.RefreshAll();

    public void Dispose() => _usageHost.Dispose();

    public static string DefaultConfigPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiUsage", "config.json");
}
