namespace ChatGptExportDownloader.Core;

public sealed record RangeDownloadOptions(
    string OutputDirectory,
    string OutputFileName,
    int ChunkMiB = 128,
    int MaxRetries = 30,
    bool VerifyZip = true)
{
    public long ChunkBytes => Math.Max(1, ChunkMiB) * 1024L * 1024L;

    public string SafeOutputFileName => string.IsNullOrWhiteSpace(OutputFileName)
        ? "chatgpt-export.zip"
        : Path.GetFileName(OutputFileName);
}
