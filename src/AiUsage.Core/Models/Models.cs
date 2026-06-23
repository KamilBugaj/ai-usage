namespace AiUsage.Core.Models;

public enum Source
{
    ClaudeWeb,
    ChatGptWeb,
    Copilot
}

public enum LimitWindow { Session5h, Weekly7d, Monthly }

public record LimitSnapshot(
    Source Source,
    LimitWindow Window,
    double Utilization,
    DateTimeOffset ResetsAt);
