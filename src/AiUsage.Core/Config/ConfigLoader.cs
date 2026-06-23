using System.Text.Json;

namespace AiUsage.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, _opts) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }
}
