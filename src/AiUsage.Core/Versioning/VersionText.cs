namespace AiUsage.Core.Versioning;

/// <summary>
/// Turns a raw <c>AssemblyInformationalVersion</c> into the string shown to users.
/// Pure string handling, kept out of the UI project so it can be unit tested.
/// </summary>
public static class VersionText
{
    /// <summary>
    /// Version of a build that CI did not stamp. Must match the <c>&lt;Version&gt;</c>
    /// baseline in <c>AiUsage.App.csproj</c>; the release workflow overrides that
    /// baseline with the git tag, so a shipped build never carries this value.
    /// </summary>
    public const string DevBuild = "0.0.0-dev";

    /// <summary>Returned when the assembly carries no usable version.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// Strips the build metadata the SDK appends ("0.0.11+&lt;commit sha&gt;") while keeping
    /// any prerelease suffix ("0.1.0-rc1"), which is part of the version proper.
    /// Returns <see cref="Unknown"/> for a missing or metadata-only input.
    /// </summary>
    public static string Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return Unknown;

        var plus = informationalVersion.IndexOf('+');
        var version = (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();

        return version.Length == 0 ? Unknown : version;
    }

    /// <summary>True for a local build carrying the csproj baseline rather than a tag.</summary>
    public static bool IsDevBuild(string? version) =>
        version is not null && version.StartsWith(DevBuild, StringComparison.Ordinal);
}
