using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AiUsage.App.Theming;
using AiUsage.Application.Providers;
using AiUsage.Core.Config;

namespace AiUsage.App.ViewModels;

// One row in the tile manager (drag-reorderable list).
public partial class TileSettingRow : ObservableObject
{
    public string Provider { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string AlertUnit { get; init; } = "";

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private bool _isLarge = true;
    [ObservableProperty] private string _alertThresholdText = ""; // empty = no alert

    public string AlertHint => $"alert ≥ ({AlertUnit})";
}

public partial class SettingsViewModel : ObservableObject
{
    // --- Appearance ---
    public IReadOnlyList<string> Presets => ThemeService.Presets;
    public IReadOnlyList<string> AccentSwatches => ThemeService.AccentSwatches;

    [ObservableProperty] private string _selectedPreset = "Mocha";
    [ObservableProperty] private string? _selectedAccent;

    // --- Tiles ---
    public ObservableCollection<TileSettingRow> TileRows { get; } = [];

    // --- Credentials ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectLabel))]
    private bool _claudeConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectLabel))]
    private bool _isConnecting;

    [ObservableProperty] private string _claudeStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectChatGptLabel))]
    private bool _chatGptConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectChatGptLabel))]
    private bool _isConnectingChatGpt;

    [ObservableProperty] private string _chatGptStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectCopilotLabel))]
    private bool _copilotConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectCopilotLabel))]
    private bool _isConnectingCopilot;

    [ObservableProperty] private string _copilotStatus = "";

    public string ConnectLabel => IsConnecting ? "Connecting…"
        : ClaudeConnected                       ? "Reconnect"
        :                                         "Connect";

    public string ConnectChatGptLabel => IsConnectingChatGpt ? "Connecting…"
        : ChatGptConnected                                   ? "Reconnect"
        :                                                       "Connect";

    public string ConnectCopilotLabel => IsConnectingCopilot ? "Connecting…"
        : CopilotConnected                                   ? "Reconnect"
        :                                                       "Connect";

    // Callbacks injected by App (no-ops for the XAML designer). Connect/Disconnect are
    // keyed by provider so the surface stays flat as providers are added.
    private readonly Action _onApply;
    private readonly Action<ThemeConfig> _onPreviewTheme;
    private readonly Action<string> _onConnect;
    private readonly Action<string> _onDisconnect;
    private bool _loading;

    public SettingsViewModel() : this(() => { }, _ => { }, _ => { }, _ => { }) { }

    public SettingsViewModel(
        Action onApply, Action<ThemeConfig> onPreviewTheme,
        Action<string> onConnect, Action<string> onDisconnect)
    {
        _onApply = onApply;
        _onPreviewTheme = onPreviewTheme;
        _onConnect = onConnect;
        _onDisconnect = onDisconnect;
    }

    public ThemeConfig CurrentTheme => new(SelectedPreset, SelectedAccent);

    /// <summary>Populates the editable state from the saved config + live connection state.</summary>
    public void Load(AppConfig cfg, bool claudeConnected)
    {
        _loading = true;
        try
        {
            SelectedPreset = cfg.Ui?.Theme?.Preset ?? "Mocha";
            SelectedAccent = cfg.Ui?.Theme?.Accent;

            ClaudeConnected     = claudeConnected;
            ChatGptConnected    = !string.IsNullOrWhiteSpace(cfg.ChatGptWeb?.SessionToken);
            CopilotConnected    = !string.IsNullOrWhiteSpace(cfg.Copilot?.OAuthToken);

            TileRows.Clear();
            var uiTiles = cfg.Ui?.Tiles ?? [];
            var ordered = ProviderCatalog.All
                .Select(p => (info: p, ui: uiTiles.FirstOrDefault(t => t.Provider == p.Key)))
                .OrderBy(x => x.ui?.Order ?? ProviderCatalog.DefaultOrder(x.info.Key));

            foreach (var (info, ui) in ordered)
            {
                // Percentage providers carry a mandatory alert; default to 80% when unset.
                var defaultThreshold = info.AlertUnit == "%" ? "80" : "";
                TileRows.Add(new TileSettingRow
                {
                    Provider           = info.Key,
                    DisplayName        = info.DisplayName,
                    AlertUnit          = info.AlertUnit,
                    Enabled            = ui?.Enabled ?? true,
                    IsLarge            = (ui?.Size ?? TileSize.Large) == TileSize.Large,
                    AlertThresholdText = ui?.AlertThreshold?.ToString(CultureInfo.InvariantCulture)
                                         ?? defaultThreshold
                });
            }
        }
        finally { _loading = false; }
    }

    /// <summary>Builds the tile UI config list from the current (possibly reordered) rows.</summary>
    public List<TileUiConfig> BuildTileConfigs()
    {
        var list = new List<TileUiConfig>();
        for (int i = 0; i < TileRows.Count; i++)
        {
            var r = TileRows[i];
            // % providers keep a mandatory alert: empty/invalid input falls back to 80.
            double? threshold =
                double.TryParse(r.AlertThresholdText, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                    ? v
                    : r.AlertUnit == "%" ? 80.0 : null;
            list.Add(new TileUiConfig(
                r.Provider, r.Enabled, i,
                r.IsLarge ? TileSize.Large : TileSize.Small, threshold));
        }
        return list;
    }

    partial void OnSelectedPresetChanged(string value) { if (!_loading) PreviewTheme(); }
    partial void OnSelectedAccentChanged(string? value) { if (!_loading) PreviewTheme(); }

    private void PreviewTheme() => _onPreviewTheme(CurrentTheme);

    [RelayCommand] private void PickAccent(string hex) => SelectedAccent = hex;
    [RelayCommand] private void ResetAccent() => SelectedAccent = null;
    [RelayCommand] private void ConnectClaude() => _onConnect("ClaudeWeb");
    [RelayCommand] private void ConnectChatGpt() => _onConnect("ChatGptWeb");
    [RelayCommand] private void DisconnectClaude() => _onDisconnect("ClaudeWeb");
    [RelayCommand] private void DisconnectChatGpt() => _onDisconnect("ChatGptWeb");
    [RelayCommand] private void ConnectCopilot() => _onConnect("Copilot");
    [RelayCommand] private void DisconnectCopilot() => _onDisconnect("Copilot");
    [RelayCommand] private void Apply() => _onApply();
}
