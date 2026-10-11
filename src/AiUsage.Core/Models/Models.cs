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

// Prepaid:  credits the user bought (top-ups); no expiry.
// Included: a credit grant that comes with the plan or a promotion and expires.
public enum CreditKind { Prepaid, Included }

// A remaining money balance. Amount is in whole currency units, Currency an ISO code ("USD").
public record CreditSnapshot(
    Source Source,
    CreditKind Kind,
    decimal Amount,
    string Currency,
    DateTimeOffset? ExpiresAt = null);
