using AiUsage.Core.Versioning;

namespace AiUsage.Core.Tests;

public class VersionTextTests
{
    // The exact shape the SDK stamps on a CI build, captured from a real publish.
    private const string StampedWithSha = "0.0.11+cde12945c61d54c23c424fab44a8dc28caa625c9";

    [Fact]
    public void Normalize_StripsCommitShaMetadata()
    {
        Assert.Equal("0.0.11", VersionText.Normalize(StampedWithSha));
    }

    [Fact]
    public void Normalize_KeepsPrereleaseSuffix()
    {
        // A "-rc1" tag is part of the version; only the "+" metadata is build noise.
        Assert.Equal("0.1.0-rc1", VersionText.Normalize("0.1.0-rc1+abc123"));
    }

    [Fact]
    public void Normalize_PlainVersion_IsUnchanged()
    {
        Assert.Equal("0.0.11", VersionText.Normalize("0.0.11"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+abc123")] // metadata only, nothing left after stripping
    public void Normalize_NoUsableVersion_ReturnsUnknown(string? informational)
    {
        Assert.Equal(VersionText.Unknown, VersionText.Normalize(informational));
    }

    [Fact]
    public void Normalize_DevBaseline_SurvivesRoundTrip()
    {
        // A local build stamps the csproj baseline plus the sha; both halves must
        // survive for IsDevBuild to still recognise it.
        var normalized = VersionText.Normalize($"{VersionText.DevBuild}+cde1294");
        Assert.Equal(VersionText.DevBuild, normalized);
        Assert.True(VersionText.IsDevBuild(normalized));
    }

    [Theory]
    [InlineData("0.0.0-dev", true)]
    [InlineData("0.0.11", false)]
    [InlineData("0.1.0-rc1", false)]  // prerelease is a real release, not a dev build
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void IsDevBuild_DistinguishesLocalBuildsFromReleases(string? version, bool expected)
    {
        Assert.Equal(expected, VersionText.IsDevBuild(version));
    }

    [Fact]
    public void IsDevBuild_MatchesTheCsprojBaseline()
    {
        // Guards the duplication: the constant here mirrors <Version> in AiUsage.App.csproj.
        // If the baseline changes in one place only, this is the test that should fail.
        Assert.Equal("0.0.0-dev", VersionText.DevBuild);
    }
}
