using System.Text.Json;
using AiUsage.Core.Adapters;

namespace AiUsage.Core.Tests;

public class LimitParsingTests
{
private static JsonElement Parse(string json)
{
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.Clone();
}

    private static readonly DateTimeOffset Fallback =
        new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReadResetsAt_IsoString_IsParsed()
    {
        var el = Parse("""{ "reset_at": "2030-01-02T03:04:05Z" }""");
        var resets = LimitParsing.ReadResetsAt(el, Fallback, "reset_at");
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), resets);
    }

    [Fact]
    public void ReadResetsAt_EpochSeconds_IsParsed()
    {
        var el = Parse("""{ "resets_at": 1893456000 }""");
        var resets = LimitParsing.ReadResetsAt(el, Fallback, "reset_at", "resets_at");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), resets);
    }

    [Fact]
    public void ReadResetsAt_FirstMatchingKeyWins()
    {
        var el = Parse("""{ "resets_at": "2030-05-05T00:00:00Z", "reset_at": "2031-06-06T00:00:00Z" }""");
        // "reset_at" is listed first, so it is preferred over "resets_at".
        var resets = LimitParsing.ReadResetsAt(el, Fallback, "reset_at", "resets_at");
        Assert.Equal(new DateTimeOffset(2031, 6, 6, 0, 0, 0, TimeSpan.Zero), resets);
    }

    [Fact]
    public void ReadResetsAt_NoMatchingKey_ReturnsFallback()
    {
        var el = Parse("""{ "other": "2030-01-01T00:00:00Z" }""");
        var resets = LimitParsing.ReadResetsAt(el, Fallback, "reset_at", "resets_at");
        Assert.Equal(Fallback, resets);
    }

    [Fact]
    public void ReadResetsAt_UnparseableString_SkipsToFallback()
    {
        // A present-but-malformed value is skipped (no throw), falling back to the default.
        var el = Parse("""{ "reset_at": "not-a-date" }""");
        var resets = LimitParsing.ReadResetsAt(el, Fallback, "reset_at");
        Assert.Equal(Fallback, resets);
    }

    [Fact]
    public void ReadResetsAt_SkipsNullKey_UsesNext()
    {
        var el = Parse("""{ "reset_at": null, "resets_at": "2030-07-08T09:10:11Z" }""");
        var resets = LimitParsing.ReadResetsAt(el, Fallback, "reset_at", "resets_at");
        Assert.Equal(new DateTimeOffset(2030, 7, 8, 9, 10, 11, TimeSpan.Zero), resets);
    }
}
