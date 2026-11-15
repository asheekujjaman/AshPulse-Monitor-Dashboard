namespace UniversalITMonitor.Core.Monitoring;

public interface IIcmpProbe
{
    Task<IcmpProbeResult> CheckAsync(string ipAddress, int timeoutMilliseconds, CancellationToken cancellationToken = default);
}

public sealed record IcmpProbeResult(bool IsReachable, long? ResponseTimeMilliseconds, string? Error);
