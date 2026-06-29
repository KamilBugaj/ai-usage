using System.Text.Json;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.Core.Adapters.ClaudeWeb;

public sealed class ClaudeWebAdapter : ISourceAdapter
{
    private const string BaseUrl = "https://claude.ai/api";

    private readonly IBrowserFetcher _fetcher;
    private readonly ClaudeWebConfig? _cfg;
    private string? _cachedOrgId;

    public Source Source => Source.ClaudeWeb;
    public TimeSpan PollInterval => TimeSpan.FromMinutes(_cfg?.PollIntervalMinutes ?? 1);

    public ClaudeWebAdapter(IBrowserFetcher fetcher, ClaudeWebConfig? cfg = null)
    {
        _fetcher = fetcher;
        _cfg = cfg;
        _cachedOrgId = cfg?.OrganizationId;
    }

    public async Task PollOnceAsync(IUsageSink sink, CancellationToken ct)
    {
        foreach (var s in await ResolveAndFetchAsync(ct))
            await sink.EmitAsync(s);
    }

    // Resolves which organisation to query, then returns its usage snapshots.
    private async Task<IReadOnlyList<LimitSnapshot>> ResolveAndFetchAsync(CancellationToken ct)
    {
        // Known org (from config or a previous successful poll) → query it directly.
        if (_cachedOrgId is not null)
            return await FetchForOrgAsync(_cachedOrgId, ct) ?? [];

        // Auto-discovery: an account can belong to several organisations (personal +
        // team/api). Usage only exists for the consumer org, which is NOT necessarily
        // first in the list — so try each (chat-capable first) and keep the one that
        // actually returns data. Picking the first org blindly is why a fresh install
        // could show an empty Claude tile while a machine with a cached orgId worked.
        var body = await _fetcher.FetchJsonAsync($"{BaseUrl}/organizations", ct);
        var orgIds = ParseOrgIds(body);
        if (orgIds.Count == 0)
            throw new InvalidOperationException("No organisations found on this Claude.ai account.");

        foreach (var id in orgIds)
        {
            IReadOnlyList<LimitSnapshot>? snaps;
            try { snaps = await FetchForOrgAsync(id, ct); }
            catch (HttpRequestException) { continue; } // org rejects usage — try the next
            if (snaps is { Count: > 0 })
            {
                _cachedOrgId = id; // remember the working org for subsequent polls
                return snaps;
            }
        }

        throw new InvalidOperationException(
            "Connected, but no Claude.ai organisation returned usage data.");
    }

    // Tries /usage, falling back to /rate_limits on 404.
    private async Task<IReadOnlyList<LimitSnapshot>?> FetchForOrgAsync(string orgId, CancellationToken ct)
        => await FetchSnapshotsAsync(orgId, "usage", ct)
        ?? await FetchSnapshotsAsync(orgId, "rate_limits", ct);

    private async Task<IReadOnlyList<LimitSnapshot>?> FetchSnapshotsAsync(
        string orgId, string endpoint, CancellationToken ct)
    {
        var url = $"{BaseUrl}/organizations/{orgId}/{endpoint}";
        try
        {
            var body = await _fetcher.FetchJsonAsync(url, ct);
            return ParseSnapshots(body);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("HTTP 404"))
        {
            return null;
        }
    }

    public static string? ParseFirstOrgId(string json) => ParseOrgIds(json).FirstOrDefault();

    /// <summary>
    /// Returns every organisation id from the /api/organizations payload, ordered so that
    /// chat-capable orgs (the consumer account that has usage limits) come first. Accepts
    /// both flat arrays and { "data": [...] }, and the "uuid" (claude.ai) or "id" field.
    /// </summary>
    public static IReadOnlyList<string> ParseOrgIds(string json)
    {
        var orgs = new List<(string id, int rank)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
                array = root;
            else if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                array = data;
            else
                return [];

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = ReadOrgId(item);
                if (id is not null) orgs.Add((id, OrgRank(item)));
            }
        }
        catch { }

        // Stable sort by preference: chat-capable, then non-API-only, then the rest —
        // mirrors how the consumer (chat) org is chosen on claude.ai.
        return orgs.OrderBy(o => o.rank).Select(o => o.id).ToList();
    }

    // The org identifier has been "uuid" on claude.ai and "id" on older/other shapes.
    private static string? ReadOrgId(JsonElement item)
    {
        foreach (var key in new[] { "uuid", "id" })
            if (item.TryGetProperty(key, out var v) &&
                v.ValueKind == JsonValueKind.String &&
                !string.IsNullOrEmpty(v.GetString()))
                return v.GetString();
        return null;
    }

    // 0 = has "chat" capability (consumer org), 1 = not API-only, 2 = API-only/unknown.
    private static int OrgRank(JsonElement item)
    {
        var caps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (item.TryGetProperty("capabilities", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var c in arr.EnumerateArray())
                if (c.ValueKind == JsonValueKind.String && c.GetString() is { } s)
                    caps.Add(s);

        if (caps.Contains("chat")) return 0;
        var isApiOnly = caps.Count > 0 && caps.Count == 1 && caps.Contains("api");
        return isApiOnly ? 2 : 1;
    }

    public static IReadOnlyList<LimitSnapshot> ParseSnapshots(string json)
    {
        var result = new List<LimitSnapshot>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object) return result;

            // Shape A: { "limits": [{ "type": "...", "utilization": 0.6, "reset_at": "..." }] }
            if (root.TryGetProperty("limits", out var limitsArr) &&
                limitsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in limitsArr.EnumerateArray())
                    if (TryParseLimit(item, out var snap)) result.Add(snap!);
                if (result.Count > 0) return result;
            }

            // Shape B: { "message_limit": { "percent_full": 0.6, "reset_at": "..." } }
            if (root.TryGetProperty("message_limit", out var msgLimit))
            {
                if (TryParseMessageLimit(msgLimit, LimitWindow.Session5h, out var snap))
                    result.Add(snap!);
                return result;
            }

            // Shape C: { "session": {...}, "weekly": {...} }
            if (root.TryGetProperty("session", out var session))
                if (TryParseMessageLimit(session, LimitWindow.Session5h, out var snap)) result.Add(snap!);
            if (root.TryGetProperty("weekly", out var weekly))
                if (TryParseMessageLimit(weekly, LimitWindow.Weekly7d, out var snap)) result.Add(snap!);
            if (result.Count > 0) return result;

            // Shape D: { "five_hour": { "utilization": 58.0, "resets_at": "..." }, "seven_day": {...} }
            // utilization may be null when limit is active but no usage tracked yet → default 0.
            if (root.TryGetProperty("five_hour", out var fiveHour) &&
                fiveHour.ValueKind == JsonValueKind.Object)
                if (TryParseMessageLimit(fiveHour, LimitWindow.Session5h, out var snap, allowNullUtil: true, percentScale: true)) result.Add(snap!);
            if (root.TryGetProperty("seven_day", out var sevenDay) &&
                sevenDay.ValueKind == JsonValueKind.Object)
                if (TryParseMessageLimit(sevenDay, LimitWindow.Weekly7d, out var snap, allowNullUtil: true, percentScale: true)) result.Add(snap!);
        }
        catch { }

        return result;
    }

    private static bool TryParseLimit(JsonElement el, out LimitSnapshot? snap)
    {
        snap = null;
        var window = LimitWindow.Session5h;

        if (el.TryGetProperty("type", out var typeProp))
        {
            window = typeProp.GetString() switch
            {
                "weekly" or "weekly_7d" or "week" => LimitWindow.Weekly7d,
                "monthly" => LimitWindow.Monthly,
                _ => LimitWindow.Session5h
            };
        }

        return TryParseMessageLimit(el, window, out snap);
    }

    private static bool TryParseMessageLimit(
        JsonElement el, LimitWindow window, out LimitSnapshot? snap,
        bool allowNullUtil = false, bool percentScale = false)
    {
        snap = null;
        double utilization = -1;

        // utilization / percent_full carry the value on either a 0–1 fraction scale
        // (Shapes A/B/C) or a 0–100 percentage scale (Shape D — the real claude.ai
        // payload, e.g. "utilization": 58.0). The scale is fixed per shape via
        // percentScale; guessing it from the value mis-read low percentages such as
        // 1.0 ("1 %") as a full 1.0 fraction and showed them as 100 %.
        if (el.TryGetProperty("utilization", out var util) && util.TryGetDouble(out var u))
            utilization = percentScale ? u / 100.0 : u;
        else if (el.TryGetProperty("percent_full", out var pf) && pf.TryGetDouble(out var p))
            utilization = percentScale ? p / 100.0 : p;
        else if (el.TryGetProperty("messages_used", out var used) && used.TryGetInt64(out var usedV) &&
                 el.TryGetProperty("messages_total", out var total) && total.TryGetInt64(out var totalV) &&
                 totalV > 0)
            utilization = (double)usedV / totalV; // already a 0–1 fraction

        // For Shape D keys (five_hour / seven_day), null utilization means limit exists but
        // no usage tracked yet — treat as 0% rather than skipping the entry entirely.
        if (utilization < 0 && allowNullUtil) utilization = 0;
        if (utilization < 0) return false;
        utilization = Math.Clamp(utilization, 0.0, 1.0);

        var resetsAt = AiUsage.Core.Adapters.LimitParsing.ReadResetsAt(
            el, DateTimeOffset.UtcNow.AddHours(5), "reset_at", "resets_at", "resetAt");

        snap = new LimitSnapshot(Source.ClaudeWeb, window, utilization, resetsAt);
        return true;
    }
}
