# KB.AI.Usage — Project Context

## Goal

Multi-platform desktop app that aggregates AI usage stats across providers into a single tile-based dashboard.

Active providers (tiles shown in the UI):
- Claude.ai (`ClaudeWebAdapter`) — in-app WebView2 login; reads 5h + 7d limits
- ChatGPT (`ChatGptWebAdapter`) — in-app WebView2 login; usage API is paid-plan only
  (Plus/Pro/Codex), free accounts get a "needs a paid plan" message
- GitHub Copilot (`CopilotApiAdapter`) — GitHub OAuth **device flow** (browser authorize),
  then `api.github.com/copilot_internal/user` for the premium-interactions % quota

Providers connect from the in-app Settings panel (Connect / Disconnect); credentials/tokens
are persisted to config, so no manual token pasting is needed.

(Earlier dormant adapters — Anthropic API, OpenAI API, Copilot log scraper — were removed
in the VSA refactor; recover from git history if ever needed.)

## Stack

- .NET 10 / C#
- Avalonia UI (cross-platform: Windows, macOS, Linux)
- WebView2 (Windows) for web-based session scraping
- Three projects `App → Application → Core` (UI / orchestration / domain). Application and
  Core have no Avalonia reference. See `ARCHITECTURE.md`.
- Feature-per-provider vertical slice: `App/Features/<Provider>/` (ViewModel + View + Sink +
  Feature) and `App/Providers/` connectors for auth. Shared bits live in `App/Features/`:
  `WebViewSession` (WebView2 sign-in/restore/fetch) and `SingleBarTileSink` (one-window tiles).
- Core adapters in `AiUsage.Core/Adapters/`; poll loop + tile mapping in `AiUsage.Application`.

## Constraints

- No additional runtimes (no Node, no Python in app logic)
- Windows primary target; Avalonia enables cross-platform where WebView2 not required
- Config (incl. session tokens) at `%AppData%\AiUsage\config.json` (Windows) /
  `~/.config/AiUsage/config.json`; managed via the in-app Settings, not hand-edited
- WebView2 user-data + cookies under `%AppData%\AiUsage\WebView2`
- CI/CD via GitHub Actions (`.github/workflows/`)

## UI / settings

In-app Settings panel (same window as the dashboard) covers: themes (Mocha/Latte/Nord)
+ accent swatches with live preview, per-tile enable/disable, drag-to-reorder, S/L size,
and a mandatory alert threshold (default 80% for the %-based providers). Usage bars are
tri-coloured (green → yellow near the threshold → red at/over it); time-to-reset shows as
a filling ring with a hover tooltip.

Ultra compact mode (Appearance checkbox, `UiConfig.UltraCompact`): chrome-less 240 px
dashboard — no header, minimal single/dual-row tiles (9 px text, 3 px bars, 11 px rings),
click anywhere hides to tray, details move to hover tooltips. The per-tile Large toggle
is hidden while active (sizes stay stored). Settings remain reachable from the tray menu
and always render at full width.
