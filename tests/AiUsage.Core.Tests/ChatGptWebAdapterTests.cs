using AiUsage.Core.Adapters.ChatGptWeb;
using AiUsage.Core.Models;

namespace AiUsage.Core.Tests;

/// <summary>
/// The fixtures are the contract the adapter is written against. FreeUsage, BusinessUsage
/// and MonthlyCredits are bodies captured from live accounts (July 2026); PlusUsage is the
/// two-window form the open-source Codex CLI reads from the same endpoint. When a payload
/// changes, capture the new body here first, then adjust the parser.
/// </summary>
public class ChatGptWebAdapterTests
{
    // GET /backend-api/wham/usage — live free account: one ~30d window, no secondary.
    private const string FreeUsage = """
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

    // GET /backend-api/wham/usage — Plus/Pro: a ~5h primary and a ~7d secondary.
    private const string PlusUsage = """
        {
          "plan_type": "pro",
          "rate_limit": {
            "primary_window":   { "used_percent": 15, "reset_at": 1893456000, "limit_window_seconds": 18000 },
            "secondary_window": { "used_percent": 5,  "reset_at": 1893974400, "limit_window_seconds": 604800 }
          }
        }
        """;

    // GET /backend-api/wham/usage — live Business seat: 200, but every usage field null.
    private const string BusinessUsage =
        """{"plan_type":"business","rate_limit":null,"additional_rate_limits":null,"credits":{"has_credits":false}}""";

    // GET …/spend-controls/current-user/monthly-usage — live Business seat, the numbers the
    // ChatGPT UI renders as "1635 of 7500 credits used, 78% remaining".
    private const string MonthlyCredits = """
        {"effective_monthly_limit":{"limit":7500,"enforcement_mode":"HARD_CAP","limit_mode":"amount_credits"},
         "current_month_usage":1634.8825812339783}
        """;

    // ── ParseAccessToken (/api/auth/session) ──────────────────────────────────

    [Fact]
    public void ParseAccessToken_ExtractsToken()
    {
        var json = """{"accessToken":"eyJhbGci.eyJzdWIi.sig","user":{"id":"user-123"}}""";
        Assert.Equal("eyJhbGci.eyJzdWIi.sig", ChatGptWebAdapter.ParseAccessToken(json));
    }

    [Theory]
    [InlineData("{}")]                              // signed out
    [InlineData("""{"user":{"id":"user-123"}}""")]
    [InlineData("""{"accessToken":null}""")]
    [InlineData("""{"accessToken":""}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ParseAccessToken_NoToken_ReturnsNull(string json)
        => Assert.Null(ChatGptWebAdapter.ParseAccessToken(json));

    // ── ParseAuthClaims (access-token JWT) ────────────────────────────────────

    [Fact]
    public void ParseAuthClaims_ExtractsAccountIdAndPlan_FromOpenAiAuthClaim()
    {
        var (accountId, plan) = ChatGptWebAdapter.ParseAuthClaims(Jwt("acc-xyz", "enterprise"));
        Assert.Equal("acc-xyz", accountId);
        Assert.Equal("enterprise", plan);
    }

    [Theory]
    [InlineData("""{"sub":"u1"}""")]
    // Only the namespaced claim is a known location for these.
    [InlineData("""{"chatgpt_account_id":"acc-root","chatgpt_plan_type":"plus"}""")]
    [InlineData("""{"https://api.openai.com/auth":"nope"}""")]
    public void ParseAuthClaims_NoAuthClaim_ReturnsNulls(string payload)
    {
        var jwt = $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payload)}.sig";
        Assert.Equal((null, null), ChatGptWebAdapter.ParseAuthClaims(jwt));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.!!!not-base64!!!.c")]
    [InlineData("a.bm90IGpzb24.c")] // base64 of "not json"
    public void ParseAuthClaims_Malformed_ReturnsNulls(string token)
        => Assert.Equal((null, null), ChatGptWebAdapter.ParseAuthClaims(token));

    // ── ParseSnapshots (/wham/usage) ──────────────────────────────────────────

    [Fact]
    public void ParseSnapshots_FreeAccount_ThirtyDayWindow_IsMonthly()
    {
        // It must map to Monthly: classifying it as Weekly7d once made the tile drop it
        // (its sink did not accept that window) and hang on "Loading…" forever.
        var snap = Assert.Single(ChatGptWebAdapter.ParseSnapshots(FreeUsage));

        Assert.Equal(Source.ChatGptWeb, snap.Source);
        Assert.Equal(LimitWindow.Monthly, snap.Window);
        Assert.Equal(0.05, snap.Utilization, precision: 5);
        // reset_at, not now + reset_after_seconds: the latter mirrors the window length.
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786730882), snap.ResetsAt);
    }

    [Fact]
    public void ParseSnapshots_PlusAccount_SessionAndWeeklyWindows()
    {
        var snaps = ChatGptWebAdapter.ParseSnapshots(PlusUsage);

        Assert.Equal(2, snaps.Count);
        Assert.Equal(LimitWindow.Session5h, snaps[0].Window);
        Assert.Equal(0.15, snaps[0].Utilization, precision: 5);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), snaps[0].ResetsAt);
        Assert.Equal(LimitWindow.Weekly7d, snaps[1].Window);
        Assert.Equal(0.05, snaps[1].Utilization, precision: 5);
    }

    [Fact]
    public void ParseSnapshots_BusinessSeat_HasNoWindow()
        => Assert.Empty(ChatGptWebAdapter.ParseSnapshots(BusinessUsage));

    [Theory]
    [InlineData(18000,   LimitWindow.Session5h)]  // 5h
    [InlineData(36000,   LimitWindow.Session5h)]  // 10h: still a session window
    [InlineData(36001,   LimitWindow.Weekly7d)]
    [InlineData(604800,  LimitWindow.Weekly7d)]   // 7d
    [InlineData(1209600, LimitWindow.Weekly7d)]   // 14d: still weekly
    [InlineData(1209601, LimitWindow.Monthly)]
    [InlineData(2592000, LimitWindow.Monthly)]    // 30d
    public void ParseSnapshots_WindowLength_DecidesTheLane(long seconds, LimitWindow expected)
    {
        var json = PrimaryWindow($"\"used_percent\":1,\"limit_window_seconds\":{seconds},\"reset_at\":1893456000");
        Assert.Equal(expected, Assert.Single(ChatGptWebAdapter.ParseSnapshots(json)).Window);
    }

    [Theory]
    [InlineData(130, 1.0)]
    [InlineData(-5,  0.0)]
    public void ParseSnapshots_Utilization_IsClamped(double usedPercent, double expected)
    {
        var json = PrimaryWindow($"\"used_percent\":{usedPercent},\"limit_window_seconds\":18000,\"reset_at\":1893456000");
        Assert.Equal(expected, Assert.Single(ChatGptWebAdapter.ParseSnapshots(json)).Utilization);
    }

    [Theory]
    // A window missing any of its three fields is not a window.
    [InlineData("""{"rate_limit":{"primary_window":{"limit_window_seconds":18000,"reset_at":1893456000}}}""")]
    [InlineData("""{"rate_limit":{"primary_window":{"used_percent":10,"reset_at":1893456000}}}""")]
    [InlineData("""{"rate_limit":{"primary_window":{"used_percent":10,"limit_window_seconds":18000,"reset_after_seconds":3600}}}""")]
    // Shapes the adapter used to accept on spec. None was ever observed; none is read.
    [InlineData("""{"rate_limits":{"primary":{"used_percent":12.5,"window_minutes":299,"resets_in_seconds":3600}}}""")]
    [InlineData("""{"primary":{"used_percent":80,"window_minutes":300}}""")]
    [InlineData("""{"models":{"gpt-4o":{"limit":80,"remaining":32,"reset_at":"2030-01-01T12:00:00Z"}}}""")]
    [InlineData("""[{"limit":40,"remaining":10,"reset_at":"2030-01-01T12:00:00Z"}]""")]
    [InlineData("""{"rate_limits":[{"limit":40,"remaining":10}]}""")]
    [InlineData("""{"something":"else"}""")]
    [InlineData("not json")]
    public void ParseSnapshots_NoKnownWindow_ReturnsEmpty(string json)
        => Assert.Empty(ChatGptWebAdapter.ParseSnapshots(json));

    // ── ParseMonthlyCredits (spend-controls) ──────────────────────────────────

    [Fact]
    public void ParseMonthlyCredits_BusinessSeat_MapsToMonthlyUtilization()
    {
        var now = new DateTimeOffset(2026, 7, 15, 18, 0, 0, TimeSpan.Zero);

        var snap = ChatGptWebAdapter.ParseMonthlyCredits(MonthlyCredits, now);

        Assert.NotNull(snap);
        Assert.Equal(LimitWindow.Monthly, snap.Window);
        Assert.Equal(0.218, snap.Utilization, precision: 3);
        // Renews at the start of the next calendar month.
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), snap.ResetsAt);
    }

    [Fact]
    public void ParseMonthlyCredits_December_RollsIntoNextYear()
    {
        var snap = ChatGptWebAdapter.ParseMonthlyCredits(
            MonthlyCredits, new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), snap!.ResetsAt);
    }

    [Theory]
    [InlineData("""{"effective_monthly_limit":null,"current_month_usage":12.5}""")] // unlimited seat
    [InlineData("""{"effective_monthly_limit":{"limit":0},"current_month_usage":0}""")]
    [InlineData("""{"effective_monthly_limit":{"limit":7500}}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ParseMonthlyCredits_NoCap_ReturnsNull(string json)
        => Assert.Null(ChatGptWebAdapter.ParseMonthlyCredits(json, DateTimeOffset.UtcNow));

    // ── PollOnceAsync ─────────────────────────────────────────────────────────

    private const string SessionUrl = "https://chatgpt.com/api/auth/session";
    private const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";
    private static string CreditsUrl(string account)
        => $"https://chatgpt.com/backend-api/accounts/{account}/spend-controls/current-user/monthly-usage";

    private static string Session(string? account, string? plan) => $$"""{"accessToken":"{{Jwt(account, plan)}}"}""";

    [Fact]
    public async Task PollOnceAsync_FreeAccount_EmitsItsWindow_ScopedToTheAccount()
    {
        var fetcher = new FakeFetcher { [SessionUrl] = Session("acc", "free"), [UsageUrl] = FreeUsage };
        var sink = new RecordingSink();

        await new ChatGptWebAdapter(fetcher).PollOnceAsync(sink, CancellationToken.None);

        Assert.Equal(LimitWindow.Monthly, Assert.Single(sink.Limits).Window);
        // One usage endpoint, nothing else: no credits lookup once a window is found.
        Assert.Equal(new[] { SessionUrl, UsageUrl }, fetcher.Requested);
        var headers = fetcher.Headers[UsageUrl]!;
        Assert.Equal($"Bearer {Jwt("acc", "free")}", headers["Authorization"]);
        Assert.Equal("acc", headers["ChatGPT-Account-Id"]);
    }

    [Fact]
    public async Task PollOnceAsync_BusinessSeat_FallsBackToMonthlyCredits()
    {
        var fetcher = new FakeFetcher
        {
            [SessionUrl] = Session("acc", "business"),
            [UsageUrl] = BusinessUsage,
            [CreditsUrl("acc")] = MonthlyCredits,
        };
        var sink = new RecordingSink();

        await new ChatGptWebAdapter(fetcher).PollOnceAsync(sink, CancellationToken.None);

        var snap = Assert.Single(sink.Limits);
        Assert.Equal(LimitWindow.Monthly, snap.Window);
        Assert.Equal(0.218, snap.Utilization, precision: 3);
        Assert.Equal("acc", fetcher.Headers[CreditsUrl("acc")]!["ChatGPT-Account-Id"]);
    }

    [Fact]
    public async Task PollOnceAsync_PaidSeatWithNoWindowAndNoCap_IsUnavailableNotAnError()
    {
        // No credits route → HTTP 404, i.e. no spend controls on this workspace.
        var fetcher = new FakeFetcher { [SessionUrl] = Session("acc", "business"), [UsageUrl] = BusinessUsage };

        var ex = await Assert.ThrowsAsync<UsageUnavailableException>(
            () => new ChatGptWebAdapter(fetcher).PollOnceAsync(new RecordingSink(), CancellationToken.None));

        Assert.Equal("Business account: no usage data", ex.Message);
    }

    [Theory]
    [InlineData("free", "ChatGPT returned no usage data for this account. (plan: free)")]
    [InlineData(null, "ChatGPT returned no usage data for this account.")]
    public async Task PollOnceAsync_NoDataOnAFreeOrUnknownPlan_IsAnError(string? plan, string expected)
    {
        var fetcher = new FakeFetcher { [SessionUrl] = Session("acc", plan), [UsageUrl] = BusinessUsage };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ChatGptWebAdapter(fetcher).PollOnceAsync(new RecordingSink(), CancellationToken.None));

        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public async Task PollOnceAsync_NoAccountClaim_SendsNoAccountHeaderAndSkipsCredits()
    {
        var fetcher = new FakeFetcher { [SessionUrl] = Session(null, "plus"), [UsageUrl] = BusinessUsage };

        await Assert.ThrowsAsync<UsageUnavailableException>(
            () => new ChatGptWebAdapter(fetcher).PollOnceAsync(new RecordingSink(), CancellationToken.None));

        Assert.Equal(new[] { SessionUrl, UsageUrl }, fetcher.Requested);
        Assert.False(fetcher.Headers[UsageUrl]!.ContainsKey("ChatGPT-Account-Id"));
    }

    [Fact]
    public async Task PollOnceAsync_UsageRequestFails_SurfacesTheErrorWithoutTryingOtherEndpoints()
    {
        var fetcher = new FakeFetcher { [SessionUrl] = Session("acc", "plus") }; // usage → HTTP 404

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => new ChatGptWebAdapter(fetcher).PollOnceAsync(new RecordingSink(), CancellationToken.None));

        Assert.Equal("ChatGPT usage request failed: HTTP 404", ex.Message);
        Assert.Equal(new[] { SessionUrl, UsageUrl }, fetcher.Requested);
    }

    [Fact]
    public async Task PollOnceAsync_SignedOutSession_AsksToReconnect()
    {
        var fetcher = new FakeFetcher { [SessionUrl] = "{}" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ChatGptWebAdapter(fetcher).PollOnceAsync(new RecordingSink(), CancellationToken.None));

        Assert.Contains("Reconnect ChatGPT", ex.Message);
        Assert.Equal(new[] { SessionUrl }, fetcher.Requested);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string PrimaryWindow(string fields)
        => "{\"rate_limit\":{\"primary_window\":{" + fields + "}}}";

    // An unsigned access token carrying the OpenAI auth claim; null fields are omitted.
    private static string Jwt(string? account, string? plan)
    {
        var fields = new List<string>();
        if (account is not null) fields.Add($"\"chatgpt_account_id\":\"{account}\"");
        if (plan is not null) fields.Add($"\"chatgpt_plan_type\":\"{plan}\"");
        var payload = "{\"https://api.openai.com/auth\":{" + string.Join(",", fields) + "}}";
        return $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payload)}.sig";
    }

    // Base64url-encodes a UTF-8 string the way JWT segments are encoded.
    private static string B64Url(string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed class FakeFetcher : Dictionary<string, string>, IBrowserFetcher
    {
        public List<string> Requested { get; } = [];
        public Dictionary<string, IReadOnlyDictionary<string, string>?> Headers { get; } = [];

        public Task<string> FetchJsonAsync(
            string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
        {
            Requested.Add(url);
            Headers[url] = headers;
            return TryGetValue(url, out var body)
                ? Task.FromResult(body)
                : Task.FromException<string>(new HttpRequestException("HTTP 404"));
        }
    }

    private sealed class RecordingSink : IUsageSink
    {
        public List<LimitSnapshot> Limits { get; } = [];
        public Task EmitAsync(LimitSnapshot s) { Limits.Add(s); return Task.CompletedTask; }
    }
}
