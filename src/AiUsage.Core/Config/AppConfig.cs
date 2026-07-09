namespace AiUsage.Core.Config;

// sessionKey:     cookie from claude.ai (DevTools → Application → Cookies → sessionKey)
// cfClearance:    cf_clearance cookie — optional, but often required by CloudFlare
// cookieString:   full Cookie header captured from WebView — preferred over building
//                 from individual fields because cf_clearance is TLS-fingerprint-bound
// organizationId: optional — auto-discovered from /api/organizations when blank
public record ClaudeWebConfig(
    string SessionKey,
    string? CfClearance = null,
    string? OrganizationId = null,
    int PollIntervalMinutes = 1,
    string? CookieString = null);

// sessionToken: value of the __Secure-next-auth.session-token cookie from chatgpt.com
public record ChatGptWebConfig(string SessionToken, int PollIntervalMinutes = 1);

// oAuthToken: GitHub OAuth token (gho_…) obtained via device flow, used as
//             "Authorization: token …" against the Copilot internal usage API
//             (real % quota).
public record CopilotConfig(
    int PollIntervalMinutes = 1,
    string? OAuthToken = null);

public record AppConfig(
    ClaudeWebConfig? ClaudeWeb = null,
    ChatGptWebConfig? ChatGptWeb = null,
    CopilotConfig? Copilot = null,
    UiConfig? Ui = null);

// --- UI / appearance ---

public enum TileSize { Small, Large }

// Preset: named palette in ThemeService (e.g. "Mocha", "Latte", "Nord").
// Accent: optional hex override for the accent colour (e.g. "#89B4FA"); null = preset default.
public record ThemeConfig(string Preset = "Mocha", string? Accent = null);

// Per-tile UI settings. Provider matches Source enum name:
// "ClaudeWeb" | "ChatGptWeb" | "Copilot".
// AlertThreshold unit is % for all current providers. null = no alert.
public record TileUiConfig(
    string Provider,
    bool Enabled = true,
    int Order = 0,
    TileSize Size = TileSize.Large,
    double? AlertThreshold = null);

// UltraCompact: chrome-less dashboard (no header, click hides to tray) with minimal tiles.
// AlwaysOnTop: keep the window above other windows (Window.Topmost).
public record UiConfig(
    ThemeConfig? Theme = null,
    IReadOnlyList<TileUiConfig>? Tiles = null,
    bool UltraCompact = false,
    bool AlwaysOnTop = false);
