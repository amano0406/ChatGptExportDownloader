using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ChatGptExportDownloader.Core;

public sealed class RangeDownloadService
{
    private readonly HttpClient _httpClient;

    public RangeDownloadService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public async Task<RangeDownloadResult> DownloadAsync(
        CapturedRequest request,
        RangeDownloadOptions options,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var probe = await new RangeProbeClient(_httpClient).ProbeAsync(request, cancellationToken).ConfigureAwait(false);
        if (probe.TotalBytes is null or <= 0)
        {
            throw new InvalidOperationException("The server did not return a total file size.");
        }

        if (!probe.RangeSupported)
        {
            throw new InvalidOperationException("The server did not confirm byte range support.");
        }

        var totalBytes = probe.TotalBytes.Value;
        Directory.CreateDirectory(options.OutputDirectory);

        var finalPath = Path.Combine(options.OutputDirectory, options.SafeOutputFileName);
        var partPath = finalPath + ".part";
        var statePath = finalPath + ".download-state.json";

        if (File.Exists(finalPath))
        {
            var length = new FileInfo(finalPath).Length;
            if (length == totalBytes)
            {
                var verification = options.VerifyZip ? ZipVerifier.Verify(finalPath) : null;
                progress.Report(new DownloadProgress(totalBytes, totalBytes, 0, 0, "already-complete"));
                return new RangeDownloadResult(finalPath, totalBytes, true, verification);
            }

            throw new IOException($"Refusing to overwrite existing file with unexpected size: {finalPath}");
        }

        var downloaded = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (downloaded > totalBytes)
        {
            throw new IOException($"Partial file is larger than the expected file size: {partPath}");
        }

        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;

        await using (var output = new FileStream(
                         partPath,
                         FileMode.Append,
                         FileAccess.Write,
                         FileShare.Read,
                         bufferSize: 1024 * 1024,
                         useAsync: true))
        {
            while (downloaded < totalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var start = downloaded;
                var end = Math.Min(start + options.ChunkBytes - 1, totalBytes - 1);
                var expectedLength = end - start + 1;
                var currentRange = $"bytes={start}-{end}";
                var attempt = 0;

                while (true)
                {
                    attempt++;
                    try
                    {
                        var chunkPath = $"{partPath}.chunk";
                        var written = await DownloadChunkAsync(
                            request,
                            chunkPath,
                            start,
                            end,
                            cancellationToken).ConfigureAwait(false);

                        if (written != expectedLength)
                        {
                            if (File.Exists(chunkPath))
                            {
                                File.Delete(chunkPath);
                            }

                            throw new IOException($"Short chunk: expected {expectedLength}, got {written}.");
                        }

                        await using (var chunk = new FileStream(
                                         chunkPath,
                                         FileMode.Open,
                                         FileAccess.Read,
                                         FileShare.Read,
                                         bufferSize: 1024 * 1024,
                                         useAsync: true))
                        {
                            await chunk.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                        }

                        File.Delete(chunkPath);
                        downloaded += written;
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        output.Flush(flushToDisk: true);
                        await WriteStateAsync(
                            statePath,
                            finalPath,
                            partPath,
                            totalBytes,
                            downloaded,
                            cancellationToken).ConfigureAwait(false);

                        var speed = downloaded / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                        progress.Report(new DownloadProgress(downloaded, totalBytes, speed, retryCount, currentRange));
                        break;
                    }
                    catch (Exception ex) when (IsRetryable(ex) && attempt < options.MaxRetries)
                    {
                        retryCount++;
                        var delay = TimeSpan.FromSeconds(Math.Min(60, attempt * 2));
                        var speed = downloaded / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                        progress.Report(new DownloadProgress(downloaded, totalBytes, speed, retryCount, currentRange));
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        if (File.Exists(finalPath))
        {
            throw new IOException($"Final file appeared during download; refusing to overwrite: {finalPath}");
        }

        File.Move(partPath, finalPath);
        var zipVerification = options.VerifyZip ? ZipVerifier.Verify(finalPath) : null;
        return new RangeDownloadResult(finalPath, totalBytes, false, zipVerification);
    }

    private async Task<long> DownloadChunkAsync(
        CapturedRequest request,
        string chunkPath,
        long start,
        long end,
        CancellationToken cancellationToken)
    {
        if (File.Exists(chunkPath))
        {
            File.Delete(chunkPath);
        }

        using var message = CreateRequest(request);
        message.Headers.Range = new RangeHeaderValue(start, end);

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException($"Expected 206 Partial Content, got {(int)response.StatusCode}.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            chunkPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        var buffer = new byte[1024 * 1024];
        var written = 0L;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return written;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
        }
    }

    private static HttpRequestMessage CreateRequest(CapturedRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
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

        return message;
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex is IOException
            or HttpRequestException
            or TaskCanceledException
            or TimeoutException;
    }

    private static async Task WriteStateAsync(
        string statePath,
        string finalPath,
        string partPath,
        long totalBytes,
        long downloadedBytes,
        CancellationToken cancellationToken)
    {
        var state = new
        {
            finalPath,
            partPath,
            totalBytes,
            downloadedBytes,
            updatedAt = DateTimeOffset.Now
        };

        var tempPath = statePath + ".tmp";
        await File.WriteAllTextAsync(
            tempPath,
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, statePath, overwrite: true);
    }
}
