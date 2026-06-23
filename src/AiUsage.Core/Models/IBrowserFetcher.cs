namespace AiUsage.Core.Models;

public interface IBrowserFetcher
{
    /// <summary>
    /// Fetches JSON from an absolute URL using the browser's authenticated session.
    /// Optional extra request headers (e.g. Authorization) are added to the fetch().
    /// Throws HttpRequestException on HTTP error status.
    /// </summary>
    Task<string> FetchJsonAsync(
        string url, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null);
}
