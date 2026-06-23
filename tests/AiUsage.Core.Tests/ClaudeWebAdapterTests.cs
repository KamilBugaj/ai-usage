using AiUsage.Core.Adapters.ClaudeWeb;
using AiUsage.Core.Models;

namespace AiUsage.Core.Tests;

public class ClaudeWebAdapterTests
{
    [Fact]
    public void ParseSnapshots_ShapeA_LimitsArray()
    {
        var json = """
            {
              "limits": [
                { "type": "session", "utilization": 0.6, "reset_at": "2030-01-01T12:00:00Z" },
                { "type": "weekly",  "utilization": 0.2, "reset_at": "2030-01-07T00:00:00Z" }
              ]
            }
            """;
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Equal(2, snaps.Count);
        Assert.Equal(0.6, snaps[0].Utilization);
        Assert.Equal(LimitWindow.Session5h, snaps[0].Window);
        Assert.Equal(LimitWindow.Weekly7d, snaps[1].Window);
    }

    [Fact]
    public void ParseSnapshots_ShapeB_MessageLimit()
    {
        var json = """
            {
              "message_limit": {
                "percent_full": 0.75,
                "reset_at": "2030-06-01T08:00:00Z"
              }
            }
            """;
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(0.75, snaps[0].Utilization);
    }

    [Fact]
    public void ParseSnapshots_MessagesUsedOverTotal_ComputesUtilization()
    {
        var json = """
            {
              "limits": [
                { "type": "session", "messages_used": 30, "messages_total": 50, "reset_at": "2030-01-01T12:00:00Z" }
              ]
            }
            """;
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(0.6, snaps[0].Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshots_UtilizationClamped_WhenOver1()
    {
        var json = """{"limits": [{"type": "session", "utilization": 1.5, "reset_at": "2030-01-01T00:00:00Z"}]}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(1.0, snaps[0].Utilization);
    }

    [Fact]
    public void ParseSnapshots_UnknownShape_ReturnsEmpty()
    {
        var snaps = ClaudeWebAdapter.ParseSnapshots("""{"something": "else"}""");
        Assert.Empty(snaps);
    }

    [Fact]
    public void ParseSnapshots_MalformedJson_ReturnsEmpty()
    {
        var snaps = ClaudeWebAdapter.ParseSnapshots("not json at all");
        Assert.Empty(snaps);
    }
}
