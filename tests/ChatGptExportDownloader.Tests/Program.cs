using System.Net;
using System.Net.Http.Headers;
using ChatGptExportDownloader.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Parse cURL(cmd) request", TestParseCurlCmd),
    ("Redact signed URL", TestRedactUrl),
    ("Probe parses Range response", TestRangeProbe),
    ("Range downloader writes and verifies file", TestRangeDownload),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static Task TestParseCurlCmd()
{
    const string command = """
curl ^"https://chatgpt.com/backend-api/estuary/content?id=abc-2026-04-27.zip^&ts=493717^&sig=secret^&v=0^" ^
  -H ^"accept-language: ja,en-US;q=0.9,en;q=0.8^" ^
  -b ^"__Secure-next-auth.session-token.0=token^%^3D; cf_clearance=clearance; oai-chat-web-route=^\^"route^\^"^" ^
  -H ^"user-agent: Mozilla/5.0 Test Browser^"
""";

    var request = CurlCommandParser.Parse(command);
    AssertEqual("abc-2026-04-27.zip", request.FileName);
    AssertTrue(request.Url.Query.Contains("sig=secret"), "URL should preserve sig internally.");
    AssertTrue(request.CookieHeader.Contains("token%3D"), "Cookie should unescape cmd caret before percent.");
    AssertTrue(request.CookieHeader.Contains("\"route\""), "Quoted cookie value should survive tokenization.");
    AssertEqual("Mozilla/5.0 Test Browser", request.UserAgent);
    AssertEqual("ja,en-US;q=0.9,en;q=0.8", request.AcceptLanguage);
    return Task.CompletedTask;
}

static Task TestRedactUrl()
{
    var url = new Uri("https://chatgpt.com/backend-api/estuary/content?id=file.zip&sig=secret&v=0");
    var redacted = ChatGptExportUrl.Redact(url);
    AssertTrue(redacted.Contains("sig=%2A%2A%2A") || redacted.Contains("sig=***"), "sig should be redacted.");
    AssertTrue(!redacted.Contains("secret"), "secret value must not remain.");
    return Task.CompletedTask;
}

static async Task TestRangeProbe()
{
    var request = CapturedRequest.Create(
        new Uri("https://chatgpt.com/backend-api/estuary/content?id=file.zip&sig=secret"),
        "session=token",
        "TestAgent",
        "ja",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/octet-stream"
        });

    using var httpClient = new HttpClient(new FakeProbeHandler());
    var result = await new RangeProbeClient(httpClient).ProbeAsync(request);
    AssertEqual(206, result.StatusCode);
    AssertTrue(result.RangeSupported, "Range should be supported.");
    AssertEqual(8746139436L, result.TotalBytes ?? 0);
}

static async Task TestRangeDownload()
{
    var outputDirectory = Path.Combine(Path.GetTempPath(), "chatgpt-export-downloader-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);

    try
    {
        const long totalBytes = 3 * 1024 * 1024 + 17;
        var request = CapturedRequest.Create(
            new Uri("https://chatgpt.com/backend-api/estuary/content?id=test-export.zip&sig=secret"),
            "session=token",
            "TestAgent",
            "ja",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        using var httpClient = new HttpClient(new FakeDownloadHandler(totalBytes));
        var progress = new CollectingProgress();
        var result = await new RangeDownloadService(httpClient).DownloadAsync(
            request,
            new RangeDownloadOptions(outputDirectory, "test-export.bin", ChunkMiB: 1, MaxRetries: 3, VerifyZip: false),
            progress,
            CancellationToken.None);

        AssertTrue(File.Exists(result.FinalPath), "Final file should exist.");
        AssertEqual(totalBytes, new FileInfo(result.FinalPath).Length);
        AssertEqual(totalBytes, progress.Last?.DownloadedBytes ?? 0);
        AssertTrue(!File.Exists(result.FinalPath + ".part"), "Part file should be renamed.");
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

sealed class CollectingProgress : IProgress<DownloadProgress>
{
    public DownloadProgress? Last { get; private set; }

    public void Report(DownloadProgress value)
    {
        Last = value;
    }
}

sealed class FakeProbeHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Range?.Ranges.FirstOrDefault()?.From != 0)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }

        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent([0])
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 8746139436);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        response.Content.Headers.LastModified = new DateTimeOffset(2026, 4, 27, 15, 18, 57, TimeSpan.Zero);
        response.Headers.AcceptRanges.Add("bytes");
        response.Headers.ETag = new EntityTagHeaderValue("\"0x8DEA4704D8A15B6\"");
        return Task.FromResult(response);
    }
}

sealed class FakeDownloadHandler(long totalBytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var range = request.Headers.Range?.Ranges.FirstOrDefault();
        if (range?.From is null || range.To is null)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }

        var start = range.From.Value;
        var end = Math.Min(range.To.Value, totalBytes - 1);
        if (start < 0 || start > end || end >= totalBytes)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
        }

        var length = checked((int)(end - start + 1));
        var body = new byte[length];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)((start + i) % 251);
        }

        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(body)
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, totalBytes);
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        response.Headers.AcceptRanges.Add("bytes");
        return Task.FromResult(response);
    }
}
