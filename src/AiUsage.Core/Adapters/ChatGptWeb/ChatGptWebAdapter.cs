using System.Text.Json;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.Core.Adapters.ChatGptWeb;

public sealed class ChatGptWebAdapter : ISourceAdapter
{
    // Step 1: read the NextAuth session (cookie auth) to get a short-lived accessToken (JWT).
    //         The session lives at /api/auth/session — NOT /backend-api/auth/session (404).
    // Step 2: use accessToken as Bearer to call rate_limits.
    // Both run inside the WebView2 browser context (IBrowserFetcher) — chatgpt.com is
    // behind Cloudflare and rejects plain HttpClient requests with 403 Forbidden.
    private const string SessionUrl = "https://chatgpt.com/api/auth/session";

    // Candidate rate-limit endpoints, tried in order — the path has moved between
    // ChatGPT backend versions, so fall through on 4xx. /wham/usage is the current one
    // (polled by the Codex CLI); the others are older fallbacks.
    private static readonly string[] RateLimitUrls =
    [
        "https://chatgpt.com/backend-api/wham/usage",
        "https://chatgpt.com/backend-api/rate_limits",
        "https://chatgpt.com/backend-api/conversation/rate_limits",
    ];

    private readonly IBrowserFetcher _fetcher;
    private readonly ChatGptWebConfig? _cfg;

    public Source Source => Source.ChatGptWeb;
    public TimeSpan PollInterval => TimeSpan.FromMinutes(_cfg?.PollIntervalMinutes ?? 1);

    public ChatGptWebAdapter(IBrowserFetcher fetcher, ChatGptWebConfig? cfg = null)
    {
        _fetcher = fetcher;
        _cfg = cfg;
    }

    public async Task PollOnceAsync(IUsageSink sink, CancellationToken ct)
    {
        var accessToken = await GetAccessTokenAsync(ct);
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}" };

        // Try each candidate usage endpoint. Any HTTP failure (404/400 = path moved,
        // 401/403 = no entitlement, etc.) means "try the next one" — we only care
        // whether ANY endpoint returns usable data.
        string? body = null;
        foreach (var url in RateLimitUrls)
        {
            try
            {
                var candidate = await _fetcher.FetchJsonAsync(url, ct, headers);
                // A 200 with no parseable limits is the free-account signature — keep
                // looking in case another endpoint has data, but don't accept it as final.
                if (ParseSnapshots(candidate).Count > 0)
                {
                    body = candidate;
                    break;
                }
            }
            catch (HttpRequestException) { /* path/auth/shape issue — try the next candidate */ }
        }

        if (body is null)
            // Session is valid (we got an access token) but no endpoint returned usage
            // data — the signature of a free account. ChatGPT only exposes a usage/quota
            // API on paid plans (Plus / Pro / Codex); the free tier has nothing to show.
            throw new HttpRequestException(
                "ChatGPT usage needs a paid plan (Plus / Pro / Codex). "
                + "Free accounts don't expose usage limits.");

        foreach (var s in ParseSnapshots(body))
            await sink.EmitAsync(s);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        string body;
        try
        {
            body = await _fetcher.FetchJsonAsync(SessionUrl, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(
                $"ChatGPT session request failed ({SessionUrl}): {ex.Message}", ex);
        }

        var token = ParseAccessToken(body);
        if (token is null) throw new InvalidOperationException("Failed to extract accessToken from ChatGPT session.");
        return token;
    }

    public static string? ParseAccessToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("accessToken", out var token))
                return token.GetString();
        }
        catch { }
        return null;
    }

    // Parses multiple possible shapes of the ChatGPT rate_limits response.
    public static IReadOnlyList<LimitSnapshot> ParseSnapshots(string json)
    {
        var result = new List<LimitSnapshot>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Shape B: flat array [{ "limit": N, "remaining": N, "reset_at": "..." }]
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    if (TryParseModelLimit(item, out var snap))
                        result.Add(snap!);
                }
                if (result.Count > 0) return result;
            }

            if (root.ValueKind != JsonValueKind.Object) return result;

            // Shape W (current /wham/usage): { "rate_limits": { "primary": {...}, "secondary": {...} } }
            // or the rate_limits object at root. Each window:
            //   { "used_percent": 12.5, "window_minutes": 299, "resets_in_seconds": 17940 }
            var rlObj = root.TryGetProperty("rate_limits", out var rlProp) &&
                        rlProp.ValueKind == JsonValueKind.Object
                ? rlProp : root;
            if (rlObj.TryGetProperty("primary", out var primary) &&
                TryParseWindow(primary, out var pSnap)) result.Add(pSnap!);
            if (rlObj.TryGetProperty("secondary", out var secondary) &&
                TryParseWindow(secondary, out var sSnap)) result.Add(sSnap!);
            if (result.Count > 0) return result;

            // Shape A: { "models": { "gpt-4o": { "limit": N, "remaining": N, "reset_at": "..." } } }
            if (root.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Object)
            {
                foreach (var model in models.EnumerateObject())
                {
                    if (TryParseModelLimit(model.Value, out var snap))
                        result.Add(snap!);
                }
                if (result.Count > 0) return result;
            }

            // Shape C: { "rate_limits": [...] }
            if (root.TryGetProperty("rate_limits", out var rl) &&
                rl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rl.EnumerateArray())
                {
                    if (TryParseModelLimit(item, out var snap))
                        result.Add(snap!);
                }
            }
        }
        catch { }

        return result;
    }

    // Parses one /wham/usage window: { used_percent, window_minutes, resets_in_seconds }.
    private static bool TryParseWindow(JsonElement el, out LimitSnapshot? snap)
    {
        snap = null;
        if (el.ValueKind != JsonValueKind.Object) return false;
        if (!el.TryGetProperty("used_percent", out var up) || !up.TryGetDouble(out var pct))
            return false;

        var utilization = Math.Clamp(pct / 100.0, 0.0, 1.0);

        // Short window (~5h) is the session limit; anything longer is the weekly window.
        var window = LimitWindow.Session5h;
        if (el.TryGetProperty("window_minutes", out var wm) && wm.TryGetInt64(out var mins) && mins > 600)
            window = LimitWindow.Weekly7d;

        var resetsAt = DateTimeOffset.UtcNow.AddHours(window == LimitWindow.Weekly7d ? 24 * 7 : 5);
        if (el.TryGetProperty("resets_in_seconds", out var rs) && rs.TryGetInt64(out var secs) && secs >= 0)
            resetsAt = DateTimeOffset.UtcNow.AddSeconds(secs);

        snap = new LimitSnapshot(Source.ChatGptWeb, window, utilization, resetsAt);
        return true;
    }

    private static bool TryParseModelLimit(JsonElement el, out LimitSnapshot? snap)
    {
        snap = null;

        long limit = 0, remaining = 0;
        bool hasLimit = el.TryGetProperty("limit", out var lp) && lp.TryGetInt64(out limit);
        bool hasRemaining = el.TryGetProperty("remaining", out var rp) && rp.TryGetInt64(out remaining);

        double utilization = -1;

        if (hasLimit && hasRemaining && limit > 0)
            utilization = 1.0 - (double)remaining / limit;
        else if (el.TryGetProperty("utilization", out var util) && util.TryGetDouble(out var u))
            utilization = u;
        else if (el.TryGetProperty("percent_full", out var pf) && pf.TryGetDouble(out var p))
            utilization = p;

        if (utilization < 0) return false;
        utilization = Math.Clamp(utilization, 0.0, 1.0);

        var resetsAt = AiUsage.Core.Adapters.LimitParsing.ReadResetsAt(
            el, DateTimeOffset.UtcNow.AddHours(3), "reset_at", "resets_at", "resetAt", "reset_after");

        snap = new LimitSnapshot(Source.ChatGptWeb, LimitWindow.Session5h, utilization, resetsAt);
        return true;
    }
}
