using System.Net;
using System.Net.NetworkInformation;
using UniversalITMonitor.Core.Monitoring;

namespace UniversalITMonitor.Monitoring;

public sealed class IcmpProbe : IIcmpProbe
{
    public async Task<IcmpProbeResult> CheckAsync(
        string ipAddress,
        int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ipAddress, out var address))
        {
            return new IcmpProbeResult(false, null, "The stored IP address is invalid.");
        }

        using var ping = new Ping();
        try
        {
            var options = new PingOptions(64, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var reply = await ping.SendPingAsync(
                address,
                TimeSpan.FromMilliseconds(timeoutMilliseconds),
                new byte[32],
                options,
                cancellationToken);

            return reply.Status == IPStatus.Success
                ? new IcmpProbeResult(true, reply.RoundtripTime, null)
                : new IcmpProbeResult(false, null, $"ICMP check returned {reply.Status}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PingException)
        {
            return new IcmpProbeResult(false, null, "The ICMP request could not be completed.");
        }
        catch (TimeoutException)
        {
            return new IcmpProbeResult(false, null, "The device did not respond before the check timed out.");
        }
    }
}
