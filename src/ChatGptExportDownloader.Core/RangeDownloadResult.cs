namespace ChatGptExportDownloader.Core;

public sealed record RangeDownloadResult(
    string FinalPath,
    long TotalBytes,
    bool AlreadyComplete,
    ZipVerificationResult? ZipVerification);
