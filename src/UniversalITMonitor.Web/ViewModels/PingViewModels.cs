using System.ComponentModel.DataAnnotations;

namespace UniversalITMonitor.Web.ViewModels;

/// <summary>Targets to probe during one live ping refresh.</summary>
public sealed record PingProbeRequest
{
    /// <summary>IP addresses or DNS names currently shown by the live ping page.</summary>
    [Required, MinLength(1), MaxLength(24)]
    public required string[] Targets { get; init; }

    /// <summary>Timeout for each ICMP request in milliseconds.</summary>
    [Range(250, 5000)]
    public int TimeoutMilliseconds { get; init; } = 1000;
}

/// <summary>ICMP results for a single target.</summary>
public sealed record PingProbeResponse(IReadOnlyList<PingTargetResponse> Results);

/// <summary>One raw ICMP reply or failure result.</summary>
public sealed record PingTargetResponse(
    string Target,
    bool IsReachable,
    long? ResponseTimeMilliseconds,
    int? TimeToLive,
    string Status,
    string? ResolvedAddress,
    int? ReplyBytes,
    string? Error);
