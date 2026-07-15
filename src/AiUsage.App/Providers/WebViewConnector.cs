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
    // A Disconnect in flight: Connect must wait for the cookie wipe to finish, or the login
    // window would find the old session still present and sign straight back in.
    private Task? _signOut;

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

            // Let a pending sign-out finish first, so this login starts from a clean profile.
            if (_signOut is not null) await _signOut;

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

        // Sign out before dropping the session: clearing the marker alone leaves the
        // provider's cookies in the WebView2 profile, so the next Connect silently restores
        // the same account and there is no way to switch to a different one.
        var fetcher = Fetcher;
        Fetcher = null;
        // Chain onto a sign-out that is still running rather than replacing it: a second
        // Disconnect sees a null Fetcher, so its own task would complete immediately, and
        // Connect — which only awaits the latest — would start while the earlier cookie
        // wipe was still in flight, restoring the old account.
        _signOut = SignOutAndDisposeAsync(_signOut, fetcher);

        _clearMarker();
        _setConnected(false);
        _setStatus("");
        _onChanged();
    }

    // Never faults: Connect awaits this, so a failed sign-out or disposal must not leave the
    // provider permanently unable to log back in.
    private static async Task SignOutAndDisposeAsync(Task? pending, IBrowserFetcher? fetcher)
    {
        if (pending is not null)
        {
            try { await pending; } catch { }
        }

        try
        {
            if (fetcher is Features.WebViewSession session)
                await session.SignOutAsync();
        }
        catch { }

        try { (fetcher as IDisposable)?.Dispose(); } catch { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        (Fetcher as IDisposable)?.Dispose();
    }
}
