using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using AiUsage.App.Features.Copilot;
using AiUsage.Core.Config;
using AiUsage.Core.Models;

namespace AiUsage.App.Providers;

/// <summary>
/// GitHub device-flow connector for Copilot. Unlike the WebView2 providers there is no live
/// session: it persists an OAuth token to config and the adapter reads it, so
/// <see cref="Fetcher"/> is always null. Silent restore is a no-op (the token is already
/// in config and the tile picks it up on the next host build).
/// </summary>
internal sealed class CopilotConnector : IProviderConnector
{
    private readonly string _configPath;
    private readonly Action<string> _setStatus;
    private readonly Action<bool> _setConnected;
    private readonly Action<bool> _setConnecting;
    private readonly Action _onChanged;

    private CancellationTokenSource? _cts;

    public string Key => "Copilot";
    public IBrowserFetcher? Fetcher => null;

    public CopilotConnector(
        string configPath,
        Action<string> setStatus,
        Action<bool> setConnected,
        Action<bool> setConnecting,
        Action onChanged)
    {
        _configPath = configPath;
        _setStatus = setStatus;
        _setConnected = setConnected;
        _setConnecting = setConnecting;
        _onChanged = onChanged;
    }

    public void Connect(TopLevel owner)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _ = LoginAsync(_cts.Token);
    }

    // GitHub device flow: get a code, open the browser to github.com/login/device with the
    // code shown in the status line, poll until the user authorises, then persist the token.
    private async Task LoginAsync(CancellationToken ct)
    {
        try
        {
            _setConnecting(true);
            _setStatus("Requesting code…");

            var code = await CopilotDeviceFlow.RequestCodeAsync(ct);
            _setStatus($"Enter code {code.UserCode} at {code.VerificationUri} (browser opened)…");
            OpenInBrowser(code.VerificationUri);

            var token = await CopilotDeviceFlow.PollForTokenAsync(code, ct);
            ConfigSaver.UpdateCopilotToken(_configPath, token);

            _setConnected(true);
            _setStatus("Connected! Loading usage…");
            _onChanged();
        }
        catch (OperationCanceledException) { _setStatus(""); }
        catch (Exception ex) { _setStatus(ex.Message); }
        finally { _setConnecting(false); }
    }

    public Task TryRestoreAsync() => Task.CompletedTask;

    public void Disconnect()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        ConfigSaver.UpdateCopilotToken(_configPath, null);
        _setConnected(false);
        _setStatus("");
        _onChanged();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch { /* user can open the URL manually from the status line */ }
    }
}
