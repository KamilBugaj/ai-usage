using AiUsage.Core.Adapters.Copilot;
using AiUsage.Core.Models;

namespace AiUsage.Core.Tests;

public class CopilotApiAdapterTests
{
    [Fact]
    public void ParseSnapshot_PremiumInteractions_UsesPercentRemaining()
    {
        var json = """
            {
              "copilot_plan": "business",
              "quota_reset_date": "2030-02-01",
              "quota_snapshots": {
                "premium_interactions": { "entitlement": 300, "remaining": 75, "percent_remaining": 25, "unlimited": false },
                "chat": { "unlimited": true, "percent_remaining": 100 }
              }
            }
            """;
        var snap = CopilotApiAdapter.ParseSnapshot(json);
        Assert.NotNull(snap);
        Assert.Equal(Source.Copilot, snap!.Source);
        Assert.Equal(LimitWindow.Monthly, snap.Window);
        Assert.Equal(0.75, snap.Utilization, precision: 5); // 100 - 25 = 75% used
        Assert.Equal(2030, snap.ResetsAt.Year);
        Assert.Equal(2, snap.ResetsAt.Month);
    }

    [Fact]
    public void ParseSnapshot_FallsBackToChat_WhenNoPremium()
    {
        var json = """
            {
              "quota_reset_date": "2030-03-01",
              "quota_snapshots": {
                "chat": { "entitlement": 50, "remaining": 10, "percent_remaining": 20 }
              }
            }
            """;
        var snap = CopilotApiAdapter.ParseSnapshot(json);
        Assert.NotNull(snap);
        Assert.Equal(0.8, snap!.Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshot_SkipsZeroEntitlementPlaceholder()
    {
        var json = """
            {
              "quota_snapshots": {
                "premium_interactions": { "entitlement": 0, "remaining": 0, "percent_remaining": 100 }
              }
            }
            """;
        // Placeholder premium is skipped and there is no chat window → null.
        Assert.Null(CopilotApiAdapter.ParseSnapshot(json));
    }

    [Fact]
    public void ParseSnapshot_Unlimited_ReportsZeroUsage()
    {
        var json = """
            {
              "quota_snapshots": { "premium_interactions": { "unlimited": true } }
            }
            """;
        var snap = CopilotApiAdapter.ParseSnapshot(json);
        Assert.NotNull(snap);
        Assert.Equal(0.0, snap!.Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshot_GarbageJson_ReturnsNull()
    {
        Assert.Null(CopilotApiAdapter.ParseSnapshot("not json"));
        Assert.Null(CopilotApiAdapter.ParseSnapshot("{}"));
    }
}
