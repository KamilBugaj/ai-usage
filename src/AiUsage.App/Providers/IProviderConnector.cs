using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using AiUsage.Core.Models;

namespace AiUsage.App.Providers;

/// <summary>
/// One provider's interactive auth lifecycle — connect, disconnect, silent restore —
/// owning its own session and cancellation. App drives every provider uniformly through
/// this interface instead of hand-written per-provider methods.
/// </summary>
internal interface IProviderConnector : IDisposable
{
    string Key { get; }

    /// <summary>Live session used by the poll adapter, or null (not connected / config-driven).</summary>
    IBrowserFetcher? Fetcher { get; }

    /// <summary>Starts the interactive sign-in flow (fire-and-forget).</summary>
    void Connect(TopLevel owner);

    /// <summary>Tears down the session and clears the persisted "connected" marker.</summary>
    void Disconnect();

    /// <summary>Silently restores a session from persisted cookies/token on startup.</summary>
    Task TryRestoreAsync();
}
