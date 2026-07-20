using System.Text.Json.Serialization;

namespace AiUsage.App.Features;

// Trim-safe JsonTypeInfo for the two values WebViewSession injects into the page: the
// request URL (a string, JSON-encoded into a safe JS literal) and the request headers
// (Dictionary&lt;string,string&gt;). Source-gen keeps these working under PublishTrimmed.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class WebViewJsonContext : JsonSerializerContext;
