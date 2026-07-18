using System.Text.Json.Serialization;

namespace AiUsage.Core.Config;

// Source-generated JsonTypeInfo for the whole config graph. Replaces the reflection-based
// serializer so PublishTrimmed cannot strip AppConfig's metadata — if it did, Deserialize
// would throw, ConfigLoader's catch-all would swallow it, and the user would silently lose
// every saved token. Options are baked in to match the previous reflection settings exactly:
// camelCase names, indented output, omit nulls on write, case-insensitive on read.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext;
