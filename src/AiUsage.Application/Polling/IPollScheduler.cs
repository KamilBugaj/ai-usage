using AiUsage.Core.Models;

namespace AiUsage.Application.Polling;

/// <summary>Hands a provider's adapter + sink to the host, which runs its poll loop.</summary>
public interface IPollScheduler
{
    void Schedule(ISourceAdapter adapter, IUsageSink sink, IPollStatus status);
}
