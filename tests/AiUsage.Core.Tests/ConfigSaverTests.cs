using AiUsage.Core.Config;

namespace AiUsage.Core.Tests;

public class ConfigSaverTests
{
    // ── UpdateClaudeWeb ───────────────────────────────────────────────────────

    [Fact]
    public void UpdateClaudeWeb_CreatesFile_WhenNotExisting()
    {
        var path = TempPath();
        try
        {
            ConfigSaver.UpdateClaudeWeb(path, "sk-new", cfClearance: null);
            Assert.True(File.Exists(path));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void UpdateClaudeWeb_WritesSessionKey()
    {
        var path = TempPath();
        try
        {
            ConfigSaver.UpdateClaudeWeb(path, "sk-test", cfClearance: "cf-val");
            var cfg = ConfigLoader.Load(path);
            Assert.Equal("sk-test", cfg.ClaudeWeb?.SessionKey);
            Assert.Equal("cf-val",  cfg.ClaudeWeb?.CfClearance);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void UpdateClaudeWeb_PreservesOrganizationId_FromExistingConfig()
    {
        var path = WriteTempJson("""
            {"claudeWeb":{"sessionKey":"old-sk","organizationId":"org-123","pollIntervalMinutes":7}}
            """);
        try
        {
            ConfigSaver.UpdateClaudeWeb(path, "new-sk", cfClearance: null);
            var cfg = ConfigLoader.Load(path);
            Assert.Equal("new-sk",  cfg.ClaudeWeb?.SessionKey);
            Assert.Equal("org-123", cfg.ClaudeWeb?.OrganizationId);
            // Poll interval normalises to the 1-minute default on (re)connect.
            Assert.Equal(1,         cfg.ClaudeWeb?.PollIntervalMinutes);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void UpdateClaudeWeb_PreservesOtherProviders()
    {
        var path = WriteTempJson("""
            {"copilot":{"oAuthToken":"gho_xyz"},"claudeWeb":{"sessionKey":"old"}}
            """);
        try
        {
            ConfigSaver.UpdateClaudeWeb(path, "new-sk", cfClearance: null);
            var cfg = ConfigLoader.Load(path);
            Assert.Equal("gho_xyz", cfg.Copilot?.OAuthToken);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void UpdateClaudeWeb_NullCfClearance_IsPreservedAsNull()
    {
        var path = TempPath();
        try
        {
            ConfigSaver.UpdateClaudeWeb(path, "sk-x", cfClearance: null);
            var cfg = ConfigLoader.Load(path);
            Assert.Null(cfg.ClaudeWeb?.CfClearance);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void UpdateClaudeWeb_DefaultPollInterval_Is1_WhenNoExistingConfig()
    {
        var path = TempPath();
        try
        {
            ConfigSaver.UpdateClaudeWeb(path, "sk-x", cfClearance: null);
            var cfg = ConfigLoader.Load(path);
            Assert.Equal(1, cfg.ClaudeWeb?.PollIntervalMinutes);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void UpdateClaudeWeb_CreatesDirectoryIfMissing()
    {
        var dir  = Path.Combine(Path.GetTempPath(), $"aiusage_test_{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "config.json");
        try
        {
            ConfigSaver.UpdateClaudeWeb(path, "sk-x", cfClearance: null);
            Assert.True(File.Exists(path));
        }
        finally
        {
            TryDelete(path);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ── Save (Ui.UltraCompact round-trip) ────────────────────────────────────

    [Fact]
    public void Save_UiUltraCompactTrue_RoundTrips()
    {
        var path = TempPath();
        try
        {
            var config = new AppConfig(Ui: new UiConfig(UltraCompact: true));
            ConfigSaver.Save(path, config);
            var loaded = ConfigLoader.Load(path);
            Assert.True(loaded.Ui?.UltraCompact);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Save_UiUltraCompactFalse_RoundTrips()
    {
        var path = TempPath();
        try
        {
            var config = new AppConfig(Ui: new UiConfig(UltraCompact: false));
            ConfigSaver.Save(path, config);
            var loaded = ConfigLoader.Load(path);
            Assert.False(loaded.Ui?.UltraCompact);
        }
        finally { TryDelete(path); }
    }

    // ── Save (Ui.AlwaysOnTop round-trip) ─────────────────────────────────────

    [Fact]
    public void Save_UiAlwaysOnTopTrue_RoundTrips()
    {
        var path = TempPath();
        try
        {
            var config = new AppConfig(Ui: new UiConfig(AlwaysOnTop: true));
            ConfigSaver.Save(path, config);
            var loaded = ConfigLoader.Load(path);
            Assert.True(loaded.Ui?.AlwaysOnTop);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Save_UiAlwaysOnTopFalse_RoundTrips()
    {
        var path = TempPath();
        try
        {
            var config = new AppConfig(Ui: new UiConfig(AlwaysOnTop: false));
            ConfigSaver.Save(path, config);
            var loaded = ConfigLoader.Load(path);
            Assert.False(loaded.Ui?.AlwaysOnTop);
        }
        finally { TryDelete(path); }
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"aiusage_cfg_{Guid.NewGuid():N}.json");

    private static string WriteTempJson(string json)
    {
        var path = TempPath();
        File.WriteAllText(path, json);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
