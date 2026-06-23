using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AiUsage.Core.Models;

namespace AiUsage.App.Providers;

/// <summary>
/// Connector for the WebView2-login providers (Claude.ai, ChatGPT). Both sign in through a
/// NativeWebDialog that yields a live <see cref="IBrowserFetcher"/> session, persist a
/// "connected" marker, and support silent cookie-based restore. The flow is identical bar a
/// handful of provider hooks supplied by the caller.
/// </summary>
internal sealed class WebViewConnector : IProviderConnector
{
    private readonly Func<TopLevel, CancellationToken, Action<string>, Task<IBrowserFetcher>> _login;
    private readonly Func<Task<IBrowserFetcher?>> _restore;
    private readonly Action<IBrowserFetcher> _persistMarker;
    private readonly Action _clearMarker;
    private readonly Action<string> _setStatus;
    private readonly Action<bool> _setConnected;
    private readonly Action<bool> _setConnecting;
    private readonly Action _onChanged;

    private CancellationTokenSource? _cts;

    public string Key { get; }
    public IBrowserFetcher? Fetcher { get; private set; }

    public WebViewConnector(
        string key,
        Func<TopLevel, CancellationToken, Action<string>, Task<IBrowserFetcher>> login,
        Func<Task<IBrowserFetcher?>> restore,
        Action<IBrowserFetcher> persistMarker,
        Action clearMarker,
        Action<string> setStatus,
        Action<bool> setConnected,
        Action<bool> setConnecting,
        Action onChanged)
    {
        Key = key;
        _login = login;
        _restore = restore;
        _persistMarker = persistMarker;
        _clearMarker = clearMarker;
        _setStatus = setStatus;
        _setConnected = setConnected;
        _setConnecting = setConnecting;
        _onChanged = onChanged;
    }

    public void Connect(TopLevel owner)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = LoginAsync(owner, _cts.Token);
    }

    private async Task LoginAsync(TopLevel owner, CancellationToken ct)
    {
        try
        {
            _setConnecting(true);
            _setStatus("");
            void Report(string msg) => Dispatcher.UIThread.Post(() => _setStatus(msg));

            var session = await _login(owner, ct, Report);
            (Fetcher as IDisposable)?.Dispose();
            Fetcher = session;

            // Persist the session marker so the silent restore runs on the next launch.
            _persistMarker(session);
            _setConnected(true);
            _setStatus("Signed in! Loading data…");
            _onChanged();
        }
        catch (OperationCanceledException) { _setStatus(""); }
        catch (Exception ex) { _setStatus(ex.Message); }
        finally { _setConnecting(false); }
    }

    public async Task TryRestoreAsync()
    {
        try
        {
            var session = await _restore();
            if (session is null) return; // cookies expired → tile stays "Not connected"
            (Fetcher as IDisposable)?.Dispose();
            Fetcher = session;
            _setConnected(true);
            _onChanged();
        }
        catch { }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        (Fetcher as IDisposable)?.Dispose();
        Fetcher = null;

        _clearMarker();
        _setConnected(false);
        _setStatus("");
        _onChanged();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        (Fetcher as IDisposable)?.Dispose();
    }
}
