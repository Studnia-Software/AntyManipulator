using System.Net;
using System.Net.Sockets;

namespace AntyManipulator.Integrations.Fetching;

/// <summary>Ochrona przed SSRF: blokuje adresy lokalne, prywatne i specjalne.</summary>
public static class AddressGuard
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast) || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                     || b[0] == 10
                     || b[0] == 127
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) // CGNAT
                     || (b[0] == 169 && b[1] == 254)               // link-local, metadata chmur
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                     || (b[0] == 192 && b[1] == 0 && b[2] == 2)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                     || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                     || (b[0] == 203 && b[1] == 0 && b[2] == 113)
                     || b[0] >= 224);                               // multicast i zarezerwowane
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(address.IsIPv6LinkLocal
                     || address.IsIPv6SiteLocal
                     || address.IsIPv6Multicast
                     || (b[0] & 0xFE) == 0xFC                       // fc00::/7 unique local
                     || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)); // dokumentacja
        }

        return false;
    }
}
