namespace ChatGptExportDownloader.Core;

public static class ChatGptExportUrl
{
    public static string GetFileName(Uri url)
    {
        var query = ParseQuery(url.Query);
        query.TryGetValue("id", out var id);
        var name = string.IsNullOrWhiteSpace(id) ? "chatgpt-export.zip" : Path.GetFileName(id);
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.zip";
    }

    public static string Redact(Uri url)
    {
        var builder = new UriBuilder(url);
        var query = ParseQuery(builder.Query);

        foreach (var key in query.Keys.ToArray())
        {
            if (key.Equals("sig", StringComparison.OrdinalIgnoreCase))
            {
                query[key] = "***";
            }
        }

        builder.Query = string.Join(
            "&",
            query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri.ToString();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.StartsWith('?') ? query[1..] : query;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return result;
        }

        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator >= 0 ? part[..separator] : part;
            var value = separator >= 0 ? part[(separator + 1)..] : "";
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }

        return result;
    }
}
