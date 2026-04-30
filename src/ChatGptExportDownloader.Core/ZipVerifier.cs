using System.IO.Compression;

namespace ChatGptExportDownloader.Core;

public static class ZipVerifier
{
    public static ZipVerificationResult Verify(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return new ZipVerificationResult(true, archive.Entries.Count, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ZipVerificationResult(false, 0, ex.Message);
        }
    }
}
