namespace AiUsage.App.Features;

/// <summary>Shared formatting helpers for usage tiles.</summary>
internal static class TokenFormat
{
    /// <summary>Renders a token count compactly: 1.2K, 3.4M, or the raw number.</summary>
    public static string Tokens(long? n) => n switch
    {
        null         => "?",
        >= 1_000_000 => $"{n.Value / 1_000_000.0:F1}M",
        >= 1_000     => $"{n.Value / 1_000.0:F1}K",
        _            => n.Value.ToString()
    };
}
