namespace AiUsage.Core.Models;

/// <summary>
/// Thrown by an adapter when the session is valid but the provider exposes no usage data
/// to show — e.g. a ChatGPT Business/Enterprise plan, whose seat usage has no self-serve
/// API. This is an expected state, not a failure: the poll loop renders it as a muted
/// status line rather than a red error, and keeps polling on the normal interval.
/// </summary>
public sealed class UsageUnavailableException(string message) : Exception(message);
