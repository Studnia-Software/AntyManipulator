using System.Net;
using System.Net.Sockets;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AntyManipulator.Core.Abstractions;
using AntyManipulator.Core.Text;
using AntyManipulator.Integrations.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AntyManipulator.Integrations.Fetching;

/// <summary>
/// Pobiera stronę podaną przez użytkownika. Adres pochodzi z zewnątrz, więc:
/// tylko http/https i porty 80/443, połączenia wyłącznie do publicznych IP (sprawdzane
/// przy każdym połączeniu, także po przekierowaniu), limit rozmiaru i czasu. Ukryte
/// elementy strony są usuwane, żeby nie przemycały instrukcji dla modelu.
/// </summary>
public sealed class SafeContentFetcher(
    IHttpClientFactory httpFactory,
    IOptions<FetchOptions> options,
    TimeProvider time,
    ILogger<SafeContentFetcher> logger) : IContentFetcher
{
    public const string ClientName = "safe-fetch";

    private static readonly string[] RemovedSelectors =
    [
        "script", "style", "noscript", "template", "svg", "iframe", "object", "embed", "form", "button",
        "nav", "footer", "aside", "header", "[hidden]", "[aria-hidden=true]", "[style*='display:none']",
        "[style*='display: none']", "[style*='visibility:hidden']", "[style*='visibility: hidden']",
        "[style*='font-size:0']", "[style*='font-size: 0']", "[style*='opacity:0']", "[style*='opacity: 0']",
    ];

    private static readonly string[] TextSelectors = ["h1", "h2", "h3", "h4", "p", "li", "blockquote", "figcaption", "td"];

    public async Task<FetchedContent> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ContentFetchException(FetchFailureKind.InvalidUrl, "Obsługujemy tylko adresy zaczynające się od http:// lub https://.");
        }

        if (!url.IsDefaultPort || !string.IsNullOrEmpty(url.UserInfo))
        {
            throw new ContentFetchException(FetchFailureKind.ForbiddenAddress, "Ten adres ma nietypowy port lub dane logowania, więc go nie otwieramy.");
        }

        var opts = options.Value;
        var http = httpFactory.CreateClient(ClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(opts.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.8");
            request.Headers.AcceptLanguage.ParseAdd("pl,en;q=0.7");
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation(ex, "Fetching {Url} failed", url);
            if (FindInner<ForbiddenAddressException>(ex) is not null)
            {
                throw new ContentFetchException(FetchFailureKind.ForbiddenAddress, "Ten adres prowadzi do sieci wewnętrznej, więc go nie otwieramy.", ex);
            }

            throw new ContentFetchException(FetchFailureKind.Unreachable, "Nie udało się połączyć ze stroną. Sprawdź adres albo wklej treść jako tekst.", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ContentFetchException(FetchFailureKind.Unreachable, "Strona nie odpowiedziała na czas. Wklej treść jako tekst.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or (HttpStatusCode)429
                    ? "Strona nie pozwala na automatyczne pobranie treści (np. paywall lub blokada botów). Wklej treść jako tekst."
                    : $"Strona zwróciła błąd ({(int)response.StatusCode}). Sprawdź adres albo wklej treść jako tekst.";
                throw new ContentFetchException(FetchFailureKind.HttpError, message);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "text/html";
            var isHtml = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase);
            if (!isHtml && !mediaType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                throw new ContentFetchException(FetchFailureKind.UnsupportedContent,
                    "Pod tym adresem nie ma strony z tekstem (np. jest plik, obraz lub film). Wklej treść jako tekst.");
            }

            if (response.Content.Headers.ContentLength > opts.MaxBytes)
            {
                throw new ContentFetchException(FetchFailureKind.TooLarge, "Strona jest zbyt duża do analizy. Wklej fragment jako tekst.");
            }

            var bytes = await ReadLimitedAsync(response, opts.MaxBytes, timeout.Token);
            var finalUrl = response.RequestMessage?.RequestUri ?? url;

            var fetchedAt = time.GetUtcNow();
            if (!isHtml)
            {
                var plain = DecodeText(bytes, response.Content.Headers.ContentType?.CharSet);
                return Result(url, finalUrl, null, finalUrl.Host, null, plain, fetchedAt);
            }

            var parser = new HtmlParser();
            using var stream = new MemoryStream(bytes);
            using var document = await parser.ParseDocumentAsync(stream, timeout.Token);

            var title = Meta(document, "og:title") ?? document.Title;
            var publisher = Meta(document, "og:site_name") ?? finalUrl.Host;
            var published = ParseDate(Meta(document, "article:published_time")
                                      ?? document.QuerySelector("[itemprop=datePublished]")?.GetAttribute("content")
                                      ?? document.QuerySelector("time[datetime]")?.GetAttribute("datetime"));

            foreach (var selector in RemovedSelectors)
            {
                foreach (var el in document.QuerySelectorAll(selector).ToList())
                {
                    el.Remove();
                }
            }

            var root = document.QuerySelector("article") ?? document.QuerySelector("main") ?? document.Body;
            var text = ExtractText(root);
            if (text.Length < 40)
            {
                throw new ContentFetchException(FetchFailureKind.NoText,
                    "Na tej stronie nie znaleźliśmy tekstu do analizy (może być generowana skryptem lub wymagać logowania). Wklej treść jako tekst.");
            }

            return Result(url, finalUrl, title, publisher, published, text, fetchedAt);
        }
    }

    private static FetchedContent Result(Uri requested, Uri final, string? title, string? publisher, DateTimeOffset? published, string text, DateTimeOffset at) => new()
    {
        RequestedUrl = requested,
        FinalUrl = final,
        Title = string.IsNullOrWhiteSpace(title) ? null : TextTools.Truncate(TextTools.CollapseWhitespace(title), 300),
        Publisher = string.IsNullOrWhiteSpace(publisher) ? null : TextTools.Truncate(TextTools.CollapseWhitespace(publisher), 120),
        PublishedAt = published,
        Text = text,
        FetchedAt = at,
    };

    private static string ExtractText(IElement? root)
    {
        if (root is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        string? previous = null;
        foreach (var el in root.QuerySelectorAll(string.Join(',', TextSelectors)))
        {
            // pomijamy elementy zagnieżdżone w innych zbieranych (np. <p> w <li>), żeby nie dublować
            if (el.ParentElement?.Closest(string.Join(',', TextSelectors)) is not null)
            {
                continue;
            }

            var line = TextTools.CollapseWhitespace(el.TextContent);
            if (line.Length < 2 || line == previous)
            {
                continue;
            }

            sb.AppendLine(line);
            previous = line;
        }

        var text = sb.ToString().Trim();
        return text.Length >= 200 ? text : TextTools.CollapseWhitespace(root.TextContent);
    }

    private static string? Meta(IDocument doc, string property) =>
        (doc.QuerySelector($"meta[property='{property}']") ?? doc.QuerySelector($"meta[name='{property}']"))
        ?.GetAttribute("content") is { Length: > 0 } v ? v.Trim() : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static string DecodeText(byte[] bytes, string? charset)
    {
        try
        {
            return (charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset.Trim('"'))).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new ContentFetchException(FetchFailureKind.TooLarge, "Strona jest zbyt duża do analizy. Wklej fragment jako tekst.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static TException? FindInner<TException>(Exception ex) where TException : Exception
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e is TException t)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// Handler, który sam rozwiązuje DNS i łączy się tylko z publicznym IP.
    /// Sprawdzenie w momencie połączenia chroni też przed DNS rebinding i przekierowaniami do sieci lokalnej.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(FetchOptions opts) => new()
    {
        AllowAutoRedirect = opts.MaxRedirects > 0,
        MaxAutomaticRedirections = Math.Max(1, opts.MaxRedirects),
        AutomaticDecompression = DecompressionMethods.All,
        UseProxy = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(opts.TimeoutSeconds),
        ConnectCallback = async (context, ct) =>
        {
            if (context.DnsEndPoint.Port is not (80 or 443))
            {
                throw new ForbiddenAddressException("Port not allowed");
            }

            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var allowed = addresses.FirstOrDefault(AddressGuard.IsPublic)
                          ?? throw new ForbiddenAddressException("Address not public");

            var socket = new Socket(allowed.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(allowed, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}

public sealed class ForbiddenAddressException(string message) : Exception(message);
