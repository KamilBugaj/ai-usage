using System;
using System.Threading;

namespace AiUsage.App.Infrastructure;

/// <summary>
/// Per-user single-instance guard built on a named mutex. A second instance signals
/// the first one (named event) to show its window, then exits. Named events are
/// Windows-only, so on Unix the second instance just exits silently.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = "KB.AI.Usage.SingleInstance";
    private const string ShowEventName = "KB.AI.Usage.ShowWindow";

    // Held for the process lifetime; the OS releases it when the process exits.
    private static Mutex? _mutex;

    /// <summary>
    /// Returns true when this is the first instance. Otherwise asks the running
    /// instance to show its window and returns false — the caller should exit.
    /// </summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew) return true;

        try
        {
            if (OperatingSystem.IsWindows()
                && EventWaitHandle.TryOpenExisting(ShowEventName, out var evt))
            {
                using (evt) evt.Set();
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Listens for show requests from later instances and invokes <paramref name="onShow"/>
    /// for each one. The callback runs on a background thread — marshal to the UI
    /// thread in the caller. No-op on non-Windows (named events unsupported there).
    /// </summary>
    public static void WatchShowRequests(Action onShow)
    {
        if (!OperatingSystem.IsWindows()) return;

        var evt = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                evt.WaitOne();
                onShow();
            }
        })
        { IsBackground = true, Name = "SingleInstance.ShowWatcher" };
        thread.Start();
    }
}
