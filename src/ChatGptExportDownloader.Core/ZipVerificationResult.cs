namespace ChatGptExportDownloader.Core;

public sealed record ZipVerificationResult(bool IsValid, int EntryCount, string? ErrorMessage);
