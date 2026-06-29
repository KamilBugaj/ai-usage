using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AiUsage.App.Infrastructure;

/// <summary>
/// Login autostart via the per-user registry Run key
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). The value name matches the
/// installer's registry entry, so the in-app toggle and the installer checkbox manage
/// the same entry and stay in sync. No launch flag is needed — the app starts hidden
/// in the tray on every run.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Must equal the installer's [Registry] ValueName ("KB.AI.Usage") to avoid duplicates.
    private const string ValueName = "KB.AI.Usage";

    public bool IsSupported => true;

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public void SetEnabled(bool enabled)
    {
        // Throw (don't silently no-op) on failure so the caller's toggle reverts
        // instead of showing "on" while nothing was written to the registry.
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException($@"Cannot open HKCU\{RunKeyPath}.");

        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                throw new InvalidOperationException("Cannot determine the executable path for autostart.");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
