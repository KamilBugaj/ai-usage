# KB.AI.Usage: Project Context

## Goal

Desktop tray app that shows AI usage limits from several providers as tiles in one dashboard.

Providers (connect / disconnect in Settings; tokens persist to config, nothing is pasted by hand):
- Claude.ai (`ClaudeWebAdapter`): WebView2 login; 5h + 7d limits, optional credit balances
- ChatGPT (`ChatGptWebAdapter`): WebView2 login; the plan's window from `wham/usage` (5h on
  Plus/Pro, ~30d on free) or a Business seat's monthly credit allowance (spend-controls)
- GitHub Copilot (`CopilotApiAdapter`): OAuth device flow; premium-request % from
  `api.github.com/copilot_internal/user`

## Stack

- .NET 10 / C#, Avalonia UI (Windows, macOS, Linux), WebView2 for the signed-in web sessions
- Three projects `App → Application → Core` (UI / orchestration / domain); Application and
  Core have no Avalonia reference. See `ARCHITECTURE.md`.
- One vertical slice per provider: `App/Features/<Provider>/` (ViewModel, View, Sink, Feature),
  auth connectors in `App/Providers/`. Shared: `WebViewSession`, `SingleBarTileSink`.
- Adapters in `AiUsage.Core/Adapters/`; poll loop + tile text mapping in `AiUsage.Application`.

## Constraints

- No extra runtimes (no Node, no Python in app logic)
- Windows is the primary target; other OSes only where WebView2 is not needed
- Config incl. session tokens: `%AppData%\AiUsage\config.json` (`~/.config/AiUsage/` elsewhere),
  edited only through Settings. WebView2 profile: `%AppData%\AiUsage\WebView2`.
- CI/CD: GitHub Actions (`.github/workflows/`)
- Adapters parse ONE shape per endpoint: a body captured from a live account (or read from an
  official open-source client), pinned as a fixture in the adapter's tests. No field aliases,
  scale guessing, alternative endpoints or "Shape A/B/C" fallbacks. Payload changed: capture
  the new body, update the fixture, then the parser. A poll that finds no data throws.

## UI / settings

Settings (in the dashboard window): theme (Mocha / Latte / Nord) + accent with live preview,
per-tile enable, drag-to-reorder, S/L size, alert threshold (default 80%). Bars go green →
yellow near the threshold → red at it; time to reset is a filling ring with a tooltip.

Ultra compact (`UiConfig.UltraCompact`): chrome-less 240 px dashboard, no header, minimal
tiles (9 px text, 3 px bars, 11 px rings), click anywhere hides to tray, details move to
tooltips. The Large toggle is hidden while active (sizes stay stored). Settings open from
the tray menu and always render full width.

Claude credits (`UiConfig.ShowClaudeCredits`, checkbox under Providers → Claude.ai, default
off): one `Credits:` line on the Claude tile, balances on the right, in every layout.
- `purchased`: credits the user bought. `GET /api/organizations/{org}/prepaid/credits`,
  `amount` in CENTS. One extra request per poll.
- `bonus`: free grant from Anthropic ("Included credit" on claude.ai), shown only while it
  has money left and is not expired or locked. `iguana_necktie` in the `/usage` body already
  fetched, `remaining_dollars` in WHOLE DOLLARS, `resets_at` = expiry.
A failed credits read hides that balance and never touches the 5h / 7d bars.
