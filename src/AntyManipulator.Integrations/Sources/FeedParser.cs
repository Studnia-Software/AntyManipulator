using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AntyManipulator.Core.Text;

namespace AntyManipulator.Integrations.Sources;

public sealed record FeedItem(string Title, string Link, DateTimeOffset? PublishedAt, string Description, IReadOnlyList<string> Categories);

/// <summary>
/// Tolerancyjny parser RSS. Część serwisów (np. Demagog) wysyła encje HTML niedozwolone w XML,
/// przez co XmlReader odrzuca cały kanał, więc wyciągamy pola wyrażeniami regularnymi.
/// </summary>
public static partial class FeedParser
{
    public static IReadOnlyList<FeedItem> Parse(string xml, int maxDescriptionChars = 1500)
    {
        var items = new List<FeedItem>();
        foreach (Match m in ItemRegex().Matches(xml))
        {
            var body = m.Groups[1].Value;
            var title = Clean(Field(body, "title"), 400);
            var link = Clean(Field(body, "link"), 1000);
            if (string.IsNullOrEmpty(title) || !Uri.TryCreate(link, UriKind.Absolute, out var linkUri)
                || (linkUri.Scheme != Uri.UriSchemeHttp && linkUri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            var description = Clean(Field(body, "description"), maxDescriptionChars);
            var categories = CategoryRegex().Matches(body).Select(c => Clean(c.Groups[1].Value, 80)).Where(c => c.Length > 0).Distinct().ToList();
            items.Add(new FeedItem(title, StripTracking(linkUri), ParseDate(Clean(Field(body, "pubDate"), 100)), description, categories));
        }

        return items;
    }

    public static string HtmlToText(string? html, int max)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var noTags = TagRegex().Replace(html, " ");
        return TextTools.Truncate(TextTools.CollapseWhitespace(WebUtility.HtmlDecode(noTags)), max);
    }

    private static string Field(string body, string name)
    {
        var m = Regex.Match(body, $@"<{name}(?:\s[^>]*)?>(.*?)</{name}>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private static string Clean(string value, int max)
    {
        var v = CdataRegex().Replace(value, "$1");
        return HtmlToText(WebUtility.HtmlDecode(v), max);
    }

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string StripTracking(Uri uri)
    {
        var s = uri.GetLeftPart(UriPartial.Path);
        return s;
    }

    [GeneratedRegex(@"<item\b[^>]*>(.*?)</item>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ItemRegex();

    [GeneratedRegex(@"<category\b[^>]*>(.*?)</category>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CategoryRegex();

    [GeneratedRegex(@"<!\[CDATA\[(.*?)\]\]>", RegexOptions.Singleline)]
    private static partial Regex CdataRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
}
