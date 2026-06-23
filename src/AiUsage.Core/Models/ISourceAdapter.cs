namespace AiUsage.Core.Models;

public interface IUsageSink
{
    Task EmitAsync(LimitSnapshot s);
}

public interface ISourceAdapter
{
    Source Source { get; }
    TimeSpan PollInterval { get; }

    // Fetches current data and emits to sink. Throws on network/parse error.
    // AppHost runs the retry loop and maps errors to tile.ErrorMessage.
    Task PollOnceAsync(IUsageSink sink, CancellationToken ct);
}
