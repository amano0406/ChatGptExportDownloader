using System.Text;

namespace ChatGptExportDownloader.Core;

public static class CurlCommandParser
{
    public static CapturedRequest Parse(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            throw new ArgumentException("cURL text is empty.", nameof(commandText));
        }

        var tokens = Tokenize(UnescapeCmd(commandText));
        if (tokens.Count == 0)
        {
            throw new FormatException("No cURL tokens were found.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? url = null;
        string? cookie = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (i == 0 && IsCurlExecutable(token))
            {
                continue;
            }

            if (token.Equals("-H", StringComparison.OrdinalIgnoreCase)
                || token.Equals("--header", StringComparison.OrdinalIgnoreCase))
            {
                var header = RequireNext(tokens, ref i, token);
                var separator = header.IndexOf(':');
                if (separator > 0)
                {
                    var name = header[..separator].Trim();
                    var value = header[(separator + 1)..].Trim();
                    headers[name] = value;
                }

                continue;
            }

            if (token.Equals("-b", StringComparison.OrdinalIgnoreCase)
                || token.Equals("--cookie", StringComparison.OrdinalIgnoreCase)
                || token.Equals("--cookie-jar", StringComparison.OrdinalIgnoreCase))
            {
                cookie = RequireNext(tokens, ref i, token);
                continue;
            }

            if (token.Equals("--url", StringComparison.OrdinalIgnoreCase))
            {
                url = RequireNext(tokens, ref i, token);
                continue;
            }

            if (token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url ??= token;
            }
        }

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl))
        {
            throw new FormatException("Request URL was not found.");
        }

        if (string.IsNullOrWhiteSpace(cookie) && headers.TryGetValue("Cookie", out var cookieHeader))
        {
            cookie = cookieHeader;
        }

        if (string.IsNullOrWhiteSpace(cookie))
        {
            throw new FormatException("Cookie header was not found.");
        }

        headers.TryGetValue("User-Agent", out var userAgent);
        headers.TryGetValue("Accept-Language", out var acceptLanguage);

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            throw new FormatException("User-Agent header was not found.");
        }

        return CapturedRequest.Create(parsedUrl, cookie, userAgent, acceptLanguage, headers);
    }

    public static string UnescapeCmd(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '^' && i + 1 < value.Length)
            {
                i++;
                builder.Append(value[i]);
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static List<string> Tokenize(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (inQuotes && c == '\\' && i + 1 < value.Length && value[i + 1] == '"')
            {
                current.Append('"');
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string RequireNext(IReadOnlyList<string> tokens, ref int index, string option)
    {
        if (index + 1 >= tokens.Count)
        {
            throw new FormatException($"Option {option} requires a value.");
        }

        index++;
        return tokens[index];
    }

    private static bool IsCurlExecutable(string token)
    {
        var fileName = Path.GetFileName(token);
        return fileName.Equals("curl", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("curl.exe", StringComparison.OrdinalIgnoreCase);
    }
}
