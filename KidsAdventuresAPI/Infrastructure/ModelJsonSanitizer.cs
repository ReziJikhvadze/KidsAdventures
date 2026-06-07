namespace AdventurePacks.Api.Infrastructure;

internal static class ModelJsonSanitizer
{
    public static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            text = firstLineEnd >= 0 ? text[(firstLineEnd + 1)..] : text.Trim('`');

            var fenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceIndex >= 0)
            {
                text = text[..fenceIndex];
            }
        }

        text = text.Trim();

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            text = text[start..(end + 1)];
        }

        return text.Trim();
    }
}
