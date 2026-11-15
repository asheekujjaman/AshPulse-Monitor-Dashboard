using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UniversalITMonitor.Core.Monitoring;

namespace UniversalITMonitor.Monitoring;

public sealed class IcmpProbe : IIcmpProbe
{
    public async Task<IcmpProbeResult> CheckAsync(
        string ipAddress,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        IPAddress address;
        if (!IPAddress.TryParse(ipAddress, out address!))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(ipAddress.Trim(), cancellationToken);
                address = addresses.OrderByDescending(item => item.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault()!;
                if (address is null)
                    return new IcmpProbeResult(false, null, "The host name did not resolve to an IP address.", Status: "NameResolutionFailure");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (SocketException)
            {
                return new IcmpProbeResult(false, null, "The host name could not be resolved.", Status: "NameResolutionFailure");
            }
            catch (ArgumentException)
            {
                return new IcmpProbeResult(false, null, "The host name could not be resolved.", Status: "NameResolutionFailure");
            }
        }

        using var ping = new Ping();
        try
        {
            PingOptions? options = address.AddressFamily == AddressFamily.InterNetwork ? new PingOptions(64, true) : null;
            var reply = await ping.SendPingAsync(
                address,
                TimeSpan.FromMilliseconds(timeoutMilliseconds),
                new byte[32],
                options,
                cancellationToken);

            var reachable = reply.Status == IPStatus.Success;
            return new IcmpProbeResult(
                reachable,
                reachable ? reply.RoundtripTime : null,
                reachable ? null : $"ICMP check returned {reply.Status}.",
                reply.Options?.Ttl,
                reply.Status.ToString(),
                reply.Address?.ToString() ?? address.ToString(),
                reply.Buffer.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PingException)
        {
            return new IcmpProbeResult(false, null, "The ICMP request could not be completed.", Status: "PingError", ResolvedAddress: address.ToString());
        }
        catch (TimeoutException)
        {
            return new IcmpProbeResult(false, null, "The device did not respond before the check timed out.", Status: "TimedOut", ResolvedAddress: address.ToString());
        }
    }
}
