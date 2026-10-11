using AiUsage.Application.Tiles;

namespace AiUsage.Application.Tests;

public class TileMappingTests
{
    // ── Percent ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0,   0)]
    [InlineData(0.5,   50)]
    [InlineData(0.589, 59)]   // rounds (0.589 → 58.9 → 59)
    [InlineData(1.0,   100)]
    public void Percent_RoundsToWholePercent(double util, int expected)
        => Assert.Equal(expected, TileMapping.Percent(util));

    // ── AlertActive ───────────────────────────────────────────────────────────

    [Fact]
    public void AlertActive_NullThreshold_IsFalse()
        => Assert.False(TileMapping.AlertActive(0.99, null));

    [Theory]
    [InlineData(0.80, 80, true)]   // at threshold
    [InlineData(0.81, 80, true)]   // over
    [InlineData(0.79, 80, false)]  // under
    public void AlertActive_ComparesAgainstThreshold(double util, double threshold, bool expected)
        => Assert.Equal(expected, TileMapping.AlertActive(util, threshold));

    // ── Credits ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(12.34,   "USD", "$12.34")]
    [InlineData(0,       "USD", "$0.00")]
    [InlineData(100,     "USD", "$100.00")]
    [InlineData(1234.5,  "USD", "$1,234.50")]
    [InlineData(12.34,   "EUR", "12.34 EUR")]
    public void Credits_FormatsAmountWithCurrency(double amount, string currency, string expected)
        => Assert.Equal(expected, TileMapping.Credits((decimal)amount, currency));

    [Fact]
    public void Credits_IgnoresCurrentCulture()
    {
        // pl-PL would otherwise render "12,34" — the tile must not depend on the OS locale.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pl-PL");
        try { Assert.Equal("$12.34", TileMapping.Credits(12.34m, "USD")); }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    // ── ResetsIn ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ResetsIn_PastWindow_IsSoon()
        => Assert.Equal("soon", TileMapping.ResetsIn(Now - TimeSpan.FromMinutes(1), Now, ResetFormat.Full));

    [Theory]
    [InlineData(1, 90, "in 1d 1h")]    // 1d 1h 30m → days + hours component
    [InlineData(0, 150, "in 2h 30m")]  // 2.5h → hours + minutes
    [InlineData(0, 45, "in 45m")]      // < 1h → minutes
    public void ResetsIn_Full(int days, int minutes, string expected)
    {
        var at = Now + TimeSpan.FromDays(days) + TimeSpan.FromMinutes(minutes);
        Assert.Equal(expected, TileMapping.ResetsIn(at, Now, ResetFormat.Full));
    }

    [Theory]
    [InlineData(150, "in 2h 30m")]   // hours + minutes
    [InlineData(45,  "in 45m")]      // minutes only — no day component even for big spans
    [InlineData(1500, "in 25h 0m")]  // 25h stays in hours (no day rollover)
    public void ResetsIn_HoursMinutes(int minutes, string expected)
    {
        var at = Now + TimeSpan.FromMinutes(minutes);
        Assert.Equal(expected, TileMapping.ResetsIn(at, Now, ResetFormat.HoursMinutes));
    }

    [Theory]
    [InlineData(2, 0, "in 2d")]   // days
    [InlineData(0, 5, "in 5h")]   // hours
    public void ResetsIn_Coarse(int days, int hours, string expected)
    {
        var at = Now + TimeSpan.FromDays(days) + TimeSpan.FromHours(hours);
        Assert.Equal(expected, TileMapping.ResetsIn(at, Now, ResetFormat.Coarse));
    }
}
