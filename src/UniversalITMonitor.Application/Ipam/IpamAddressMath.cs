using System.Numerics;
using System.Net;
using System.Net.Sockets;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Ipam;

public static class IpamAddressMath
{
    public static bool TryGetIpv4UsableRange(IpamSubnet subnet, out BigInteger start, out BigInteger end)
    {
        start = BigInteger.Zero;
        end = BigInteger.MinusOne;
        if (!IPAddress.TryParse(subnet.NetworkAddress, out var network) || network.AddressFamily != AddressFamily.InterNetwork || subnet.PrefixLength is < 0 or > 32)
            return false;
        start = ToIpv4Number(network.ToString());
        end = start + (BigInteger.One << (32 - subnet.PrefixLength)) - 1;
        if (subnet.PrefixLength <= 30) { start++; end--; }
        return true;
    }

    public static BigInteger ToIpv4Number(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("A valid IPv4 address is required.", nameof(ip));
        return new BigInteger(address.GetAddressBytes(), isUnsigned: true, isBigEndian: true);
    }

    public static string FromIpv4Number(BigInteger value)
    {
        if (value < BigInteger.Zero || value > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var bytes = new byte[4];
        raw.CopyTo(bytes, 4 - raw.Length);
        return new IPAddress(bytes).ToString();
    }
}
