using System.Text.Json;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.Core.Adapters.ChatGptWeb;

/// <summary>
/// Reads usage from chatgpt.com's own web API through the signed-in browser session
/// (chatgpt.com sits behind Cloudflare and answers plain HttpClient requests with 403).
/// The API is internal and undocumented, so each parser targets exactly one shape, pinned
/// as a fixture in ChatGptWebAdapterTests: bodies captured from live free and Business
/// accounts, plus the two-window Plus/Pro form as the open-source Codex CLI reads it from
/// the same endpoint. No alternative field names or endpoints are tried on spec. If a
/// payload changes, capture the new body and update the parser.
/// </summary>
public sealed class ChatGptWebAdapter : ISourceAdapter
{
    // NextAuth session (cookie auth). Yields the short-lived access token the API wants.
    private const string SessionUrl = "https://chatgpt.com/api/auth/session";

    // The rate-limit windows of the signed-in account; also what the Codex CLI polls.
    private const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    // Business/Enterprise seats have no rate-limit window. Their cap is an admin-set
    // monthly credit allowance, the number Settings → Usage renders.
    private static string MonthlyCreditsUrl(string accountId)
        => $"https://chatgpt.com/backend-api/accounts/{accountId}/spend-controls/current-user/monthly-usage";

    // A window's length decides its lane: Plus/Pro report ~5h and ~7d, free accounts ~30d.
    private const double TenHoursSeconds = 10 * 60 * 60;
    private const double TwoWeeksSeconds = 14 * 24 * 60 * 60;

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
        var accessToken = ParseAccessToken(await FetchAsync("session", SessionUrl, null, ct))
            ?? throw new InvalidOperationException("ChatGPT session carries no access token. Reconnect ChatGPT in Settings.");
        var (accountId, planType) = ParseAuthClaims(accessToken);

        // The usage endpoints are account-scoped: without ChatGPT-Account-Id an account with
        // several workspaces (personal + Business) can get another workspace's answer.
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}" };
        if (accountId is not null)
            headers["ChatGPT-Account-Id"] = accountId;

        var windows = ParseSnapshots(await FetchAsync("usage", UsageUrl, headers, ct));
        if (windows.Count > 0)
        {
            await sink.EmitLimitsAsync(windows);
            return;
        }

        // A failed request surfaces like any other: reading it as "no cap" would report a
        // capped seat as having no usage data whenever the endpoint hiccups.
        if (accountId is not null &&
            ParseMonthlyCredits(
                await FetchAsync("credits", MonthlyCreditsUrl(accountId), headers, ct),
                DateTimeOffset.UtcNow) is { } credits)
        {
            await sink.EmitLimitsAsync([credits]);
            return;
        }

        // Neither a window nor a credit cap. On a paid seat that is a normal state (an
        // uncapped Business seat), so it is reported as a muted line, not an error.
        if (planType is not null && !planType.Contains("free", StringComparison.OrdinalIgnoreCase))
            throw new UsageUnavailableException(
                $"{char.ToUpperInvariant(planType[0])}{planType[1..]} account: no usage data");

        throw new InvalidOperationException(
            "ChatGPT returned no usage data for this account."
            + (planType is null ? "" : $" (plan: {planType})"));
    }

    private async Task<string> FetchAsync(
        string what, string url, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        try
        {
            return await _fetcher.FetchJsonAsync(url, ct, headers);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"ChatGPT {what} request failed: {ex.Message}", ex);
        }
    }


    // ── GET /api/auth/session ─────────────────────────────────────────────────

    /// <summary>Reads the access token (a JWT) from { "accessToken": "…", "user": {…} }.</summary>
    public static string? ParseAccessToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? ReadString(doc.RootElement, "accessToken")
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Extracts the account id and plan type from the access token's payload, where they
    /// sit under one namespaced claim:
    /// { "https://api.openai.com/auth": { "chatgpt_account_id": "…", "chatgpt_plan_type": "business" } }
    /// Returns nulls for anything missing or unparseable.
    /// </summary>
    public static (string? AccountId, string? PlanType) ParseAuthClaims(string accessToken)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length < 2) return (null, null);

            using var doc = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("https://api.openai.com/auth", out var auth) ||
                auth.ValueKind != JsonValueKind.Object) return (null, null);

            return (ReadString(auth, "chatgpt_account_id"), ReadString(auth, "chatgpt_plan_type"));
        }
        catch (Exception ex) when (ex is JsonException or FormatException) { return (null, null); }
    }

    private static string DecodeBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    // ── GET /backend-api/wham/usage ───────────────────────────────────────────

    /// <summary>
    /// Parses the rate-limit windows of a wham/usage payload:
    /// { "plan_type": "free",
    ///   "rate_limit": { "primary_window": { "used_percent": 5, "limit_window_seconds": 2592000,
    ///                                       "reset_at": 1786730882 },
    ///                   "secondary_window": null } }
    /// Plus/Pro carry a ~5h primary and a ~7d secondary; free accounts one ~30d primary.
    /// Business seats answer with "rate_limit": null, which yields no window.
    /// </summary>
    public static IReadOnlyList<LimitSnapshot> ParseSnapshots(string json)
    {
        var result = new List<LimitSnapshot>(2);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("rate_limit", out var rateLimit) ||
                rateLimit.ValueKind != JsonValueKind.Object) return result;

            foreach (var key in new[] { "primary_window", "secondary_window" })
                if (rateLimit.TryGetProperty(key, out var el) && ParseWindow(el) is { } snap)
                    result.Add(snap);
        }
        catch (JsonException) { }

        return result;
    }

    private static LimitSnapshot? ParseWindow(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object ||
            !TryReadNumber(el, "used_percent", out var usedPercent) ||
            !TryReadNumber(el, "limit_window_seconds", out var windowSeconds) ||
            // reset_at is absolute epoch seconds. The payload also has reset_after_seconds,
            // but that mirrors the window length, not the time actually remaining.
            !TryReadNumber(el, "reset_at", out var resetAt)) return null;

        var window = windowSeconds switch
        {
            > TwoWeeksSeconds => LimitWindow.Monthly,
            > TenHoursSeconds => LimitWindow.Weekly7d,
            _ => LimitWindow.Session5h,
        };

        return new LimitSnapshot(
            Source.ChatGptWeb, window,
            Math.Clamp(usedPercent / 100.0, 0.0, 1.0),
            DateTimeOffset.FromUnixTimeSeconds((long)resetAt));
    }

    // ── GET /backend-api/accounts/{id}/spend-controls/current-user/monthly-usage ──

    /// <summary>
    /// Parses a Business seat's monthly credit allowance:
    /// { "effective_monthly_limit": { "limit": 7500, "enforcement_mode": "HARD_CAP",
    ///                                "limit_mode": "amount_credits" },
    ///   "current_month_usage": 1634.88 }
    /// Returns null when no cap is configured (an unlimited seat has nothing to plot).
    /// </summary>
    public static LimitSnapshot? ParseMonthlyCredits(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("effective_monthly_limit", out var cap) ||
                cap.ValueKind != JsonValueKind.Object ||
                !TryReadNumber(cap, "limit", out var limit) || limit <= 0 ||
                !TryReadNumber(root, "current_month_usage", out var used)) return null;

            // The payload carries no reset time: the allowance renews with the calendar
            // month, which is also how the ChatGPT UI derives the date it shows.
            var resetsAt = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero)
                .AddMonths(1);

            return new LimitSnapshot(
                Source.ChatGptWeb, LimitWindow.Monthly,
                Math.Clamp(used / limit, 0.0, 1.0), resetsAt);
        }
        catch (JsonException) { return null; }
    }

    // ── Field readers ─────────────────────────────────────────────────────────

    // Empty strings read as absent.
    private static string? ReadString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
           p.GetString() is { Length: > 0 } s
            ? s : null;

    private static bool TryReadNumber(JsonElement el, string name, out double value)
    {
        value = 0;
        return el.TryGetProperty(name, out var p) &&
               p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out value);
    }
}
