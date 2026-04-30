namespace ChatGptExportDownloader.Core;

public sealed record DownloadProgress(
    long DownloadedBytes,
    long TotalBytes,
    double BytesPerSecond,
    int RetryCount,
    string CurrentRange)
{
    public double Percent => TotalBytes <= 0 ? 0 : DownloadedBytes * 100d / TotalBytes;
}
