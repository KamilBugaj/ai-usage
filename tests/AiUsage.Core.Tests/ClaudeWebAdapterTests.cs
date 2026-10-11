using AiUsage.Core.Adapters.ClaudeWeb;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.Core.Tests;

/// <summary>
/// The fixtures below are real claude.ai responses captured on 2026-10-08, trimmed and
/// with identifiers replaced. They are the contract the adapter is written against:
/// when a payload changes, capture the new body here first, then adjust the parser.
/// </summary>
public class ClaudeWebAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 19, 0, 0, TimeSpan.Zero);

    private const string ChatOrg = "11111111-1111-1111-1111-111111111111";
    private const string ApiOrg = "22222222-2222-2222-2222-222222222222";

    // GET /api/organizations. Note "id" is a number; the uuid is what the other URLs take.
    private const string Organizations = $$"""
        [
          { "id": 1001, "uuid": "{{ChatOrg}}", "name": "Personal",
            "capabilities": ["claude_pro", "chat"] },
          { "id": 1002, "uuid": "{{ApiOrg}}", "name": "Individual Org",
            "capabilities": ["api", "api_individual", "customer_terms:standard"] }
        ]
        """;

    // GET /api/organizations/{org}/usage, from an account that Settings → Usage showed as
    // 5h 64 %, weekly 12 %, "Included credit: $100 of $100 left, expires November 5".
    private const string Usage = """
        {
          "five_hour": { "utilization": 64.0, "resets_at": "2026-10-08T22:10:00.074831+00:00", "limit_dollars": null, "used_dollars": null, "remaining_dollars": null, "locked_reason": null },
          "seven_day": { "utilization": 12.0, "resets_at": "2026-10-14T15:00:00.074856+00:00", "limit_dollars": null, "used_dollars": null, "remaining_dollars": null, "locked_reason": null },
          "seven_day_oauth_apps": null,
          "seven_day_opus": null,
          "seven_day_sonnet": null,
          "tangelo": null,
          "iguana_necktie": { "utilization": 0.0, "resets_at": "2026-11-05T07:59:00+00:00", "limit_dollars": 100, "used_dollars": 0.0, "remaining_dollars": 100.0, "locked_reason": null },
          "cedar_ember": null,
          "extra_usage": { "is_enabled": true, "monthly_limit": 3000, "used_credits": 0.0, "utilization": null, "currency": "USD", "decimal_places": 2 },
          "limits": [
            { "kind": "session", "group": "session", "percent": 64, "severity": "normal", "resets_at": "2026-10-08T22:10:00.074831+00:00", "scope": null, "is_active": true },
            { "kind": "weekly_all", "group": "weekly", "percent": 12, "severity": "normal", "resets_at": "2026-10-14T15:00:00.074856+00:00", "scope": null, "is_active": false }
          ],
          "spend": { "used": { "amount_minor": 0, "currency": "USD", "exponent": 2 }, "limit": { "amount_minor": 3000, "currency": "USD", "exponent": 2 }, "percent": 0, "enabled": true },
          "member_dashboard_available": false
        }
        """;

    // GET /api/organizations/{org}/prepaid/credits, shown by Settings → Usage as
    // "Usage credits $9.44".
    private const string PrepaidCredits = """
        {
          "amount": 944,
          "currency": "USD",
          "balance": { "money": null, "credits": { "amount_minor": 944, "exponent": 2 } },
          "balance_credits": 9,
          "amount_without_scoped_credits": 944,
          "auto_reload_settings": null,
          "pending_invoice_amount_cents": null,
          "tranches": [
            { "remaining_amount_minor_units": 443, "currency": "USD", "expires_at": null, "granted_amount_minor_units": 500, "program_id": "prepaid_additional_usage_individual" },
            { "remaining_amount_minor_units": 500, "currency": "USD", "expires_at": null, "granted_amount_minor_units": 500, "program_id": "prepaid_additional_usage_individual" }
          ],
          "promo_tranches": [],
          "next_expires_at": null
        }
        """;

    // ── ParseOrgIds ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseOrgIds_RealPayload_ReturnsUuids()
        => Assert.Equal(new[] { ChatOrg, ApiOrg }, ClaudeWebAdapter.ParseOrgIds(Organizations));

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
        Assert.Equal(new[] { "personal-org", "team-org" }, ClaudeWebAdapter.ParseOrgIds(json));
    }

    [Fact]
    public void ParseOrgIds_NoChatOrg_PreservesOrder()
    {
        var json = """[{"uuid":"a"},{"uuid":"b","capabilities":["api"]},{"uuid":"c"}]""";
        Assert.Equal(new[] { "a", "b", "c" }, ClaudeWebAdapter.ParseOrgIds(json));
    }

    [Fact]
    public void ParseOrgIds_EntryWithoutUuid_IsSkipped()
    {
        // "id" is the numeric database id, not something the usage URL accepts.
        var json = """[{"id":1001,"capabilities":["chat"]},{"uuid":"","capabilities":["chat"]},{"uuid":"ok"}]""";
        Assert.Equal(new[] { "ok" }, ClaudeWebAdapter.ParseOrgIds(json));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"data":[{"uuid":"wrapped"}]}""")] // only the flat array is a known shape
    [InlineData("not json")]
    public void ParseOrgIds_NotAnOrgArray_ReturnsEmpty(string json)
        => Assert.Empty(ClaudeWebAdapter.ParseOrgIds(json));

    // ── ParseSnapshots (/usage limit windows) ─────────────────────────────────

    [Fact]
    public void ParseSnapshots_RealPayload_ReadsBothWindows()
    {
        var snaps = ClaudeWebAdapter.ParseSnapshots(Usage, Now);

        // Exactly two: the dollar-denominated iguana_necktie block and the "limits"
        // array describe other things and must not surface as extra windows.
        Assert.Equal(2, snaps.Count);

        var session = snaps.Single(s => s.Window == LimitWindow.Session5h);
        Assert.Equal(Source.ClaudeWeb, session.Source);
        Assert.Equal(0.64, session.Utilization, precision: 5);
        Assert.Equal(DateTimeOffset.Parse("2026-10-08T22:10:00.074831+00:00"), session.ResetsAt);

        var weekly = snaps.Single(s => s.Window == LimitWindow.Weekly7d);
        Assert.Equal(0.12, weekly.Utilization, precision: 5);
        Assert.Equal(DateTimeOffset.Parse("2026-10-14T15:00:00.074856+00:00"), weekly.ResetsAt);
    }

    [Fact]
    public void ParseSnapshots_WindowIsNull_IsSkipped()
    {
        var json = """{"five_hour":{"utilization":100.0,"resets_at":"2030-01-01T00:00:00Z"},"seven_day":null}""";
        var snap = Assert.Single(ClaudeWebAdapter.ParseSnapshots(json, Now));
        Assert.Equal(LimitWindow.Session5h, snap.Window);
        Assert.Equal(1.0, snap.Utilization, precision: 5);
    }

    // utilization is always a percentage. An earlier value-based heuristic (÷100 only
    // when ≥ 2.0) read raw 1.0 ("1 %") as a full 1.0 fraction and showed 100 %.
    [Theory]
    [InlineData(58.0,  0.58)]
    [InlineData(100.0, 1.0)]
    [InlineData(2.0,   0.02)]
    [InlineData(1.9,   0.019)]  // regression: was 100 %
    [InlineData(1.0,   0.01)]   // regression: was 100 %
    [InlineData(0.5,   0.005)]  // regression: was 50 %
    [InlineData(0.0,   0.0)]
    [InlineData(150.0, 1.0)]    // over 100 % clamps
    public void ParseSnapshots_Utilization_IsAPercentage(double raw, double expected)
    {
        var u = raw.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var key in new[] { "five_hour", "seven_day" })
        {
            var json = $$$"""{"{{{key}}}":{"utilization":{{{u}}},"resets_at":"2030-01-01T00:00:00Z"}}""";
            var snap = Assert.Single(ClaudeWebAdapter.ParseSnapshots(json, Now));
            Assert.Equal(expected, snap.Utilization, precision: 5);
        }
    }

    [Fact]
    public void ParseSnapshots_NullUtilization_IsZeroNotMissing()
    {
        var json = """{"five_hour":{"utilization":null,"resets_at":"2030-01-01T00:00:00Z"}}""";
        Assert.Equal(0.0, Assert.Single(ClaudeWebAdapter.ParseSnapshots(json, Now)).Utilization);
    }

    [Fact]
    public void ParseSnapshots_NullResetsAt_ReportsAFullWindowFromNow()
    {
        var json = """{"five_hour":{"utilization":0.0,"resets_at":null},"seven_day":{"utilization":0.0,"resets_at":null}}""";
        var snaps = ClaudeWebAdapter.ParseSnapshots(json, Now);
        Assert.Equal(Now.AddHours(5), snaps.Single(s => s.Window == LimitWindow.Session5h).ResetsAt);
        Assert.Equal(Now.AddDays(7), snaps.Single(s => s.Window == LimitWindow.Weekly7d).ResetsAt);
    }

    [Theory]
    // Shapes the adapter used to accept on spec. None was ever observed; none is read.
    [InlineData("""{"limits":[{"type":"session","utilization":0.6,"reset_at":"2030-01-01T12:00:00Z"}]}""")]
    [InlineData("""{"message_limit":{"percent_full":0.75,"reset_at":"2030-06-01T08:00:00Z"}}""")]
    [InlineData("""{"session":{"utilization":0.4,"reset_at":"2030-01-01T12:00:00Z"}}""")]
    // The real "limits" array on its own: it mirrors the flat windows and is not a source.
    [InlineData("""{"limits":[{"kind":"session","group":"session","percent":64,"resets_at":"2030-01-01T12:00:00Z"}]}""")]
    [InlineData("""{"something":"else"}""")]
    [InlineData("""{"five_hour":"64%"}""")]
    [InlineData("[]")]
    [InlineData("not json at all")]
    public void ParseSnapshots_NoKnownWindow_ReturnsEmpty(string json)
        => Assert.Empty(ClaudeWebAdapter.ParseSnapshots(json, Now));

    // ── ParseIncludedCredit (iguana_necktie in /usage) ────────────────────────

    private static string Included(string block) => $$"""{"iguana_necktie":{{block}}}""";

    [Fact]
    public void ParseIncludedCredit_RealPayload_ReadsRemainingDollarsAndExpiry()
    {
        var credit = ClaudeWebAdapter.ParseIncludedCredit(Usage, Now);
        Assert.NotNull(credit);
        Assert.Equal(CreditKind.Included, credit.Kind);
        Assert.Equal(100.00m, credit.Amount); // whole dollars, NOT divided by 100
        Assert.Equal("USD", credit.Currency);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 7, 59, 0, TimeSpan.Zero), credit.ExpiresAt);
    }

    [Fact]
    public void ParseIncludedCredit_NoExpiry_StillReturnsBalance()
    {
        var credit = ClaudeWebAdapter.ParseIncludedCredit(
            Included("""{"remaining_dollars":25.0,"resets_at":null}"""), Now);
        Assert.Equal(25.0m, credit?.Amount);
        Assert.Null(credit?.ExpiresAt);
    }

    [Theory]
    [InlineData("null")]                                                                          // no grant (the usual case)
    [InlineData("""{"utilization":5.0,"resets_at":"2026-11-05T07:59:00+00:00"}""")]               // no dollar balance
    [InlineData("""{"limit_dollars":100,"used_dollars":40.0,"remaining_dollars":null}""")]        // balance is not derived
    [InlineData("""{"remaining_dollars":0.0,"resets_at":"2026-11-05T07:59:00+00:00"}""")]         // spent
    [InlineData("""{"remaining_dollars":100.0,"resets_at":"2026-10-01T00:00:00+00:00"}""")]       // expired
    [InlineData("""{"remaining_dollars":100.0,"resets_at":"2026-11-05T07:59:00+00:00","locked_reason":"plan_inactive"}""")] // locked
    public void ParseIncludedCredit_NothingUsable_ReturnsNull(string block)
        => Assert.Null(ClaudeWebAdapter.ParseIncludedCredit(Included(block), Now));

    [Theory]
    [InlineData("""{"five_hour":{"utilization":1.0}}""")] // key absent
    [InlineData("[]")]
    [InlineData("not json")]
    public void ParseIncludedCredit_NoBlock_ReturnsNull(string json)
        => Assert.Null(ClaudeWebAdapter.ParseIncludedCredit(json, Now));

    // ── ParseCredits (/prepaid/credits) ───────────────────────────────────────

    [Fact]
    public void ParseCredits_RealPayload_ConvertsMinorUnits()
    {
        var credits = ClaudeWebAdapter.ParseCredits(PrepaidCredits);
        Assert.NotNull(credits);
        Assert.Equal(Source.ClaudeWeb, credits.Source);
        Assert.Equal(CreditKind.Prepaid, credits.Kind);
        Assert.Equal(9.44m, credits.Amount); // 944 minor units
        Assert.Equal("USD", credits.Currency);
        Assert.Null(credits.ExpiresAt);
    }

    [Theory]
    [InlineData("0", 0.00)]
    [InlineData("1", 0.01)]
    [InlineData("1234", 12.34)]
    [InlineData("1234.0", 12.34)]
    public void ParseCredits_Amount_IsDividedBy100(string raw, double expected)
    {
        var credits = ClaudeWebAdapter.ParseCredits($$"""{"amount":{{raw}},"currency":"USD"}""");
        Assert.NotNull(credits);
        Assert.Equal((decimal)expected, credits.Amount);
    }

    [Fact]
    public void ParseCredits_Currency_IsTrimmedAndUpperCased()
    {
        var credits = ClaudeWebAdapter.ParseCredits("""{"amount":500,"currency":" eur "}""");
        Assert.Equal("EUR", credits?.Currency);
    }

    [Theory]
    [InlineData("""{"currency":"USD"}""")]                    // no amount
    [InlineData("""{"amount":null,"currency":"USD"}""")]      // null amount
    [InlineData("""{"amount":"100","currency":"USD"}""")]     // amount as string
    [InlineData("""{"amount":-5,"currency":"USD"}""")]        // negative
    [InlineData("""{"amount":100}""")]                        // no currency
    [InlineData("""{"amount":100,"currency":""}""")]          // empty currency
    [InlineData("""{"amount":100,"currency":null}""")]        // null currency
    [InlineData("""[{"amount":100,"currency":"USD"}]""")]     // not an object
    [InlineData("not json")]
    public void ParseCredits_Invalid_ReturnsNull(string json)
        => Assert.Null(ClaudeWebAdapter.ParseCredits(json));

    // ── PollOnceAsync — organisation discovery ────────────────────────────────

    private const string OrgsUrl = "https://claude.ai/api/organizations";
    private static string UsageUrl(string org) => $"https://claude.ai/api/organizations/{org}/usage";
    private static string CreditsUrl(string org) => $"https://claude.ai/api/organizations/{org}/prepaid/credits";

    // The fixture's included credit expires in 2026; push it out so the poll tests,
    // which run against the real clock, do not rot.
    private static readonly string LiveUsage = Usage.Replace("2026-11-05T07:59:00", "2099-11-05T07:59:00");

    private static ClaudeWebAdapter Adapter(FakeFetcher fetcher, bool fetchCredits = false, string? org = ChatOrg)
        => new(fetcher, new ClaudeWebConfig("sk", OrganizationId: org), fetchCredits);

    [Fact]
    public async Task PollOnceAsync_NoConfiguredOrg_DiscoversTheChatOrgAndRemembersIt()
    {
        var fetcher = new FakeFetcher { [OrgsUrl] = Organizations, [UsageUrl(ChatOrg)] = LiveUsage };
        var adapter = Adapter(fetcher, org: null);
        var sink = new RecordingSink();

        await adapter.PollOnceAsync(sink, CancellationToken.None);
        await adapter.PollOnceAsync(sink, CancellationToken.None);

        // Discovery once, then straight to the remembered org; the API org is never asked.
        Assert.Equal(new[] { OrgsUrl, UsageUrl(ChatOrg), UsageUrl(ChatOrg) }, fetcher.Requested);
        Assert.Equal(4, sink.Limits.Count);
    }

    [Fact]
    public async Task PollOnceAsync_Discovery_SkipsOrgsThatRejectOrHaveNoLimits()
    {
        var orgs = """[{"uuid":"rejects","capabilities":["chat"]},{"uuid":"empty","capabilities":["chat"]},{"uuid":"good"}]""";
        var fetcher = new FakeFetcher
        {
            [OrgsUrl] = orgs,
            // "rejects" has no route → HTTP 404
            [UsageUrl("empty")] = """{"five_hour":null,"seven_day":null}""",
            [UsageUrl("good")] = LiveUsage,
        };
        var sink = new RecordingSink();

        await Adapter(fetcher, org: null).PollOnceAsync(sink, CancellationToken.None);

        Assert.Equal(new[] { OrgsUrl, UsageUrl("rejects"), UsageUrl("empty"), UsageUrl("good") }, fetcher.Requested);
        Assert.Equal(2, sink.Limits.Count);
    }

    [Fact]
    public async Task PollOnceAsync_Discovery_NoOrgHasLimits_Throws()
    {
        var fetcher = new FakeFetcher { [OrgsUrl] = Organizations };
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Adapter(fetcher, org: null).PollOnceAsync(new RecordingSink(), CancellationToken.None));
    }

    [Fact]
    public async Task PollOnceAsync_KnownOrg_UsageFails_SurfacesTheErrorWithoutTryingOtherEndpoints()
    {
        var fetcher = new FakeFetcher(); // every request → HTTP 404

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Adapter(fetcher).PollOnceAsync(new RecordingSink(), CancellationToken.None));

        Assert.Equal(new[] { UsageUrl(ChatOrg) }, fetcher.Requested);
    }

    [Fact]
    public async Task PollOnceAsync_KnownOrg_ResponseHasNoWindows_ThrowsInsteadOfEmittingNothing()
    {
        // A silent empty poll would leave the tile on stale numbers with no explanation.
        var fetcher = new FakeFetcher { [UsageUrl(ChatOrg)] = """{"usage_v2":{"session":64}}""" };
        var sink = new RecordingSink();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Adapter(fetcher).PollOnceAsync(sink, CancellationToken.None));

        Assert.Contains("no usage limits", ex.Message);
        Assert.Empty(sink.Limits);
    }

    // ── PollOnceAsync — credits are opt-in and best-effort ────────────────────

    [Fact]
    public async Task PollOnceAsync_CreditsOff_NeverRequestsOrEmitsThem()
    {
        var fetcher = new FakeFetcher { [UsageUrl(ChatOrg)] = LiveUsage, [CreditsUrl(ChatOrg)] = PrepaidCredits };
        var sink = new RecordingSink();

        await Adapter(fetcher, fetchCredits: false).PollOnceAsync(sink, CancellationToken.None);

        Assert.Equal(new[] { UsageUrl(ChatOrg) }, fetcher.Requested);
        Assert.Equal(2, sink.Limits.Count);
        Assert.Empty(sink.Credits); // not even the included credit that rides in /usage
    }

    [Fact]
    public async Task PollOnceAsync_CreditsOn_EmitsPurchasedAndIncludedAfterLimits()
    {
        var fetcher = new FakeFetcher { [UsageUrl(ChatOrg)] = LiveUsage, [CreditsUrl(ChatOrg)] = PrepaidCredits };
        var sink = new RecordingSink();

        await Adapter(fetcher, fetchCredits: true).PollOnceAsync(sink, CancellationToken.None);

        Assert.Equal(2, sink.Limits.Count);
        var credits = Assert.Single(sink.Credits);
        Assert.Equal(9.44m, credits.Single(c => c.Kind == CreditKind.Prepaid).Amount);
        Assert.Equal(100.00m, credits.Single(c => c.Kind == CreditKind.Included).Amount);
    }

    [Fact]
    public async Task PollOnceAsync_CreditsOn_NoIncludedGrant_EmitsPurchasedOnly()
    {
        var usage = """{"five_hour":{"utilization":10.0,"resets_at":"2030-01-01T00:00:00Z"},"iguana_necktie":null}""";
        var fetcher = new FakeFetcher { [UsageUrl(ChatOrg)] = usage, [CreditsUrl(ChatOrg)] = PrepaidCredits };
        var sink = new RecordingSink();

        await Adapter(fetcher, fetchCredits: true).PollOnceAsync(sink, CancellationToken.None);

        Assert.Equal(CreditKind.Prepaid, Assert.Single(Assert.Single(sink.Credits)).Kind);
    }

    [Fact]
    public async Task PollOnceAsync_CreditsRequestFails_LimitsAndIncludedCreditStillEmitted()
    {
        // No credits route → the fake answers "HTTP 404", like an account without usage credits.
        var fetcher = new FakeFetcher { [UsageUrl(ChatOrg)] = LiveUsage };
        var sink = new RecordingSink();

        await Adapter(fetcher, fetchCredits: true).PollOnceAsync(sink, CancellationToken.None);

        Assert.Equal(2, sink.Limits.Count);
        // Emitted without a Prepaid entry: that is what tells the tile to hide the row.
        Assert.Equal(CreditKind.Included, Assert.Single(Assert.Single(sink.Credits)).Kind);
    }

    [Fact]
    public async Task PollOnceAsync_CreditsUnparseable_EmitsWithoutPurchasedBalance()
    {
        var usage = """{"five_hour":{"utilization":10.0,"resets_at":"2030-01-01T00:00:00Z"}}""";
        var fetcher = new FakeFetcher { [UsageUrl(ChatOrg)] = usage, [CreditsUrl(ChatOrg)] = """{"unexpected":true}""" };
        var sink = new RecordingSink();

        await Adapter(fetcher, fetchCredits: true).PollOnceAsync(sink, CancellationToken.None);

        Assert.Empty(Assert.Single(sink.Credits));
    }

    private sealed class FakeFetcher : Dictionary<string, string>, IBrowserFetcher
    {
        public List<string> Requested { get; } = [];

        public Task<string> FetchJsonAsync(
            string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null)
        {
            Requested.Add(url);
            return TryGetValue(url, out var body)
                ? Task.FromResult(body)
                : Task.FromException<string>(new HttpRequestException("HTTP 404"));
        }
    }

    private sealed class RecordingSink : IUsageSink
    {
        public List<LimitSnapshot> Limits { get; } = [];
        public List<IReadOnlyList<CreditSnapshot>> Credits { get; } = [];

        public Task EmitAsync(LimitSnapshot s) { Limits.Add(s); return Task.CompletedTask; }
        public Task EmitCreditsAsync(IReadOnlyList<CreditSnapshot> credits) { Credits.Add(credits); return Task.CompletedTask; }
    }
}
