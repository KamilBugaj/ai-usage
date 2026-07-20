using System.Reflection;
using AiUsage.Core.Versioning;

namespace AiUsage.App.Infrastructure;

/// <summary>
/// The running build's version, read from the assembly rather than a hardcoded constant
/// so it always matches what CI stamped in (`-p:Version=` from the release tag).
///
/// Reads <see cref="AssemblyInformationalVersionAttribute"/>, not AssemblyVersion, because
/// only the informational one keeps a prerelease suffix ("0.1.0-rc1"); the other two are
/// four-part numerics that drop it. Formatting of the raw value lives in
/// <see cref="VersionText"/>, where it is unit tested.
/// </summary>
public static class AppVersion
{
    /// <summary>Version of the running build, e.g. "0.0.10" or "0.0.0-dev".</summary>
    public static string Current { get; } = VersionText.Normalize(Read());

    /// <summary>True for a local (non-CI) build, which carries the csproj baseline.</summary>
    public static bool IsDevBuild => VersionText.IsDevBuild(Current);

    // typeof(...).Assembly rather than GetEntryAssembly(): the latter can be null when
    // the app is hosted rather than launched directly.
    private static string? Read() => typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
