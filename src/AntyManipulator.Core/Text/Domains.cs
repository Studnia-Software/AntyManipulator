namespace AntyManipulator.Core.Text;

public static class Domains
{
    /// <summary>Host bez "www.", małymi literami, albo null dla niepoprawnego adresu.</summary>
    public static string? HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>Czy host to podana domena albo jej subdomena (np. "zdrowie.gov.pl" pasuje do "gov.pl").</summary>
    public static bool Matches(string? host, string domain)
    {
        if (string.IsNullOrEmpty(host) || string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }

        var d = domain.Trim().ToLowerInvariant();
        if (d.StartsWith("www.", StringComparison.Ordinal))
        {
            d = d[4..];
        }

        return host == d || host.EndsWith("." + d, StringComparison.Ordinal);
    }
}
