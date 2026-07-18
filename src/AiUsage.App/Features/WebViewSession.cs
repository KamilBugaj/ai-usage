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
    Func<IReadOnlyList<(string Name, string Value)>, string?> SelectToken,
    // Registrable domains wiped on Disconnect. Must include the identity provider, not just
    // the app host: leaving the SSO cookie behind lets the next Connect sign straight back
    // in, so the user can never switch accounts.
    string[] SignOutDomains);

/// <summary>The WebView2-login providers, each parameterising <see cref="WebViewSession"/>.</summary>
internal static class WebProviders
{
    public static readonly WebViewProviderSpec Claude = new(
        Source: new Uri("https://claude.ai"),
        LoginTitle: "Sign in to claude.ai",
        SignInPrompt: "Sign in to claude.ai in the browser window…",
        SelectToken: cookies => cookies
            .FirstOrDefault(c => c.Name == "sessionKey").Value,
        SignOutDomains: ["claude.ai", "anthropic.com"]);

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
                                 c.Name.StartsWith(ChatGptCookie + ".", StringComparison.Ordinal)).Value,
        // openai.com carries the SSO session that chatgpt.com logs in through.
        SignOutDomains: ["chatgpt.com", "openai.com"]);
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

    // The injected fetch answers via postMessage. If the page never answers (Cloudflare
    // interstitial, SPA navigation destroying the JS context, a dropped script), the wait
    // must not last forever: the semaphore below is held for the whole call, so one hung
    // fetch would deadlock every later poll for that provider and freeze its tile blank.
    private const int FetchTimeoutSeconds = 20;

    private readonly NativeWebDialog _dialog;
    private readonly WebViewProviderSpec _spec;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private bool _disposed;

    /// <summary>The session marker (recognised cookie value) persisted to config.</summary>
    public string Token { get; }

    private WebViewSession(NativeWebDialog dialog, WebViewProviderSpec spec, string token)
    {
        _dialog = dialog;
        _spec = spec;
        Token = token;
    }

    /// <summary>
    /// Wipes this provider's cookies from the shared WebView2 profile, so the next Connect
    /// lands on a real login page instead of silently restoring the same account. Only this
    /// provider's domains are touched — the other providers keep their sessions. Best effort:
    /// a failure here must not block Disconnect.
    /// </summary>
    public async Task SignOutAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var cm = _dialog.TryGetCookieManager();
                if (cm is null) return;
                var cookies = await cm.GetCookiesAsync();
                if (cookies is null) return;

                foreach (var c in cookies)
                {
                    var domain = (c.Domain ?? string.Empty).TrimStart('.');
                    if (_spec.SignOutDomains.Any(d =>
                            domain.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                            domain.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
                        cm.DeleteCookie(c.Name, c.Domain, c.Path);
                }
            });
        }
        catch { /* best effort — Disconnect still clears the marker and drops the session */ }
    }

    public async Task<string> FetchJsonAsync(
        string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Bound the WHOLE call, not just the reply: the lock wait, the dispatch to the UI
        // thread and the page's answer can each stall forever (WebView2 not ready, Cloudflare
        // interstitial, SPA navigation dropping the JS context). Any unbounded step freezes
        // the provider's tile on "Loading…" and, since the lock is held, every later poll too.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(FetchTimeoutSeconds));
        var tct = timeout.Token;

        // Names the step in flight, so a timeout says which one stalled.
        var step = "waiting for the session lock";
        try
        {
            await _sem.WaitAsync(tct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Stalled(step, url);
        }

        try
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Correlate replies: a fetch that timed out can still answer later, and its reply
            // must not resolve the *next* request with a stale body.
            var id = Guid.NewGuid().ToString("N");
            var prefix = id + ":";

            EventHandler<WebMessageReceivedEventArgs>? handler = null;
            handler = (_, e) =>
            {
                // e.Body is the raw string passed to window.chrome.webview.postMessage().
                var msg = e.Body ?? string.Empty;
                if (!msg.StartsWith(prefix, StringComparison.Ordinal)) return; // not ours
                var payload = msg[prefix.Length..];
                if (payload.StartsWith("err:", StringComparison.Ordinal))
                    tcs.TrySetException(new HttpRequestException(payload[4..]));
                else if (payload.StartsWith("ok:", StringComparison.Ordinal))
                    tcs.TrySetResult(payload[3..]);
            };

            try
            {
                step = "dispatching the fetch script";
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _dialog.WebMessageReceived += handler;
                    // JSON-encode the URL so it becomes a safe JS string literal (quotes,
                    // backslashes etc. can't break out of the fetch() call).
                    var jsUrl = JsonSerializer.Serialize(url, WebViewJsonContext.Default.String);
                    var hdrs = new Dictionary<string, string> { ["accept"] = "application/json" };
                    if (headers is not null)
                        foreach (var kv in headers) hdrs[kv.Key] = kv.Value;
                    var jsHeaders = JsonSerializer.Serialize(hdrs, WebViewJsonContext.Default.DictionaryStringString);
                    // Deliberately not awaited: InvokeScript can itself stall when the WebView
                    // is mid-navigation, and awaiting it would stall outside the timeout above.
                    // A failed injection is reported through tcs so the wait below ends early.
                    // Use arrow functions — older WebView2 versions support ES6+
                    _ = _dialog.InvokeScript($$"""
                        fetch({{jsUrl}}, {
                            credentials: 'include',
                            headers: {{jsHeaders}}
                        })
                        .then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.text(); })
                        .then(t => window.chrome.webview.postMessage('{{id}}:ok:' + t))
                        .catch(e => window.chrome.webview.postMessage('{{id}}:err:' + e.message));
                        """)
                        .ContinueWith(
                            t => tcs.TrySetException(new HttpRequestException(
                                "WebView script injection failed: "
                                + t.Exception!.GetBaseException().Message)),
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted,
                            TaskScheduler.Default);
                }).GetTask().WaitAsync(tct);

                step = "waiting for the page to reply";
                return await tcs.Task.WaitAsync(tct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw Stalled(step, url);
            }
            finally
            {
                // Post, never await: the unwind must not be able to block. Correlation (above)
                // is what protects a later request from a late reply, not the unsubscribe.
                Dispatcher.UIThread.Post(() => _dialog.WebMessageReceived -= handler);
            }
        }
        finally
        {
            _sem.Release();
        }
    }

    private static HttpRequestException Stalled(string step, string url)
        => new($"WebView fetch stalled {step} — gave up after {FetchTimeoutSeconds}s ({url}). "
               + "The page may be showing a Cloudflare check or a login.");

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

                tcs.TrySetResult(new WebViewSession(dialog, spec, token));
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

                tcs.TrySetResult(new WebViewSession(dialog, spec, token));
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
        // Give the dialog an explicit size. The underlying window's Width/Height stay
        // double.NaN until Resize() is called; on macOS that NaN flows into the native
        // -[WKWebView initWithFrame:] call and kills the app with SIGILL
        // ("Invalid view geometry: y is NaN") the moment the login window is laid out.
        // Windows/WebView2 tolerates the NaN, so this only crashes on macOS.
        dialog.Resize(460, 720);
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
