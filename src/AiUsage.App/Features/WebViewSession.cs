using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using AiUsage.Core.Models;

namespace AiUsage.App.Features;

/// <summary>
/// Describes one WebView2-login provider: where to sign in and how to recognise its
/// session cookie. Everything else — the login window, silent restore and the in-context
/// fetch() plumbing — is identical across providers and lives in <see cref="WebViewSession"/>.
/// </summary>
internal sealed record WebViewProviderSpec(
    Uri Source,
    string LoginTitle,
    string SignInPrompt,
    Func<IReadOnlyList<(string Name, string Value)>, string?> SelectToken);

/// <summary>The WebView2-login providers, each parameterising <see cref="WebViewSession"/>.</summary>
internal static class WebProviders
{
    public static readonly WebViewProviderSpec Claude = new(
        Source: new Uri("https://claude.ai"),
        LoginTitle: "Sign in to claude.ai",
        SignInPrompt: "Sign in to claude.ai in the browser window…",
        SelectToken: cookies => cookies
            .FirstOrDefault(c => c.Name == "sessionKey").Value);

    // chatgpt.com's session cookie is the NextAuth token. NextAuth chunks large JWTs across
    // "<name>.0", "<name>.1" … so match by prefix as well as the exact name. The value is
    // only a "connected" marker — transport uses the browser's own cookies (credentials:'include').
    private const string ChatGptCookie = "__Secure-next-auth.session-token";

    public static readonly WebViewProviderSpec ChatGpt = new(
        Source: new Uri("https://chatgpt.com/"),
        LoginTitle: "Sign in to ChatGPT",
        SignInPrompt: "Sign in to ChatGPT in the browser window…",
        SelectToken: cookies => cookies
            .FirstOrDefault(c => c.Name == ChatGptCookie ||
                                 c.Name.StartsWith(ChatGptCookie + ".", StringComparison.Ordinal)).Value);
}

/// <summary>
/// A live NativeWebDialog navigated to a provider's site that issues API calls inside it via
/// fetch(). Running requests from the same WebView2 context that solved the Cloudflare TLS
/// challenge keeps the session valid — a plain HttpClient (different TLS fingerprint) gets a
/// 403. A single implementation serves every WebView2 provider (Claude.ai, ChatGPT),
/// parameterised by a <see cref="WebViewProviderSpec"/>.
/// </summary>
internal sealed class WebViewSession : IBrowserFetcher, IDisposable
{
    private const int RestoreTimeoutSeconds = 15;

    private readonly NativeWebDialog _dialog;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private bool _disposed;

    /// <summary>The session marker (recognised cookie value) persisted to config.</summary>
    public string Token { get; }

    private WebViewSession(NativeWebDialog dialog, string token)
    {
        _dialog = dialog;
        Token = token;
    }

    public async Task<string> FetchJsonAsync(
        string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _sem.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<WebMessageReceivedEventArgs>? handler = null;
            handler = (_, e) =>
            {
                _dialog.WebMessageReceived -= handler;
                // e.Body is the raw string passed to window.chrome.webview.postMessage().
                var msg = e.Body ?? string.Empty;
                if (msg.StartsWith("__err__:", StringComparison.Ordinal))
                    tcs.TrySetException(new HttpRequestException(msg[8..]));
                else
                    tcs.TrySetResult(msg);
            };

            using var reg = ct.Register(() =>
            {
                Dispatcher.UIThread.Post(() => _dialog.WebMessageReceived -= handler!);
                tcs.TrySetCanceled(ct);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _dialog.WebMessageReceived += handler;
                // JSON-encode the URL so it becomes a safe JS string literal (quotes,
                // backslashes etc. can't break out of the fetch() call).
                var jsUrl = JsonSerializer.Serialize(url);
                var hdrs = new Dictionary<string, string> { ["accept"] = "application/json" };
                if (headers is not null)
                    foreach (var kv in headers) hdrs[kv.Key] = kv.Value;
                var jsHeaders = JsonSerializer.Serialize(hdrs);
                // Use arrow functions — older WebView2 versions support ES6+
                _ = _dialog.InvokeScript($$"""
                    fetch({{jsUrl}}, {
                        credentials: 'include',
                        headers: {{jsHeaders}}
                    })
                    .then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); })
                    .then(t => window.chrome.webview.postMessage(t))
                    .catch(e => window.chrome.webview.postMessage('__err__:' + e.message));
                    """);
            });

            return await tcs.Task;
        }
        finally
        {
            _sem.Release();
        }
    }

    /// <summary>
    /// Opens the sign-in window. After the provider's session cookie appears the dialog is
    /// hidden (not closed) — the same WebView2 context (with its cf_clearance TLS binding)
    /// is reused for all subsequent API polling.
    /// </summary>
    public static async Task<WebViewSession> LoginAsync(
        TopLevel owner, WebViewProviderSpec spec,
        CancellationToken ct = default, Action<string>? progress = null)
    {
        var tcs = new TaskCompletionSource<WebViewSession>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var dialog = CreateDialog(spec, spec.LoginTitle);

        dialog.NavigationCompleted += async (_, _) =>
        {
            if (tcs.Task.IsCompleted) return;
            try
            {
                var token = await ReadTokenAsync(dialog, spec);
                if (token is null) return; // not signed in yet — wait for the next navigation

                tcs.TrySetResult(new WebViewSession(dialog, token));
                Dispatcher.UIThread.Post(() => dialog.HideWindow());
            }
            catch (Exception ex) when (!tcs.Task.IsCompleted)
            {
                tcs.TrySetException(ex);
            }
        };

        dialog.Closing += (_, _) =>
        {
            if (!tcs.Task.IsCompleted) tcs.TrySetCanceled();
        };

        ct.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (!tcs.Task.IsCompleted) dialog.Close();
        }));

        progress?.Invoke(spec.SignInPrompt);
        dialog.Show(owner);

        return await tcs.Task;
    }

    /// <summary>
    /// Tries to restore a session silently from WebView2's persisted cookie store. Returns
    /// null when cookies have expired or were never saved (the user must sign in manually).
    /// </summary>
    public static async Task<WebViewSession?> RestoreAsync(
        WebViewProviderSpec spec, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<WebViewSession?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var dialog = CreateDialog(spec, title: "");

        // Disposes the dialog when restoration fails, so we don't leak a hidden WebView2
        // window. On success the dialog is handed to the session, which owns its lifetime.
        void FailAndDispose()
        {
            tcs.TrySetResult(null);
            Dispatcher.UIThread.Post(() => dialog.Dispose());
        }

        dialog.NavigationCompleted += async (_, _) =>
        {
            if (tcs.Task.IsCompleted) return;
            try
            {
                var token = await ReadTokenAsync(dialog, spec);
                if (token is null) { FailAndDispose(); return; }

                tcs.TrySetResult(new WebViewSession(dialog, token));
                Dispatcher.UIThread.Post(() => dialog.HideWindow());
            }
            catch { FailAndDispose(); }
        };

        // Silent restore: hide the window as soon as it exists (NavigationStarted fires after
        // the WebView2 adapter is initialised, so TryGetWindow is non-null here).
        dialog.NavigationStarted += (_, _) => Dispatcher.UIThread.Post(() => dialog.HideWindow());

        ct.Register(() => tcs.TrySetCanceled());

        dialog.Show();
        // Hide synchronously in the same UI-thread tick, before the window can paint — this
        // avoids the brief flash of the restore window on startup. NavigationStarted is kept
        // as a fallback in case the window handle isn't ready yet.
        dialog.HideWindow();

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(RestoreTimeoutSeconds), ct);
        }
        catch
        {
            Dispatcher.UIThread.Post(() => dialog.Dispose());
            return null;
        }
    }

    private static NativeWebDialog CreateDialog(WebViewProviderSpec spec, string title)
    {
        var dialog = new NativeWebDialog { Title = title, Source = spec.Source };
        dialog.EnvironmentRequested += WebView2AppData.Apply;
        return dialog;
    }

    // Reads the provider's session cookie via the WebView2 cookie store (HttpOnly cookies
    // aren't visible to document.cookie). Returns null when the cookie store isn't ready or
    // no matching, non-empty cookie exists — callers decide whether that means "keep waiting"
    // (login) or "give up" (restore).
    private static async Task<string?> ReadTokenAsync(NativeWebDialog dialog, WebViewProviderSpec spec)
    {
        var cm = dialog.TryGetCookieManager();
        if (cm is null) return null;

        var cookies = await cm.GetCookiesAsync();
        IReadOnlyList<(string Name, string Value)> pairs = cookies is null
            ? []
            : cookies.Select(c => (c.Name, c.Value)).ToList();

        var token = spec.SelectToken(pairs);
        return string.IsNullOrEmpty(token) ? null : token;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sem.Dispose();
        Dispatcher.UIThread.Post(() => _dialog.Dispose());
    }
}
