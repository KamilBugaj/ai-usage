using System.Net.Http;
using System.Text.Json;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.Core.Adapters.Copilot;

/// <summary>
/// Reads GitHub Copilot quota from the internal usage API used by the editor plugins:
/// GET https://api.github.com/copilot_internal/user, authorised with the GitHub OAuth
/// token (obtained via device flow). Reports the premium-interactions window as the
/// primary limit, falling back to the chat window. Percentages are real (not scraped).
/// </summary>
public sealed class CopilotApiAdapter : ISourceAdapter
{
    private const string UsageUrl = "https://api.github.com/copilot_internal/user";

    private readonly CopilotConfig _cfg;
    private static readonly HttpClient _http = new();

    public Source Source => Source.Copilot;
    public TimeSpan PollInterval => TimeSpan.FromMinutes(_cfg.PollIntervalMinutes);

    public CopilotApiAdapter(CopilotConfig cfg) => _cfg = cfg;

    public async Task PollOnceAsync(IUsageSink sink, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_cfg.OAuthToken))
            throw new InvalidOperationException("GitHub Copilot is not connected.");

        using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"token {_cfg.OAuthToken}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        // Editor-impersonation headers — the endpoint is plugin-only and 404s without them.
        req.Headers.TryAddWithoutValidation("Editor-Version", "vscode/1.96.2");
        req.Headers.TryAddWithoutValidation("Editor-Plugin-Version", "copilot-chat/0.26.7");
        req.Headers.TryAddWithoutValidation("User-Agent", "GitHubCopilotChat/0.26.7");
        req.Headers.TryAddWithoutValidation("X-Github-Api-Version", "2025-04-01");

        using var resp = await _http.SendAsync(req, ct);

        if ((int)resp.StatusCode is 401 or 403)
            throw new HttpRequestException(
                "GitHub rejected the Copilot token (401/403). Reconnect GitHub Copilot in Settings.");

        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync(ct);
        var snap = ParseSnapshot(body);
        if (snap is not null)
            await sink.EmitAsync(snap);
    }

    /// <summary>
    /// Parses the copilot_internal/user response into a monthly limit snapshot.
    /// Prefers premium_interactions, falls back to chat. Returns null when neither
    /// window carries a usable quota (e.g. token-based-billing placeholders).
    /// </summary>
    public static LimitSnapshot? ParseSnapshot(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("quota_snapshots", out var snaps) ||
                snaps.ValueKind != JsonValueKind.Object)
                return null;

            var resetsAt = ParseResetDate(root);

            // premium_interactions first, then chat.
            if (snaps.TryGetProperty("premium_interactions", out var premium) &&
                TryWindow(premium, resetsAt, out var pSnap))
                return pSnap;

            if (snaps.TryGetProperty("chat", out var chat) &&
                TryWindow(chat, resetsAt, out var cSnap))
                return cSnap;
        }
        catch { }
        return null;
    }

    // One quota window: { entitlement, remaining, percent_remaining, unlimited, quota_id }.
    private static bool TryWindow(JsonElement el, DateTimeOffset resetsAt, out LimitSnapshot? snap)
    {
        snap = null;
        if (el.ValueKind != JsonValueKind.Object) return false;

        var unlimited = el.TryGetProperty("unlimited", out var u) &&
                        u.ValueKind == JsonValueKind.True;
        if (unlimited)
        {
            snap = new LimitSnapshot(Source.Copilot, LimitWindow.Monthly, 0.0, resetsAt);
            return true;
        }

        double? percentRemaining =
            el.TryGetProperty("percent_remaining", out var pr) && pr.TryGetDouble(out var p) ? p : null;

        // Derive from entitlement/remaining when percent_remaining is absent.
        if (percentRemaining is null &&
            el.TryGetProperty("entitlement", out var ent) && ent.TryGetDouble(out var e) && e > 0 &&
            el.TryGetProperty("remaining", out var rem) && rem.TryGetDouble(out var r))
            percentRemaining = r / e * 100.0;

        if (percentRemaining is null) return false;

        // Skip zero-entitlement placeholders (Business token-based billing) — no real signal.
        if (el.TryGetProperty("entitlement", out var ent2) && ent2.TryGetDouble(out var ev) && ev == 0 &&
            el.TryGetProperty("remaining", out var rem2) && rem2.TryGetDouble(out var rv) && rv == 0)
            return false;

        var utilization = Math.Clamp((100.0 - percentRemaining.Value) / 100.0, 0.0, 1.0);
        snap = new LimitSnapshot(Source.Copilot, LimitWindow.Monthly, utilization, resetsAt);
        return true;
    }

    private static DateTimeOffset ParseResetDate(JsonElement root)
    {
        if (root.TryGetProperty("quota_reset_date", out var d) &&
            d.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(d.GetString(), out var parsed))
            return parsed;
        // Fallback: first day of next month.
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
    }
}
