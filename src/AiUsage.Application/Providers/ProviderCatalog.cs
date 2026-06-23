namespace AiUsage.Application.Providers;

// Key matches the Source enum name. AlertUnit drives the threshold label in settings
// and the comparison metric in the sink ("%" for all current providers).
public sealed record ProviderInfo(string Key, string DisplayName, string AlertUnit);

/// <summary>
/// Single source of truth for the set of providers and their display metadata.
/// Consumed by the host (tile ordering) and the settings editor (tile rows).
/// </summary>
public static class ProviderCatalog
{
    // Default display order.
    public static readonly IReadOnlyList<ProviderInfo> All =
    [
        new("ClaudeWeb",  "Claude.ai",      "%"),
        new("ChatGptWeb", "ChatGPT",        "%"),
        new("Copilot",    "GitHub Copilot", "%"),
    ];

    public static int DefaultOrder(string key)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i].Key == key) return i;
        return int.MaxValue;
    }
}
