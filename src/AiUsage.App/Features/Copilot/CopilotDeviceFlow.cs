using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsage.App.Features.Copilot;

/// <summary>
/// GitHub OAuth device flow for Copilot. Uses the public VS Code client id (the same
/// one the Copilot plugins use) so the resulting token is accepted by the internal
/// copilot_internal/user usage endpoint. No client secret is involved.
/// </summary>
internal static class CopilotDeviceFlow
{
    private const string ClientId = "Iv1.b507a08c87ecfe98"; // VS Code OAuth app
    private const string Scope = "read:user";
    private const string DeviceCodeUrl = "https://github.com/login/device/code";
    private const string AccessTokenUrl = "https://github.com/login/oauth/access_token";

    private static readonly HttpClient _http = new();

    public sealed record DeviceCode(
        string UserCode, string VerificationUri, string DeviceCodeValue, int Interval, int ExpiresIn);

    /// <summary>Step 1: ask GitHub for a device + user code.</summary>
    public static async Task<DeviceCode> RequestCodeAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, DeviceCodeUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = Scope,
            })
        };
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new DeviceCode(
            UserCode: root.GetProperty("user_code").GetString() ?? "",
            VerificationUri: root.GetProperty("verification_uri").GetString() ?? "https://github.com/login/device",
            DeviceCodeValue: root.GetProperty("device_code").GetString() ?? "",
            Interval: root.TryGetProperty("interval", out var i) ? i.GetInt32() : 5,
            ExpiresIn: root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 900);
    }

    /// <summary>Step 2: poll until the user authorises in the browser. Returns the OAuth token.</summary>
    public static async Task<string> PollForTokenAsync(DeviceCode code, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(code.Interval, 1));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(code.ExpiresIn);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("GitHub authorization timed out. Try connecting again.");

            using var req = new HttpRequestMessage(HttpMethod.Post, AccessTokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = code.DeviceCodeValue,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                })
            };
            req.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("access_token", out var token) &&
                token.ValueKind == JsonValueKind.String)
                return token.GetString()!;

            var error = root.TryGetProperty("error", out var er) ? er.GetString() : null;
            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "expired_token":
                    throw new TimeoutException("GitHub authorization expired. Try connecting again.");
                case "access_denied":
                    throw new OperationCanceledException("GitHub authorization was denied.");
                default:
                    if (error is not null)
                        throw new InvalidOperationException($"GitHub device flow error: {error}");
                    continue;
            }
        }

        ct.ThrowIfCancellationRequested();
        return ""; // unreachable
    }
}
