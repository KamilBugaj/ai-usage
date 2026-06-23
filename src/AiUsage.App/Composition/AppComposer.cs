using System;
using System.Collections.Generic;
using Avalonia.Controls;
using AiUsage.App.Features;
using AiUsage.App.Providers;
using AiUsage.App.ViewModels;
using AiUsage.Application.Abstractions;
using AiUsage.Core.Config;

namespace AiUsage.App.Composition;

/// <summary>
/// Composition root for the running app: owns the provider connectors and the current
/// <see cref="AppHost"/>, and rebuilds the host whenever a connection changes. Keeps
/// App.axaml.cs a thin Avalonia shell (tray + window) with no wiring of its own.
/// </summary>
internal sealed class AppComposer : IDisposable
{
    private readonly MainWindowViewModel _vm;
    private readonly IUiDispatcher _dispatcher;
    private readonly string _configPath;
    private readonly Dictionary<string, IProviderConnector> _connectors;
    private AppHost? _host;

    public AppComposer(
        MainWindowViewModel vm, SettingsViewModel settings,
        IUiDispatcher dispatcher, string configPath)
    {
        _vm = vm;
        _dispatcher = dispatcher;
        _configPath = configPath;
        _connectors = BuildConnectors(settings);
        RebuildHost();
    }

    /// <summary>Starts the interactive sign-in flow for one provider.</summary>
    public void Connect(TopLevel owner, string key) => _connectors[key].Connect(owner);

    /// <summary>Tears down one provider's session and clears its persisted marker.</summary>
    public void Disconnect(string key) => _connectors[key].Disconnect();

    /// <summary>Silently restores any providers that have a saved "connected" marker.</summary>
    public void RestoreSaved(AppConfig cfg)
    {
        if (cfg.ClaudeWeb is not null)  _ = _connectors["ClaudeWeb"].TryRestoreAsync();
        if (cfg.ChatGptWeb is not null) _ = _connectors["ChatGptWeb"].TryRestoreAsync();
    }

    /// <summary>True when the provider currently holds a live session (vs only a config marker).</summary>
    public bool HasLiveSession(string key) => _connectors.GetValueOrDefault(key)?.Fetcher is not null;

    /// <summary>Rebuilds the host (and its poll loops) against the current sessions + config.</summary>
    public void RebuildHost()
    {
        _host?.Dispose();
        _host = new AppHost(
            _vm, _configPath, _dispatcher,
            _connectors.GetValueOrDefault("ClaudeWeb")?.Fetcher,
            _connectors.GetValueOrDefault("ChatGptWeb")?.Fetcher);
    }

    private Dictionary<string, IProviderConnector> BuildConnectors(SettingsViewModel settings)
    {
        var path = _configPath;
        return new Dictionary<string, IProviderConnector>
        {
            ["ClaudeWeb"] = new WebViewConnector(
                "ClaudeWeb",
                login:        async (owner, ct, report) => await WebViewSession.LoginAsync(owner, WebProviders.Claude, ct, report),
                restore:      async () => await WebViewSession.RestoreAsync(WebProviders.Claude),
                persistMarker: f => ConfigSaver.UpdateClaudeWeb(path, ((WebViewSession)f).Token, null, null),
                clearMarker:  () => ConfigSaver.Save(path, ConfigLoader.Load(path) with { ClaudeWeb = null }),
                setStatus:    s => settings.ClaudeStatus = s,
                setConnected: b => settings.ClaudeConnected = b,
                setConnecting: b => settings.IsConnecting = b,
                onChanged:    RebuildHost),

            ["ChatGptWeb"] = new WebViewConnector(
                "ChatGptWeb",
                login:        async (owner, ct, report) => await WebViewSession.LoginAsync(owner, WebProviders.ChatGpt, ct, report),
                restore:      async () => await WebViewSession.RestoreAsync(WebProviders.ChatGpt),
                persistMarker: f => ConfigSaver.UpdateChatGptWeb(path, ((WebViewSession)f).Token),
                clearMarker:  () => ConfigSaver.Save(path, ConfigLoader.Load(path) with { ChatGptWeb = null }),
                setStatus:    s => settings.ChatGptStatus = s,
                setConnected: b => settings.ChatGptConnected = b,
                setConnecting: b => settings.IsConnectingChatGpt = b,
                onChanged:    RebuildHost),

            ["Copilot"] = new CopilotConnector(
                path,
                setStatus:    s => settings.CopilotStatus = s,
                setConnected: b => settings.CopilotConnected = b,
                setConnecting: b => settings.IsConnectingCopilot = b,
                onChanged:    RebuildHost),
        };
    }

    public void Dispose()
    {
        foreach (var c in _connectors.Values) c.Dispose();
        _host?.Dispose();
    }
}
