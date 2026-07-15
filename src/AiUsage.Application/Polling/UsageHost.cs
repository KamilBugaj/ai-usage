using AiUsage.Application.Abstractions;
using AiUsage.Core.Models;

namespace AiUsage.Application.Polling;

/// <summary>
/// Runs one poll loop per provider, owning the warm-up/backoff retry policy and a shared
/// "refresh now" signal. UI-framework-agnostic: loading/error status updates are marshalled
/// through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class UsageHost : IPollScheduler, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();
    private volatile TaskCompletionSource<bool> _refreshSignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public UsageHost(IUiDispatcher dispatcher) => _dispatcher = dispatcher;

    public void Schedule(ISourceAdapter adapter, IUsageSink sink, IPollStatus status)
        => _ = PollLoopAsync(adapter, sink, status);

    private async Task PollLoopAsync(ISourceAdapter adapter, IUsageSink sink, IPollStatus status)
    {
        var ct = _cts.Token;
        var interval = adapter.PollInterval;
        var failures = 0;

        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                await adapter.PollOnceAsync(sink, ct);
                // Clear any prior error, but leave IsLoading alone: the sink flips it off
                // when real data arrives. A successful-but-empty poll (e.g. WebView2 still
                // warming up on a fresh machine) keeps the tile showing "Loading…".
                _dispatcher.Post(() => status.ErrorMessage = "");
                failures = 0;
                wait = interval;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (UsageUnavailableException ex)
            {
                // Valid session, but this plan exposes no usage data — an expected state,
                // not a failure. Show a muted status line, clear the backoff, and keep
                // polling on the normal interval (the plan could change / gain access).
                _dispatcher.Post(() => status.SetUnavailable(ex.Message));
                failures = 0;
                wait = interval;
            }
            catch (Exception ex)
            {
                failures++;
                // Treat the first couple of failures as warm-up (WebView2 / Cloudflare not
                // ready yet on a cold start): keep showing "Loading…" and retry quickly with
                // backoff instead of waiting the full interval. Surface the error only once it
                // persists, so a transient cold-start blip never flashes a scary message.
                if (failures >= 3)
                    _dispatcher.Post(() => { status.IsLoading = false; status.ErrorMessage = ex.Message; });
                var backoffSecs = Math.Min(3 * Math.Pow(2, failures - 1), interval.TotalSeconds);
                wait = TimeSpan.FromSeconds(backoffSecs);
            }

            try
            {
                var signal = _refreshSignal.Task;
                // Linked CTS so the pending Task.Delay timer is released as soon as a
                // refresh signal wins the race, instead of lingering until interval end.
                using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await Task.WhenAny(Task.Delay(wait, delayCts.Token), signal);
                delayCts.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public void RefreshAll()
    {
        var old = Interlocked.Exchange(
            ref _refreshSignal,
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult(true);
    }

    public void Dispose()
    {
        _cts.Cancel();
        RefreshAll();
    }
}
