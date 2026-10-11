using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.Core.Adapters.Copilot;

/// <summary>
/// Reads the GitHub Copilot premium-request quota from the internal API the editor
/// plugins use: GET https://api.github.com/copilot_internal/user, authorised with the
/// GitHub OAuth token from the device flow. The endpoint is undocumented, so the parser
/// targets exactly one shape, the body captured from a live Business seat on 2026-10-09
/// and pinned as a fixture in CopilotApiAdapterTests. If the payload changes, capture the
/// new body and update the parser.
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

        // Throw rather than emit nothing: a poll that silently yields no snapshot leaves
        // the tile on "Loading…" with no explanation.
        var snap = ParseSnapshot(await resp.Content.ReadAsStringAsync(ct))
            ?? throw new InvalidOperationException(
                "GitHub returned no Copilot premium-request quota. Its response format may have changed.");

        await sink.EmitAsync(snap);
    }

    /// <summary>
    /// Parses the premium-request quota of a copilot_internal/user payload:
    /// { "quota_reset_date_utc": "2026-11-01T00:00:00.000Z",
    ///   "quota_snapshots": {
    ///     "premium_interactions": { "percent_remaining": 82.0, "unlimited": false,
    ///                               "entitlement": 15000, "remaining": 12305, … },
    ///     "chat": { "unlimited": true, … }, "completions": { "unlimited": true, … } } }
    /// Only premium_interactions is read: chat and completions are unlimited on paid seats
    /// and the tile is labelled "Premium". An unlimited premium quota reports 0 % used.
    /// Returns null when the quota or its reset date is missing.
    /// </summary>
    public static LimitSnapshot? ParseSnapshot(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // The payload also has a date-only quota_reset_date; the _utc field says the
            // same day without leaving the time zone to the parser.
            if (!root.TryGetProperty("quota_reset_date_utc", out var reset) ||
                reset.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var resetsAt)) return null;

            if (!root.TryGetProperty("quota_snapshots", out var quotas) ||
                quotas.ValueKind != JsonValueKind.Object ||
                !quotas.TryGetProperty("premium_interactions", out var premium) ||
                premium.ValueKind != JsonValueKind.Object) return null;

            double percentRemaining;
            if (premium.TryGetProperty("unlimited", out var unlimited) &&
                unlimited.ValueKind == JsonValueKind.True)
                percentRemaining = 100;
            else if (!premium.TryGetProperty("percent_remaining", out var pr) ||
                     pr.ValueKind != JsonValueKind.Number || !pr.TryGetDouble(out percentRemaining))
                return null;

            return new LimitSnapshot(
                Source.Copilot, LimitWindow.Monthly,
                Math.Clamp((100.0 - percentRemaining) / 100.0, 0.0, 1.0), resetsAt);
        }
        catch (JsonException) { return null; }
    }
}
