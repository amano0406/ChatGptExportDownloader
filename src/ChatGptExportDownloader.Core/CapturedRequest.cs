using System.Collections.ObjectModel;

namespace ChatGptExportDownloader.Core;

public sealed record CapturedRequest(
    Uri Url,
    string CookieHeader,
    string UserAgent,
    string? AcceptLanguage,
    IReadOnlyDictionary<string, string> Headers)
{
    public bool HasCookie => !string.IsNullOrWhiteSpace(CookieHeader);

    public bool HasUserAgent => !string.IsNullOrWhiteSpace(UserAgent);

    public string FileName => ChatGptExportUrl.GetFileName(Url);

    public string RedactedUrl => ChatGptExportUrl.Redact(Url);

    public static CapturedRequest Create(
        Uri url,
        string cookieHeader,
        string userAgent,
        string? acceptLanguage,
        IDictionary<string, string> headers)
    {
        return new CapturedRequest(
            url,
            cookieHeader,
            userAgent,
            acceptLanguage,
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)));
    }
}
