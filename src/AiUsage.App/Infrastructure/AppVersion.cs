using System.Reflection;

namespace AiUsage.App.Infrastructure;

/// <summary>
/// The running build's version, read from the assembly rather than a hardcoded constant
/// so it always matches what CI stamped in (`-p:Version=` from the release tag).
///
/// Reads <see cref="AssemblyInformationalVersionAttribute"/>, not AssemblyVersion, because
/// only the informational one keeps a prerelease suffix ("0.1.0-rc1"); the other two are
/// four-part numerics that drop it.
/// </summary>
public static class AppVersion
{
    /// <summary>Version of the running build, e.g. "0.0.10" or "0.0.0-dev".</summary>
    public static string Current { get; } = Read();

    /// <summary>True for a local (non-CI) build, which carries the csproj baseline.</summary>
    public static bool IsDevBuild => Current.StartsWith("0.0.0-dev", StringComparison.Ordinal);

    private static string Read()
    {
        // typeof(...).Assembly rather than GetEntryAssembly(): the latter can be null when
        // the app is hosted rather than launched directly.
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational)) return "unknown";

        // SourceLink appends "+<commit sha>" to the informational version. It is build
        // metadata, not part of the version users should see.
        var plus = informational.IndexOf('+');
        return plus >= 0 ? informational[..plus] : informational;
    }
}
