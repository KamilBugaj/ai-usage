using System.Text.Json;

namespace AiUsage.Core.Config;

public static class ConfigSaver
{
    /// <summary>Writes the whole config to disk, creating the directory if needed.</summary>
    public static void Save(string path, AppConfig config)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig));
    }

    // The connect flows below omit PollIntervalMinutes so the record default (1 minute)
    // applies — there is no UI for it, so any persisted value is a stale default and is
    // normalised on (re)connect rather than preserved.

    /// <summary>Stores (or clears, when null) the GitHub Copilot OAuth token; PollIntervalMinutes is normalised to the default.</summary>
    public static void UpdateCopilotToken(string path, string? oAuthToken)
    {
        var existing = ConfigLoader.Load(path);
        Save(path, existing with
        {
            Copilot = new CopilotConfig(OAuthToken: oAuthToken)
        });
    }

    public static void UpdateChatGptWeb(string path, string sessionToken)
    {
        var existing = ConfigLoader.Load(path);
        Save(path, existing with
        {
            ChatGptWeb = new ChatGptWebConfig(SessionToken: sessionToken)
        });
    }

    public static void UpdateClaudeWeb(
        string path, string sessionKey, string? cfClearance, string? cookieString = null)
    {
        var existing = ConfigLoader.Load(path);
        var prev = existing.ClaudeWeb;
        Save(path, existing with
        {
            ClaudeWeb = new ClaudeWebConfig(
                SessionKey: sessionKey,
                CfClearance: cfClearance,
                OrganizationId: prev?.OrganizationId,
                CookieString: cookieString)
        });
    }
}
