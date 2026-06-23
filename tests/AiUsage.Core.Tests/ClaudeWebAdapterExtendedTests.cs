using AiUsage.Core.Adapters.ClaudeWeb;
using AiUsage.Core.Models;

namespace AiUsage.Core.Tests;

/// <summary>
/// Additional edge-case tests for ClaudeWebAdapter, complementing ClaudeWebAdapterTests.
/// </summary>
public class ClaudeWebAdapterExtendedTests
{
    // ── ParseFirstOrgId ───────────────────────────────────────────────────────

    [Fact]
    public void ParseFirstOrgId_DataArray_ReturnsFirstId()
    {
        var id = ClaudeWebAdapter.ParseFirstOrgId("""{"data":[{"id":"org-abc"}]}""");
        Assert.Equal("org-abc", id);
    }

    [Fact]
    public void ParseFirstOrgId_FlatArray_ReturnsFirstId()
    {
        var id = ClaudeWebAdapter.ParseFirstOrgId("""[{"id":"org-xyz"},{"id":"org-other"}]""");
        Assert.Equal("org-xyz", id);
    }

    [Fact]
    public void ParseFirstOrgId_FlatArray_Uuid_ReturnsFirstId()
    {
        // claude.ai returns the org identifier as "uuid", not "id".
        var json = """[{"uuid":"11111111-2222-3333-4444-555555555555","name":"Personal"}]""";
        var id = ClaudeWebAdapter.ParseFirstOrgId(json);
        Assert.Equal("11111111-2222-3333-4444-555555555555", id);
    }

    [Fact]
    public void ParseFirstOrgId_DataArray_Uuid_ReturnsFirstId()
    {
        var id = ClaudeWebAdapter.ParseFirstOrgId("""{"data":[{"uuid":"org-uuid-1"}]}""");
        Assert.Equal("org-uuid-1", id);
    }

    [Fact]
    public void ParseOrgIds_ReturnsAll_InOrder()
    {
        var json = """[{"uuid":"a"},{"uuid":"b"},{"uuid":"c"}]""";
        Assert.Equal(new[] { "a", "b", "c" }, ClaudeWebAdapter.ParseOrgIds(json));
    }

    [Fact]
    public void ParseOrgIds_ChatCapableOrg_ComesFirst()
    {
        // The consumer (chat) org isn't first in the list — it must still be tried first.
        var json = """
            [
              { "uuid": "team-org", "capabilities": ["api"] },
              { "uuid": "personal-org", "capabilities": ["chat", "claude_pro"] }
            ]
            """;
        var ids = ClaudeWebAdapter.ParseOrgIds(json);
        Assert.Equal("personal-org", ids[0]);
        Assert.Equal("team-org", ids[1]);
    }

    [Fact]
    public void ParseOrgIds_NoCapabilities_PreservesOrder()
    {
        var json = """[{"uuid":"first"},{"uuid":"second"}]""";
        var ids = ClaudeWebAdapter.ParseOrgIds(json);
        Assert.Equal("first", ids[0]);
    }

    [Fact]
    public void ParseOrgIds_Empty_ReturnsEmpty()
    {
        Assert.Empty(ClaudeWebAdapter.ParseOrgIds("[]"));
        Assert.Empty(ClaudeWebAdapter.ParseOrgIds("not json"));
    }

    [Fact]
    public void ParseFirstOrgId_EmptyDataArray_ReturnsNull()
    {
        var id = ClaudeWebAdapter.ParseFirstOrgId("""{"data":[]}""");
        Assert.Null(id);
    }

    [Fact]
    public void ParseFirstOrgId_EmptyFlatArray_ReturnsNull()
    {
        var id = ClaudeWebAdapter.ParseFirstOrgId("[]");
        Assert.Null(id);
    }

    [Fact]
    public void ParseFirstOrgId_MalformedJson_ReturnsNull()
    {
        var id = ClaudeWebAdapter.ParseFirstOrgId("not json {{{");
        Assert.Null(id);
    }

    [Fact]
    public void ParseFirstOrgId_ObjectWithoutId_ReturnsNull()
    {
        var id = ClaudeWebAdapter.ParseFirstOrgId("""{"name":"My Org"}""");
        Assert.Null(id);
    }

    // ── ParseSnapshots — Shape C (session / weekly top-level keys) ────────────

    [Fact]
    public void ParseSnapshots_ShapeC_SessionKey_ParsedAsSession5h()
    {
        var json = """
            {
              "session": { "utilization": 0.4, "reset_at": "2030-01-01T12:00:00Z" }
            }
            """;
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(LimitWindow.Session5h, snaps[0].Window);
        Assert.Equal(0.4, snaps[0].Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshots_ShapeC_BothSessionAndWeekly()
    {
        var json = """
            {
              "session": { "utilization": 0.5, "reset_at": "2030-01-01T00:00:00Z" },
              "weekly":  { "utilization": 0.3, "reset_at": "2030-01-07T00:00:00Z" }
            }
            """;
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Equal(2, snaps.Count);
        Assert.Contains(snaps, s => s.Window == LimitWindow.Session5h);
        Assert.Contains(snaps, s => s.Window == LimitWindow.Weekly7d);
    }

    // ── ParseSnapshots — reset_at as Unix epoch ───────────────────────────────

    [Fact]
    public void ParseSnapshots_UnixEpochResetAt_ParsedCorrectly()
    {
        var epoch = new DateTimeOffset(2030, 6, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var json  = $$"""{"limits":[{"type":"session","utilization":0.5,"reset_at":{{epoch}}}]}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(2030, snaps[0].ResetsAt.Year);
    }

    // ── ParseSnapshots — type → LimitWindow mapping ───────────────────────────

    [Theory]
    [InlineData("weekly",   LimitWindow.Weekly7d)]
    [InlineData("weekly_7d", LimitWindow.Weekly7d)]
    [InlineData("week",     LimitWindow.Weekly7d)]
    [InlineData("monthly",  LimitWindow.Monthly)]
    [InlineData("session",  LimitWindow.Session5h)]
    [InlineData("unknown",  LimitWindow.Session5h)]
    public void ParseSnapshots_TypeMapping(string type, LimitWindow expected)
    {
        var json = $$"""{"limits":[{"type":"{{type}}","utilization":0.5,"reset_at":"2030-01-01T00:00:00Z"}]}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(expected, snaps[0].Window);
    }

    // ── ParseSnapshots — empty / missing limits ───────────────────────────────

    [Fact]
    public void ParseSnapshots_EmptyLimitsArray_ReturnsEmpty()
    {
        var snaps = ClaudeWebAdapter.ParseSnapshots("""{"limits":[]}""");
        Assert.Empty(snaps);
    }

    [Fact]
    public void ParseSnapshots_LimitMissingUtilization_Skipped()
    {
        var json = """{"limits":[{"type":"session","reset_at":"2030-01-01T00:00:00Z"}]}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Empty(snaps);
    }

    // ── ParseSnapshots — percent_full alias ───────────────────────────────────

    [Fact]
    public void ParseSnapshots_PercentFullAlias_ParsedAsUtilization()
    {
        var json = """{"message_limit":{"percent_full":0.55,"reset_at":"2030-01-01T00:00:00Z"}}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(0.55, snaps[0].Utilization, precision: 5);
    }

    // ── ParseSnapshots — resets_at / resetAt aliases ─────────────────────────

    [Theory]
    [InlineData("resets_at")]
    [InlineData("resetAt")]
    public void ParseSnapshots_ResetAtAlias(string key)
    {
        var json = $$"""{"limits":[{"type":"session","utilization":0.6,"{{key}}":"2030-06-01T00:00:00Z"}]}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(2030, snaps[0].ResetsAt.Year);
    }

    // ── ParseSnapshots — Shape D (five_hour / seven_day) ─────────────────────

    [Fact]
    public void ParseSnapshots_ShapeD_RealPayload()
    {
        var json = """
            {
              "five_hour":  { "utilization": 58.0, "resets_at": "2026-06-14T19:40:00.074981+00:00" },
              "seven_day":  { "utilization": 32.0, "resets_at": "2026-06-17T15:00:00.075003+00:00" },
              "seven_day_oauth_apps": null,
              "tangelo": null
            }
            """;
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Equal(2, snaps.Count);

        var session = snaps.First(s => s.Window == LimitWindow.Session5h);
        Assert.Equal(0.58, session.Utilization, precision: 5);
        Assert.Equal(2026, session.ResetsAt.Year);

        var weekly = snaps.First(s => s.Window == LimitWindow.Weekly7d);
        Assert.Equal(0.32, weekly.Utilization, precision: 5);
        Assert.Equal(2026, weekly.ResetsAt.Year);
    }

    [Fact]
    public void ParseSnapshots_ShapeD_OnlyFiveHour()
    {
        var json = """{"five_hour":{"utilization":100.0,"resets_at":"2030-01-01T00:00:00Z"}}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(LimitWindow.Session5h, snaps[0].Window);
        Assert.Equal(1.0, snaps[0].Utilization, precision: 5);
    }

    // ── ParseSnapshots — percentage normalisation (> 1.0 → ÷ 100) ───────────

    [Theory]
    [InlineData(0.58,  0.58)]   // already 0–1 fraction
    [InlineData(58.0,  0.58)]   // 0–100 percentage (≥ 2 → ÷ 100)
    [InlineData(100.0, 1.0)]    // 100 % → 1.0
    [InlineData(0.0,   0.0)]    // zero
    [InlineData(1.5,   1.0)]    // slightly over 1.0 → clamp (not treated as %)
    public void ParseSnapshots_UtilizationNormalization(double raw, double expected)
    {
        var u = raw.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var json = $$$"""{"five_hour":{"utilization":{{{u}}},"resets_at":"2030-01-01T00:00:00Z"}}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json);
        Assert.Single(snaps);
        Assert.Equal(expected, snaps[0].Utilization, precision: 5);
    }
}
