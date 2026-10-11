using System.Globalization;
using System.Text.Json;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.Core.Adapters.ClaudeWeb;

/// <summary>
/// Reads usage from claude.ai's own web API through the signed-in browser session.
/// The API is internal and undocumented, so every parser here targets exactly one shape:
/// the one captured from a live account on 2026-10-08 and pinned as a fixture in
/// ClaudeWebAdapterTests. No alternative field names, scales or endpoints are tried on
/// spec. If Anthropic changes a payload the tile reports an error, and the fix is to
/// capture the new body and update the parser, not to add a fallback for a shape nobody
/// has seen.
/// </summary>
public sealed class ClaudeWebAdapter : ISourceAdapter
{
    private const string BaseUrl = "https://claude.ai/api";

    // The two limit windows of a /usage body, with the length each runs for.
    private static readonly (string Key, LimitWindow Window, TimeSpan Length)[] Windows =
    [
        ("five_hour", LimitWindow.Session5h, TimeSpan.FromHours(5)),
        ("seven_day", LimitWindow.Weekly7d, TimeSpan.FromDays(7)),
    ];

    private readonly IBrowserFetcher _fetcher;
    private readonly ClaudeWebConfig? _cfg;
    private readonly bool _fetchCredits;
    private string? _cachedOrgId;

    public Source Source => Source.ClaudeWeb;
    public TimeSpan PollInterval => TimeSpan.FromMinutes(_cfg?.PollIntervalMinutes ?? 1);

    public ClaudeWebAdapter(IBrowserFetcher fetcher, ClaudeWebConfig? cfg = null, bool fetchCredits = false)
    {
        _fetcher = fetcher;
        _cfg = cfg;
        _fetchCredits = fetchCredits;
        _cachedOrgId = cfg?.OrganizationId;
    }

    public async Task PollOnceAsync(IUsageSink sink, CancellationToken ct)
    {
        var usage = await ResolveAndFetchAsync(ct);

        foreach (var s in usage.Limits)
            await sink.EmitAsync(s);

        if (_fetchCredits)
        {
            var credits = new List<CreditSnapshot>(2);
            if (await TryFetchCreditsAsync(usage.OrgId, ct) is { } prepaid) credits.Add(prepaid);
            if (usage.IncludedCredit is { } included) credits.Add(included);
            await sink.EmitCreditsAsync(credits);
        }
    }

    // One organisation's parsed /usage body.
    private sealed record OrgUsage(
        string OrgId, IReadOnlyList<LimitSnapshot> Limits, CreditSnapshot? IncludedCredit);

    // Picks the organisation to report on and returns its usage.
    private async Task<OrgUsage> ResolveAndFetchAsync(CancellationToken ct)
    {
        // Known org (from config or a previous successful poll) → query it directly.
        if (_cachedOrgId is not null)
        {
            var usage = await FetchUsageAsync(_cachedOrgId, ct);
            // Throw rather than emit nothing: a poll that silently yields no window
            // strands the tile on its last value (or on "Loading…") with no explanation.
            if (usage.Limits.Count == 0)
                throw new InvalidOperationException(
                    "Claude.ai returned no usage limits. Its response format may have changed.");
            return usage;
        }

        // Auto-discovery: an account can belong to several organisations (personal +
        // team/api). Usage only exists for the consumer org, which is NOT necessarily
        // first in the list — so try each (chat-capable first) and keep the one that
        // actually returns data. Picking the first org blindly is why a fresh install
        // could show an empty Claude tile while a machine with a cached orgId worked.
        var orgIds = ParseOrgIds(await _fetcher.FetchJsonAsync($"{BaseUrl}/organizations", ct));
        if (orgIds.Count == 0)
            throw new InvalidOperationException("No organisations found on this Claude.ai account.");

        foreach (var id in orgIds)
        {
            OrgUsage usage;
            try { usage = await FetchUsageAsync(id, ct); }
            catch (HttpRequestException) { continue; } // org rejects usage — try the next
            if (usage.Limits.Count > 0)
            {
                _cachedOrgId = id; // remember the working org for subsequent polls
                return usage;
            }
        }

        throw new InvalidOperationException(
            "Connected, but no Claude.ai organisation returned usage data.");
    }

    private async Task<OrgUsage> FetchUsageAsync(string orgId, CancellationToken ct)
    {
        var body = await _fetcher.FetchJsonAsync($"{BaseUrl}/organizations/{orgId}/usage", ct);
        var now = DateTimeOffset.UtcNow;
        return new OrgUsage(orgId, ParseSnapshots(body, now), ParseIncludedCredit(body, now));
    }

    // Best-effort: the balance is an optional extra, so a failure here (usage credits never
    // set up, fetch stalled) must not turn a tile with valid limits red.
    private async Task<CreditSnapshot?> TryFetchCreditsAsync(string orgId, CancellationToken ct)
    {
        try
        {
            var body = await _fetcher.FetchJsonAsync(
                $"{BaseUrl}/organizations/{orgId}/prepaid/credits", ct);
            return ParseCredits(body);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    // ── GET /api/organizations ────────────────────────────────────────────────

    /// <summary>
    /// Returns the organisation uuids from the /organizations payload, a flat array:
    /// [ { "uuid": "…", "capabilities": ["claude_pro", "chat"] },
    ///   { "uuid": "…", "capabilities": ["api", "api_individual"] } ]
    /// Chat-capable orgs come first: the limits belong to the consumer (chat) org, and
    /// the API returns it in no particular position.
    /// </summary>
    public static IReadOnlyList<string> ParseOrgIds(string json)
    {
        var chat = new List<string>();
        var other = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            foreach (var org in doc.RootElement.EnumerateArray())
            {
                if (org.ValueKind != JsonValueKind.Object ||
                    !org.TryGetProperty("uuid", out var uuid) ||
                    uuid.ValueKind != JsonValueKind.String ||
                    uuid.GetString() is not { Length: > 0 } id) continue;

                (HasCapability(org, "chat") ? chat : other).Add(id);
            }
        }
        catch (JsonException) { return []; }

        return [.. chat, .. other];
    }

    private static bool HasCapability(JsonElement org, string capability)
    {
        if (!org.TryGetProperty("capabilities", out var caps) || caps.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var c in caps.EnumerateArray())
            if (c.ValueKind == JsonValueKind.String && c.ValueEquals(capability)) return true;
        return false;
    }

    // ── GET /api/organizations/{org}/usage ────────────────────────────────────

    /// <summary>
    /// Parses the limit windows of a /usage payload:
    /// { "five_hour": { "utilization": 64.0, "resets_at": "2026-10-08T22:10:00.074831+00:00", … },
    ///   "seven_day": { "utilization": 12.0, "resets_at": "2026-10-14T15:00:00.074856+00:00", … }, … }
    /// A window the plan does not have is null and is skipped. The payload carries many
    /// other keys (per-model windows, a "limits" array, spend data); none are read here.
    /// </summary>
    public static IReadOnlyList<LimitSnapshot> ParseSnapshots(string json)
        => ParseSnapshots(json, DateTimeOffset.UtcNow);

    public static IReadOnlyList<LimitSnapshot> ParseSnapshots(string json, DateTimeOffset now)
    {
        var result = new List<LimitSnapshot>(Windows.Length);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;

            foreach (var (key, window, length) in Windows)
            {
                if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Object)
                    continue;

                // utilization is a percentage (64.0 = 64 %), never a 0–1 fraction. It is null
                // on a window with no usage yet: that is 0 %, not a missing limit.
                var percent = TryReadDecimal(el, "utilization", out var u) ? (double)u : 0;

                // resets_at can be null on an idle window (seen in other clients' captures,
                // not on our account). No countdown is running then, so the window is
                // reported at its full length.
                var resetsAt = ReadTimestamp(el, "resets_at") ?? now + length;

                result.Add(new LimitSnapshot(
                    Source.ClaudeWeb, window, Math.Clamp(percent / 100.0, 0.0, 1.0), resetsAt));
            }
        }
        catch (JsonException) { }

        return result;
    }

    /// <summary>
    /// Parses the grant that Settings → Usage labels "Included credit" out of a /usage
    /// payload. It sits under the codename key iguana_necktie, in WHOLE DOLLARS:
    /// { "iguana_necktie": { "limit_dollars": 100, "used_dollars": 0.0,
    ///   "remaining_dollars": 100.0, "resets_at": "2026-11-05T07:59:00+00:00",
    ///   "locked_reason": null } }
    /// Here resets_at is the grant's expiry. The key is null on accounts without a grant.
    /// Returns null unless there is a usable balance: nothing left, locked and expired
    /// all read as "no included credit".
    /// </summary>
    public static CreditSnapshot? ParseIncludedCredit(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("iguana_necktie", out var el) ||
                el.ValueKind != JsonValueKind.Object) return null;

            if (el.TryGetProperty("locked_reason", out var locked) &&
                locked.ValueKind != JsonValueKind.Null) return null;

            if (!TryReadDecimal(el, "remaining_dollars", out var remaining) || remaining <= 0)
                return null;

            var expiresAt = ReadTimestamp(el, "resets_at");
            if (expiresAt <= now) return null;

            return new CreditSnapshot(Source.ClaudeWeb, CreditKind.Included, remaining, "USD", expiresAt);
        }
        catch (JsonException) { return null; }
    }

    // ── GET /api/organizations/{org}/prepaid/credits ──────────────────────────

    /// <summary>
    /// Parses the purchased balance that Settings → Usage labels "Usage credits":
    /// { "amount": 944, "currency": "USD", … }. amount is in MINOR UNITS (944 = $9.44),
    /// unlike the whole-dollar fields of /usage. Returns null when the balance is
    /// missing or malformed.
    /// </summary>
    public static CreditSnapshot? ParseCredits(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!TryReadDecimal(root, "amount", out var minorUnits) || minorUnits < 0) return null;

            if (!root.TryGetProperty("currency", out var cur) ||
                cur.ValueKind != JsonValueKind.String) return null;
            var currency = cur.GetString()!.Trim().ToUpperInvariant();
            if (currency.Length == 0) return null;

            return new CreditSnapshot(Source.ClaudeWeb, CreditKind.Prepaid, minorUnits / 100m, currency);
        }
        catch (JsonException) { return null; }
    }

    // ── Field readers ─────────────────────────────────────────────────────────

    private static bool TryReadDecimal(JsonElement el, string name, out decimal value)
    {
        value = 0;
        return el.TryGetProperty(name, out var p) &&
               p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out value);
    }

    // Timestamps are ISO-8601 strings with an explicit offset.
    private static DateTimeOffset? ReadTimestamp(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String &&
           DateTimeOffset.TryParse(p.GetString(), CultureInfo.InvariantCulture,
               DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
