using AiUsage.Core.Adapters.ChatGptWeb;
using AiUsage.Core.Models;

namespace AiUsage.Core.Tests;

public class ChatGptWebAdapterTests
{
    [Fact]
    public void ParseAccessToken_ExtractsToken()
    {
        var json = """{"accessToken":"eyJhbGci.eyJzdWIi.sig","user":{"id":"user-123"}}""";
        var token = ChatGptWebAdapter.ParseAccessToken(json);
        Assert.Equal("eyJhbGci.eyJzdWIi.sig", token);
    }

    [Fact]
    public void ParseAccessToken_Missing_ReturnsNull()
    {
        var json = """{"user":{"id":"user-123"}}""";
        Assert.Null(ChatGptWebAdapter.ParseAccessToken(json));
    }

    [Fact]
    public void ParseSnapshots_ShapeA_ModelsObject()
    {
        var json = """
            {
              "models": {
                "gpt-4o": { "limit": 80, "remaining": 32, "reset_at": "2030-01-01T12:00:00Z" }
              }
            }
            """;
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(0.6, snaps[0].Utilization, precision: 5);
        Assert.Equal(Source.ChatGptWeb, snaps[0].Source);
    }

    [Fact]
    public void ParseSnapshots_ShapeB_Array()
    {
        var json = """
            [
              { "limit": 40, "remaining": 10, "reset_at": "2030-01-01T12:00:00Z" }
            ]
            """;
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(0.75, snaps[0].Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshots_UtilizationClamped()
    {
        var json = """{"models": {"gpt-4": {"limit": 10, "remaining": -5, "reset_at": "2030-01-01T00:00:00Z"}}}""";
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(1.0, snaps[0].Utilization);
    }

    [Fact]
    public void ParseSnapshots_ZeroLimit_Skipped()
    {
        var json = """{"models": {"gpt-4": {"limit": 0, "remaining": 0}}}""";
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);
        Assert.Empty(snaps);
    }

    [Fact]
    public void ParseSnapshots_ShapeW_PrimaryAndSecondary()
    {
        // Current /wham/usage payload: rate_limits wrapper with two windows.
        var json = """
            {
              "rate_limits": {
                "primary":   { "used_percent": 12.5, "window_minutes": 299,   "resets_in_seconds": 3600 },
                "secondary": { "used_percent": 40.0, "window_minutes": 10080, "resets_in_seconds": 600000 }
              }
            }
            """;
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);

        Assert.Equal(2, snaps.Count);

        // primary → short (5h session) window
        Assert.Equal(LimitWindow.Session5h, snaps[0].Window);
        Assert.Equal(0.125, snaps[0].Utilization, precision: 5);
        Assert.Equal(Source.ChatGptWeb, snaps[0].Source);

        // secondary → long (weekly) window because window_minutes > 600
        Assert.Equal(LimitWindow.Weekly7d, snaps[1].Window);
        Assert.Equal(0.40, snaps[1].Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshots_ShapeW_RootLevelPrimary_NoWrapper()
    {
        // Some responses expose primary/secondary at the root, without rate_limits.
        var json = """{ "primary": { "used_percent": 80, "window_minutes": 300 } }""";
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);

        Assert.Single(snaps);
        Assert.Equal(LimitWindow.Session5h, snaps[0].Window);
        Assert.Equal(0.80, snaps[0].Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshots_ShapeW_ResetsInSeconds_SetsFutureReset()
    {
        var json = """{ "primary": { "used_percent": 10, "window_minutes": 299, "resets_in_seconds": 3600 } }""";
        var before = DateTimeOffset.UtcNow;
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);

        Assert.Single(snaps);
        // resets_in_seconds = 3600 → reset roughly one hour out (allow for test runtime).
        var delta = snaps[0].ResetsAt - before;
        Assert.InRange(delta.TotalSeconds, 3590, 3601);
    }

    [Fact]
    public void ParseSnapshots_ShapeW_UtilizationClamped()
    {
        var json = """{ "primary": { "used_percent": 130, "window_minutes": 299 } }""";
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(1.0, snaps[0].Utilization);
    }

    [Fact]
    public void ParseSnapshots_UnknownShape_ReturnsEmpty()
    {
        var snaps = ChatGptWebAdapter.ParseSnapshots("""{"something": "else"}""");
        Assert.Empty(snaps);
    }

    [Fact]
    public void ParseSnapshots_MalformedJson_ReturnsEmpty()
    {
        var snaps = ChatGptWebAdapter.ParseSnapshots("not json");
        Assert.Empty(snaps);
    }
}
