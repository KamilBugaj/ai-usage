using System.Text.Json;

namespace AiUsage.Core.Adapters;

/// <summary>Shared JSON helpers for the usage adapters.</summary>
public static class LimitParsing
{
    /// <summary>
    /// Reads a reset timestamp from the first of <paramref name="keys"/> that carries a
    /// usable value. Accepts an ISO-8601 string or a Unix-epoch-seconds number; any key
    /// that is missing, null, or otherwise unparseable is skipped. Returns
    /// <paramref name="fallback"/> when none of the keys yield a value.
    /// </summary>
    public static DateTimeOffset ReadResetsAt(
        JsonElement el, DateTimeOffset fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!el.TryGetProperty(key, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(prop.GetString(), out var parsed))
                return parsed;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var epoch))
                return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }
        return fallback;
    }
}
