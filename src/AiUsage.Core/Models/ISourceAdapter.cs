namespace AiUsage.Core.Models;

public interface IUsageSink
{
    // The complete set of limit windows for this poll: a window missing from the list no
    // longer exists, so a sink showing it clears it. Never called with an empty list.
    Task EmitLimitsAsync(IReadOnlyList<LimitSnapshot> limits);

    // Same contract for money balances: a kind missing from the list is unavailable right
    // now. Defaulted because only tiles that display a balance need it.
    Task EmitCreditsAsync(IReadOnlyList<CreditSnapshot> credits) => Task.CompletedTask;
}

public interface ISourceAdapter
{
    Source Source { get; }
    TimeSpan PollInterval { get; }

    // Fetches current data and emits to sink. Throws on network/parse error.
    // AppHost runs the retry loop and maps errors to tile.ErrorMessage.
    Task PollOnceAsync(IUsageSink sink, CancellationToken ct);
}
