namespace AiUsage.Core.Models;

public interface IUsageSink
{
    Task EmitAsync(LimitSnapshot s);

    // The complete set of balances for this poll: a kind missing from the list is
    // unavailable right now, so a sink showing it hides it.
    // Defaulted because only tiles that display a balance need it.
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
