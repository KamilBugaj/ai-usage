# KB.AI.Usage — Architecture

A tray desktop app (.NET 10 + Avalonia) that shows AI usage as a tile dashboard.
Each provider is a self-contained vertical slice. Code is split into three projects with
a strict dependency direction:

```
AiUsage.App  ──▶  AiUsage.Application  ──▶  AiUsage.Core
 (UI / Avalonia)    (orchestration,           (domain: models,
                     no UI framework)           adapters, config)
```

`Application` and `Core` have **no** Avalonia reference; all UI/platform code lives in `App`.

## Projects

### AiUsage.Core — domain
- `Models/` — `LimitSnapshot`, `Source`, `LimitWindow`, and the contracts
  `ISourceAdapter`, `IUsageSink`, `IBrowserFetcher`.
- `Adapters/<Provider>/` — one adapter per provider (`ClaudeWeb`, `ChatGptWeb`,
  `Copilot/CopilotApiAdapter`). An adapter polls an endpoint and emits to an `IUsageSink`.
- `Config/` — `AppConfig` record tree plus `ConfigLoader` / `ConfigSaver`
  (`%AppData%/AiUsage/config.json`).

### AiUsage.Application — orchestration (UI-agnostic)
- `Polling/UsageHost` — runs one poll loop per provider with warm-up/backoff retry and a
  shared "refresh now" signal. Marshals tile updates through `IUiDispatcher` so it never
  touches Avalonia. `IPollScheduler` / `IPollStatus` are the loop's seams.
- `Abstractions/IUiDispatcher` — "run this on the UI thread"; implemented in `App`.
- `Tiles/TileMapping` — pure formatting (percentage, alert threshold, "resets in …"),
  unit-tested without a UI.
- `Providers/ProviderCatalog` — the single source of provider metadata
  (key, display name, alert unit, default order).

### AiUsage.App — UI (Avalonia)
- `Views/`, `ViewModels/`, `Controls/`, `Converters/`, `Theming/` — the dashboard, the
  in-place settings panel, tri-colour bars and reset rings, themes.
- `Features/<Provider>/` — the per-provider UI slice: `Feature` (wires adapter + sink +
  tile), `TileViewModel` + `TileView`. Sinks map `LimitSnapshot` → view-model via
  `TileMapping`: Claude has its own two-window `ClaudeTileSink`; single-bar providers
  (ChatGPT, Copilot) share `Features/SingleBarTileSink`. WebView2 sign-in/restore/fetch is
  shared too — `Features/WebViewSession` parameterised per provider by `WebProviders`.
- `Providers/` — `IProviderConnector` and its impls drive interactive auth:
  `WebViewConnector` (Claude.ai + ChatGPT WebView2 sign-in), `CopilotConnector`
  (GitHub device flow). Each owns its session + cancellation.
- `Composition/AppComposer` — the composition root: owns the provider connectors and the
  active `AppHost`, rebuilding the host whenever a connection changes. `AppHost` builds tiles
  from config + `ProviderCatalog` and hands each adapter/sink to `UsageHost`. `App.axaml.cs`
  stays a thin Avalonia shell (tray, window, positioning) that drives `Connect`/`Disconnect`
  by provider key.

## Data flow

```
adapter.PollOnceAsync ─▶ IUsageSink (TileSink) ─▶ TileMapping ─▶ TileViewModel ─▶ TileView
        ▲                                                              │
   UsageHost poll loop (interval + backoff)                    IUiDispatcher (UI thread)
```

Interactive sign-in is separate: a connector runs the login flow, yields an `IBrowserFetcher`
session (or persists a token), then triggers a host rebuild so the adapter starts polling.

## Adding a provider

1. **Core:** add `Adapters/<P>/<P>Adapter.cs : ISourceAdapter`; add a `Source` member if needed.
2. **Application:** add the provider to `ProviderCatalog.All`.
3. **App:** add `Features/<P>/` (`Feature`, `TileViewModel`, `TileView`, and a sink — reuse
   `SingleBarTileSink` for a one-window tile); register the feature in `AppHost`. For
   interactive auth, add a connector (reuse `WebViewConnector` for WebView2 sign-in, adding a
   `WebProviders` spec) and wire it in `AppComposer.BuildConnectors`, the settings commands,
   and the `SettingsView` / `MainWindow` templates.

## Conventions

- English only across code, comments, commits, and docs.
- No secrets in the repo — `config/config.json` is gitignored; `config.example.json` holds
  field names only.
- Adapters are defensive: one provider failing must not crash the app.
