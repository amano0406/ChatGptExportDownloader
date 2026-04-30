using System.Net;
using System.Net.Http.Headers;

namespace ChatGptExportDownloader.Core;

public sealed class RangeProbeClient
{
    private readonly HttpClient _httpClient;

    public RangeProbeClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    public async Task<ProbeResult> ProbeAsync(CapturedRequest request, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        message.Headers.Range = new RangeHeaderValue(0, 0);
        message.Headers.TryAddWithoutValidation("Cookie", request.CookieHeader);
        message.Headers.TryAddWithoutValidation("User-Agent", request.UserAgent);

        if (!string.IsNullOrWhiteSpace(request.AcceptLanguage))
        {
            message.Headers.TryAddWithoutValidation("Accept-Language", request.AcceptLanguage);
        }

        if (request.Headers.TryGetValue("Accept", out var accept))
        {
            message.Headers.TryAddWithoutValidation("Accept", accept);
        }

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var statusCode = (int)response.StatusCode;
        long? totalBytes = response.Content.Headers.ContentRange?.Length;
        var contentRange = response.Content.Headers.ContentRange?.ToString();
        var acceptRanges = response.Headers.TryGetValues("Accept-Ranges", out var values)
            ? string.Join(", ", values)
            : null;

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[1];
            _ = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        }

        return new ProbeResult(
            statusCode,
            response.StatusCode == HttpStatusCode.PartialContent || string.Equals(acceptRanges, "bytes", StringComparison.OrdinalIgnoreCase),
            totalBytes,
            contentRange,
            acceptRanges,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified,
            response.Content.Headers.ContentType?.ToString());
    }
}
