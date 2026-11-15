using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Core.Monitoring;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class PingController(IIcmpProbe icmp) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>Checks up to 24 IP or DNS targets with ICMP.</summary>
    [HttpPost]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Probe([FromBody] PingProbeRequest request, CancellationToken cancellationToken)
    {
        if (request?.Targets is not { Length: > 0 and <= 24 })
            return BadRequest(new { error = "Add between 1 and 24 IP addresses or DNS names." });

        var targets = request.Targets.Select(target => target?.Trim() ?? string.Empty).ToArray();
        if (targets.Any(target => target.Length is 0 or > 253))
            return BadRequest(new { error = "Each target must contain 1 to 253 characters." });

        var timeout = Math.Clamp(request.TimeoutMilliseconds, 250, 5000);
        var results = new ConcurrentBag<(int Index, PingTargetResponse Result)>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, targets.Length),
            new ParallelOptions { MaxDegreeOfParallelism = 24, CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                var target = targets[index];
                var result = await icmp.CheckAsync(target, timeout, ct);
                results.Add((index, new PingTargetResponse(
                    target,
                    result.IsReachable,
                    result.ResponseTimeMilliseconds,
                    result.TimeToLive,
                    result.Status ?? (result.IsReachable ? "Success" : "Unknown"),
                    result.ResolvedAddress,
                    result.ReplyBytes,
                    result.Error)));
            });

        return Ok(new PingProbeResponse(results.OrderBy(item => item.Index).Select(item => item.Result).ToArray()));
    }
}
