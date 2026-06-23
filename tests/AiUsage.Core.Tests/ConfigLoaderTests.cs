using AiUsage.Core.Config;

namespace AiUsage.Core.Tests;

public class ConfigLoaderTests
{
    // ── Load from disk ────────────────────────────────────────────────────────

    [Fact]
    public void Load_NonExistentFile_ReturnsEmptyConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");
        var cfg  = ConfigLoader.Load(path);
        Assert.NotNull(cfg);
        Assert.Null(cfg.ClaudeWeb);
        Assert.Null(cfg.Copilot);
    }

    [Fact]
    public void Load_EmptyJson_ReturnsEmptyConfig()
    {
        var path = WriteTempJson("{}");
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.Null(cfg.ClaudeWeb);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmptyConfig()
    {
        var path = WriteTempJson("not valid json {{{");
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.NotNull(cfg);
            Assert.Null(cfg.ClaudeWeb);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ClaudeWebSection_DeserializesCorrectly()
    {
        var path = WriteTempJson("""
            {
              "claudeWeb": {
                "sessionKey": "sk-ant-xyz",
                "cfClearance": "abc123",
                "organizationId": "org-456",
                "pollIntervalMinutes": 10
              }
            }
            """);
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.NotNull(cfg.ClaudeWeb);
            Assert.Equal("sk-ant-xyz", cfg.ClaudeWeb.SessionKey);
            Assert.Equal("abc123",     cfg.ClaudeWeb.CfClearance);
            Assert.Equal("org-456",    cfg.ClaudeWeb.OrganizationId);
            Assert.Equal(10,           cfg.ClaudeWeb.PollIntervalMinutes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ClaudeWeb_DefaultPollInterval_Is1()
    {
        var path = WriteTempJson("""{"claudeWeb":{"sessionKey":"sk-abc"}}""");
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.Equal(1, cfg.ClaudeWeb!.PollIntervalMinutes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_ChatGptWeb_DefaultPollInterval_Is1()
    {
        var path = WriteTempJson("""{"chatGptWeb":{"sessionToken":"tok-abc"}}""");
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.Equal(1, cfg.ChatGptWeb!.PollIntervalMinutes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_Copilot_DefaultPollInterval_Is1()
    {
        var path = WriteTempJson("""{"copilot":{}}""");
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.Equal(1, cfg.Copilot!.PollIntervalMinutes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_IsCaseInsensitive()
    {
        // JSON keys with different casing
        var path = WriteTempJson("""{"ClaudeWeb":{"SessionKey":"sk-upper"}}""");
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.Equal("sk-upper", cfg.ClaudeWeb?.SessionKey);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MultipleProviders_AllDeserialized()
    {
        var path = WriteTempJson("""
            {
              "claudeWeb":  { "sessionKey": "sk-abc" },
              "chatGptWeb": { "sessionToken": "tok-abc" },
              "copilot":    { "oAuthToken": "gho_xyz" }
            }
            """);
        try
        {
            var cfg = ConfigLoader.Load(path);
            Assert.Equal("sk-abc",  cfg.ClaudeWeb?.SessionKey);
            Assert.Equal("tok-abc", cfg.ChatGptWeb?.SessionToken);
            Assert.Equal("gho_xyz", cfg.Copilot?.OAuthToken);
        }
        finally { File.Delete(path); }
    }

    private static string WriteTempJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aiusage_cfg_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
