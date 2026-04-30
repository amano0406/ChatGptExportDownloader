namespace ChatGptExportDownloader.Core;

public sealed record ProbeResult(
    int StatusCode,
    bool RangeSupported,
    long? TotalBytes,
    string? ContentRange,
    string? AcceptRanges,
    string? ETag,
    DateTimeOffset? LastModified,
    string? ContentType)
{
    public string DisplaySize => TotalBytes is null
        ? "unknown"
        : $"{TotalBytes.Value / 1_000_000_000d:0.000} GB ({TotalBytes.Value / Math.Pow(1024, 3):0.000} GiB)";
}
