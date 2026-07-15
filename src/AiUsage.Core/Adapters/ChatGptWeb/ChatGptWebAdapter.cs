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

    // Business/Enterprise seats get no rate-limit window at all — their per-user cap is an
    // admin-set MONTHLY CREDIT allowance, served from the workspace spend-controls endpoint
    // (this is what Settings → Usage renders). Account-scoped, hence the id in the path.
    private static string MonthlyCreditsUrl(string accountId)
        => $"https://chatgpt.com/backend-api/accounts/{accountId}/spend-controls/current-user/monthly-usage";

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
        var (accountId, planType) = ParseAuthClaims(accessToken);

        // wham/usage is account-scoped: the Codex CLI sends ChatGPT-Account-Id so the
        // request resolves to the right workspace. Without it, accounts with more than
        // one workspace (e.g. Enterprise + personal) can get an empty or foreign result.
        // The id lives in the accessToken JWT's OpenAI auth claim. (User-Agent can't be
        // set here — fetch() forbids it — so we only send the account header.)
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}" };
        if (!string.IsNullOrEmpty(accountId))
            headers["ChatGPT-Account-Id"] = accountId;

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
        {
            // No rate-limit window. On Business/Enterprise that's expected — wham/usage is
            // the Codex endpoint and answers all-null there — but the seat still has an
            // admin-set monthly credit cap, which lives on the spend-controls endpoint.
            if (!string.IsNullOrEmpty(accountId))
            {
                var credits = await TryGetMonthlyCreditsAsync(accountId, headers, ct);
                if (credits is not null)
                {
                    await sink.EmitAsync(credits);
                    return;
                }
            }

            // Paid plan, no window and no credit cap (e.g. an unlimited seat): nothing to
            // show, but not a failure either — report it as an informational tile line.
            if (!string.IsNullOrEmpty(planType) &&
                !planType.Contains("free", StringComparison.OrdinalIgnoreCase))
            {
                var planLabel = char.ToUpperInvariant(planType[0]) + planType[1..];
                throw new UsageUnavailableException($"{planLabel} account: no usage data");
            }

            // No plan claim (or an explicitly free plan): the classic free-tier signature.
            throw new HttpRequestException(
                "ChatGPT usage needs a paid plan (Plus / Pro / Codex). "
                + "Free accounts don't expose usage limits."
                + (string.IsNullOrEmpty(planType) ? "" : $" (token plan: {planType})"));
        }

        foreach (var s in ParseSnapshots(body))
            await sink.EmitAsync(s);
    }

    private async Task<LimitSnapshot?> TryGetMonthlyCreditsAsync(
        string accountId, Dictionary<string, string> headers, CancellationToken ct)
    {
        try
        {
            var json = await _fetcher.FetchJsonAsync(MonthlyCreditsUrl(accountId), ct, headers);
            return ParseMonthlyCredits(json, DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException)
        {
            // 404 on plans without workspace spend controls — just means "no credit cap".
            return null;
        }
    }

    /// <summary>
    /// Parses the workspace spend-control response that Settings → Usage renders:
    /// { "effective_monthly_limit": { "limit": 7500, ... }, "current_month_usage": 1634.88 }.
    /// Returns null when no cap is configured (an unlimited seat has nothing to plot).
    /// </summary>
    public static LimitSnapshot? ParseMonthlyCredits(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("effective_monthly_limit", out var lim) ||
                lim.ValueKind != JsonValueKind.Object) return null;
            if (!lim.TryGetProperty("limit", out var lp) ||
                !lp.TryGetDouble(out var limit) || limit <= 0) return null;
            if (!root.TryGetProperty("current_month_usage", out var up) ||
                !up.TryGetDouble(out var used)) return null;

            // The allowance renews at the start of the next calendar month (the UI shows it
            // in local time — "1 Aug 02:00" is midnight UTC on a CEST clock).
            var resetsAt = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMonths(1);

            return new LimitSnapshot(
                Source.ChatGptWeb, LimitWindow.Monthly,
                Math.Clamp(used / limit, 0.0, 1.0), resetsAt);
        }
        catch { return null; }
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

    /// <summary>
    /// Extracts the ChatGPT account id and plan type from an access-token JWT. Both live
    /// under the "https://api.openai.com/auth" claim (with root-level fallbacks). Returns
    /// nulls for anything missing or unparseable — the caller then simply omits the
    /// account-scoping header and falls back to a generic diagnostic.
    /// </summary>
    public static (string? AccountId, string? PlanType) ParseAuthClaims(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2) return (null, null);

            using var doc = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            string? accountId = null, planType = null;
            if (root.TryGetProperty("https://api.openai.com/auth", out var auth) &&
                auth.ValueKind == JsonValueKind.Object)
            {
                accountId = ReadString(auth, "chatgpt_account_id");
                planType = ReadString(auth, "chatgpt_plan_type");
            }
            accountId ??= ReadString(root, "chatgpt_account_id");
            planType ??= ReadString(root, "chatgpt_plan_type");
            return (accountId, planType);
        }
        catch { return (null, null); }
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;

    private static string DecodeBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
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

            // Shape W (/wham/usage). Real payload (Codex CLI backend-client):
            //   { "rate_limit": { "primary_window": {...}, "secondary_window": {...} } }
            //   window: { "used_percent": 12.5, "reset_at": <epoch>, "limit_window_seconds": 18000 }
            // Older/variant shapes use "rate_limits" with "primary"/"secondary" and
            // "window_minutes"/"resets_in_seconds"; TryParseWindow accepts both.
            var rlObj = root;
            if (root.TryGetProperty("rate_limit", out var rl1) && rl1.ValueKind == JsonValueKind.Object)
                rlObj = rl1;
            else if (root.TryGetProperty("rate_limits", out var rl2) && rl2.ValueKind == JsonValueKind.Object)
                rlObj = rl2;
            if (TryGetWindow(rlObj, out var primary, "primary_window", "primary") &&
                TryParseWindow(primary, out var pSnap)) result.Add(pSnap!);
            if (TryGetWindow(rlObj, out var secondary, "secondary_window", "secondary") &&
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

    // Returns the first present object-valued property among the candidate names.
    private static bool TryGetWindow(JsonElement obj, out JsonElement window, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Object)
            {
                window = el;
                return true;
            }
        }
        window = default;
        return false;
    }

    // Parses one /wham/usage window. Accepts the real field names
    // (used_percent, limit_window_seconds, reset_at) and the older variants
    // (window_minutes, resets_in_seconds).
    private static bool TryParseWindow(JsonElement el, out LimitSnapshot? snap)
    {
        snap = null;
        if (el.ValueKind != JsonValueKind.Object) return false;
        if (!el.TryGetProperty("used_percent", out var up) || !up.TryGetDouble(out var pct))
            return false;

        var utilization = Math.Clamp(pct / 100.0, 0.0, 1.0);

        // Window length decides the lane. ChatGPT reports different windows per plan:
        // Plus/Pro get a ~5h primary + ~7d secondary, while free accounts get a single
        // ~30d window — so anything longer than a couple of weeks is Monthly, not Weekly.
        long? windowMinutes = null;
        if (el.TryGetProperty("window_minutes", out var wm) && wm.TryGetInt64(out var mins))
            windowMinutes = mins;
        else if (el.TryGetProperty("limit_window_seconds", out var ws) && ws.TryGetInt64(out var secs))
            windowMinutes = secs / 60;

        var window = windowMinutes switch
        {
            > 20160 => LimitWindow.Monthly,   // > 14d
            > 600 => LimitWindow.Weekly7d,    // > 10h
            _ => LimitWindow.Session5h,
        };

        // Prefer a relative countdown (resets_in_seconds); otherwise read an absolute
        // reset_at (epoch or ISO). ReadResetsAt handles both and falls back to now+window.
        var fallback = DateTimeOffset.UtcNow.AddHours(window switch
        {
            LimitWindow.Monthly => 24 * 30,
            LimitWindow.Weekly7d => 24 * 7,
            _ => 5,
        });
        // An absolute reset wins when present — it's exact. The relative fields are a
        // fallback: reset_after_seconds in particular mirrors the window length rather than
        // the true time remaining, so trusting it would push every reset a full window out.
        var absolute = AiUsage.Core.Adapters.LimitParsing.ReadResetsAt(
            el, DateTimeOffset.MinValue, "reset_at", "resets_at", "resetAt");

        DateTimeOffset resetsAt;
        if (absolute > DateTimeOffset.MinValue)
            resetsAt = absolute;
        else if (el.TryGetProperty("resets_in_seconds", out var rs) && rs.TryGetInt64(out var relSecs) && relSecs >= 0)
            resetsAt = DateTimeOffset.UtcNow.AddSeconds(relSecs);
        else if (el.TryGetProperty("reset_after_seconds", out var ra) && ra.TryGetInt64(out var afterSecs) && afterSecs >= 0)
            resetsAt = DateTimeOffset.UtcNow.AddSeconds(afterSecs);
        else
            resetsAt = fallback;

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
