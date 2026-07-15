using System.Threading;
using System.Threading.Tasks;
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
    public void ParseSnapshots_ShapeW_RealWhamUsage_RateLimitWindows()
    {
        // Authoritative shape from the Codex CLI backend-client: singular "rate_limit",
        // "*_window" keys, "limit_window_seconds", and an absolute epoch "reset_at".
        var reset = DateTimeOffset.UtcNow.AddHours(4).ToUnixTimeSeconds();
        var weeklyReset = DateTimeOffset.UtcNow.AddDays(3).ToUnixTimeSeconds();
        var json = $$"""
            {
              "plan_type": "pro",
              "rate_limit": {
                "primary_window":   { "used_percent": 15, "reset_at": {{reset}},       "limit_window_seconds": 18000 },
                "secondary_window": { "used_percent": 5,  "reset_at": {{weeklyReset}}, "limit_window_seconds": 604800 }
              }
            }
            """;
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);

        Assert.Equal(2, snaps.Count);

        // primary_window: 18000s = 300 min → session (5h) window.
        Assert.Equal(LimitWindow.Session5h, snaps[0].Window);
        Assert.Equal(0.15, snaps[0].Utilization, precision: 5);
        // reset_at is an absolute epoch, kept as-is (not now + relative).
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(reset), snaps[0].ResetsAt);

        // secondary_window: 604800s = 10080 min > 600 → weekly window.
        Assert.Equal(LimitWindow.Weekly7d, snaps[1].Window);
        Assert.Equal(0.05, snaps[1].Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshots_FreePlan_ThirtyDayWindow_IsMonthly()
    {
        // Verbatim shape from a live free account: one ~30d primary window, no secondary.
        // It must map to Monthly — classifying it as Weekly7d made the ChatGPT tile drop it
        // (its sink only accepted Session5h) and hang on "Loading…" forever.
        var json = """
            {
              "plan_type": "free",
              "rate_limit": {
                "allowed": true,
                "primary_window": {
                  "used_percent": 5,
                  "limit_window_seconds": 2592000,
                  "reset_after_seconds": 2592000,
                  "reset_at": 1786730882
                },
                "secondary_window": null
              }
            }
            """;
        var snaps = ChatGptWebAdapter.ParseSnapshots(json);

        Assert.Single(snaps);
        Assert.Equal(LimitWindow.Monthly, snaps[0].Window);
        Assert.Equal(0.05, snaps[0].Utilization, precision: 5);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786730882), snaps[0].ResetsAt);
    }

    [Fact]
    public void ParseMonthlyCredits_BusinessSeat_MapsToMonthlyUtilization()
    {
        // Verbatim body from a live Business seat's spend-controls endpoint — the numbers the
        // ChatGPT UI renders as "1635 of 7500 credits used, 78% remaining".
        var json = """
            {"effective_monthly_limit":{"limit":7500,"enforcement_mode":"HARD_CAP","limit_mode":"amount_credits"},
             "current_month_usage":1634.8825812339783}
            """;
        var now = new DateTimeOffset(2026, 7, 15, 18, 0, 0, TimeSpan.Zero);

        var snap = ChatGptWebAdapter.ParseMonthlyCredits(json, now);

        Assert.NotNull(snap);
        Assert.Equal(LimitWindow.Monthly, snap!.Window);
        Assert.Equal(0.218, snap.Utilization, precision: 3);
        // Renews at the start of the next calendar month.
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), snap.ResetsAt);
    }

    [Fact]
    public void ParseMonthlyCredits_NoLimitConfigured_ReturnsNull()
    {
        // Unlimited seat: nothing to plot, so the caller falls through to its own message.
        Assert.Null(ChatGptWebAdapter.ParseMonthlyCredits(
            """{"effective_monthly_limit":null,"current_month_usage":12.5}""", DateTimeOffset.UtcNow));
        Assert.Null(ChatGptWebAdapter.ParseMonthlyCredits(
            """{"effective_monthly_limit":{"limit":0},"current_month_usage":0}""", DateTimeOffset.UtcNow));
        Assert.Null(ChatGptWebAdapter.ParseMonthlyCredits("not json", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ParseAuthClaims_ExtractsAccountIdAndPlan_FromOpenAiAuthClaim()
    {
        var payload = """{"https://api.openai.com/auth":{"chatgpt_account_id":"acc-xyz","chatgpt_plan_type":"enterprise"}}""";
        var jwt = $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payload)}.sig";

        var (accountId, plan) = ChatGptWebAdapter.ParseAuthClaims(jwt);

        Assert.Equal("acc-xyz", accountId);
        Assert.Equal("enterprise", plan);
    }

    [Fact]
    public void ParseAuthClaims_RootLevelClaims_Fallback()
    {
        var payload = """{"chatgpt_account_id":"acc-root","chatgpt_plan_type":"plus"}""";
        var jwt = $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payload)}.sig";

        var (accountId, plan) = ChatGptWebAdapter.ParseAuthClaims(jwt);

        Assert.Equal("acc-root", accountId);
        Assert.Equal("plus", plan);
    }

    [Fact]
    public void ParseAuthClaims_MissingClaim_ReturnsNulls()
    {
        var jwt = $"{B64Url("{\"alg\":\"none\"}")}.{B64Url("{\"sub\":\"u1\"}")}.sig";

        var (accountId, plan) = ChatGptWebAdapter.ParseAuthClaims(jwt);

        Assert.Null(accountId);
        Assert.Null(plan);
    }

    [Fact]
    public void ParseAuthClaims_Malformed_ReturnsNulls()
    {
        var (accountId, plan) = ChatGptWebAdapter.ParseAuthClaims("not-a-jwt");
        Assert.Null(accountId);
        Assert.Null(plan);
    }

    // Base64url-encodes a UTF-8 string the way JWT segments are encoded.
    private static string B64Url(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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

    [Fact]
    public async Task PollOnceAsync_BusinessPlanNullUsage_ThrowsUsageUnavailable()
    {
        // Reproduces a live ChatGPT Business account: session yields a valid token whose
        // JWT plan is "business", and wham/usage answers 200 with every usage field null.
        var payload = """{"https://api.openai.com/auth":{"chatgpt_account_id":"acc","chatgpt_plan_type":"business"}}""";
        var jwt = $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payload)}.sig";
        var fetcher = new FakeFetcher(
            session: $$"""{"accessToken":"{{jwt}}"}""",
            usage: """{"plan_type":"business","rate_limit":null,"additional_rate_limits":null,"credits":{"has_credits":false}}""");
        var adapter = new ChatGptWebAdapter(fetcher);

        var ex = await Assert.ThrowsAsync<UsageUnavailableException>(
            () => adapter.PollOnceAsync(new NullSink(), CancellationToken.None));
        Assert.Equal("Business account: no usage data", ex.Message);
    }

    private sealed class FakeFetcher(string session, string usage) : IBrowserFetcher
    {
        public Task<string> FetchJsonAsync(
            string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
            => Task.FromResult(url.Contains("auth/session") ? session : usage);
    }

    private sealed class NullSink : IUsageSink
    {
        public Task EmitAsync(LimitSnapshot s) => Task.CompletedTask;
    }
}
