using AiUsage.Core.Adapters.Copilot;
using AiUsage.Core.Models;

namespace AiUsage.Core.Tests;

/// <summary>
/// The fixture is a real copilot_internal/user response from a Business seat, captured on
/// 2026-10-09, trimmed and with identifiers replaced. It is the contract the adapter is
/// written against: when the payload changes, capture the new body here first.
/// </summary>
public class CopilotApiAdapterTests
{
    private const string User = """
        {
          "id": 1001,
          "login": "someone",
          "access_type_sku": "copilot_for_business_seat_quota",
          "chat_enabled": true,
          "copilot_plan": "business",
          "quota_reset_date": "2026-11-01",
          "quota_snapshots": {
            "chat": { "overage_count": 0, "overage_permitted": false, "percent_remaining": 100.0, "quota_id": "chat", "quota_remaining": 0.0, "unlimited": true, "timestamp_utc": "2026-10-09T07:20:42.840Z", "has_quota": true, "quota_reset_at": 0, "token_based_billing": true, "credits_used": 0, "overage_entitlement": 0, "remaining": 0, "entitlement": 0 },
            "completions": { "overage_count": 0, "overage_permitted": false, "percent_remaining": 100.0, "quota_id": "completions", "quota_remaining": 0.0, "unlimited": true, "timestamp_utc": "2026-10-09T07:20:42.840Z", "has_quota": true, "quota_reset_at": 0, "token_based_billing": true, "credits_used": 0, "overage_entitlement": 0, "remaining": 0, "entitlement": 0 },
            "premium_interactions": { "overage_count": 0, "overage_permitted": true, "percent_remaining": 82.0, "quota_id": "premium_interactions", "quota_remaining": 12305.9, "unlimited": false, "timestamp_utc": "2026-10-09T07:20:42.840Z", "has_quota": true, "quota_reset_at": 0, "token_based_billing": true, "credits_used": 2694, "overage_entitlement": 0, "remaining": 12305, "entitlement": 15000 }
          },
          "quota_reset_date_utc": "2026-11-01T00:00:00.000Z",
          "token_based_billing": true
        }
        """;

    private static string Payload(string premium, string reset = "\"2026-11-01T00:00:00.000Z\"")
        => $$$"""{"quota_reset_date_utc":{{{reset}}},"quota_snapshots":{"premium_interactions":{{{premium}}}}}""";

    [Fact]
    public void ParseSnapshot_RealPayload_ReadsPremiumQuotaAndUtcReset()
    {
        var snap = CopilotApiAdapter.ParseSnapshot(User);

        Assert.NotNull(snap);
        Assert.Equal(Source.Copilot, snap.Source);
        Assert.Equal(LimitWindow.Monthly, snap.Window);
        Assert.Equal(0.18, snap.Utilization, precision: 5); // 100 - 82 % remaining
        // Midnight UTC exactly: the date-only quota_reset_date would have been read in
        // the machine's local zone.
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), snap.ResetsAt);
    }

    [Theory]
    [InlineData(100.0, 0.0)]
    [InlineData(25.0,  0.75)]
    [InlineData(0.0,   1.0)]
    [InlineData(-12.5, 1.0)]  // overage clamps at fully used
    public void ParseSnapshot_PercentRemaining_BecomesUtilization(double remaining, double expected)
    {
        var p = remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var snap = CopilotApiAdapter.ParseSnapshot(Payload($$"""{"percent_remaining":{{p}},"unlimited":false}"""));
        Assert.Equal(expected, snap!.Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshot_UnlimitedPremium_ReportsZeroUsage()
    {
        // The shape the live "chat" quota has: unlimited with zeroed counters.
        var snap = CopilotApiAdapter.ParseSnapshot(
            Payload("""{"percent_remaining":100.0,"unlimited":true,"remaining":0,"entitlement":0}"""));
        Assert.Equal(0.0, snap!.Utilization, precision: 5);
    }

    [Theory]
    [InlineData("""{"entitlement":300,"remaining":75,"unlimited":false}""")] // % is not derived from counters
    [InlineData("""{"percent_remaining":"82","unlimited":false}""")]
    [InlineData("null")]
    public void ParseSnapshot_NoUsablePremiumQuota_ReturnsNull(string premium)
        => Assert.Null(CopilotApiAdapter.ParseSnapshot(Payload(premium)));

    [Fact]
    public void ParseSnapshot_OnlyChatQuota_ReturnsNull()
    {
        // The tile is the premium-request quota; a chat quota is not a substitute for it.
        var json = """
            {
              "quota_reset_date_utc": "2026-11-01T00:00:00.000Z",
              "quota_snapshots": { "chat": { "percent_remaining": 20.0, "unlimited": false } }
            }
            """;
        Assert.Null(CopilotApiAdapter.ParseSnapshot(json));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"not a date\"")]
    public void ParseSnapshot_NoResetDate_ReturnsNull(string reset)
        => Assert.Null(CopilotApiAdapter.ParseSnapshot(
            Payload("""{"percent_remaining":82.0,"unlimited":false}""", reset)));

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ParseSnapshot_NotAUserPayload_ReturnsNull(string json)
        => Assert.Null(CopilotApiAdapter.ParseSnapshot(json));
}
