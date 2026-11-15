using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.NetworkMaps;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class SearchController(
    IDeviceService devices,
    IIpamService ipam,
    IInventoryStructureService structure,
    INetworkMapService maps) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        var query = q?.Trim() ?? string.Empty;
        if (query.Length > 120) query = query[..120];
        var deviceTask = devices.GetAllAsync(cancellationToken);
        var recordTask = ipam.GetRecordsAsync(cancellationToken);
        var subnetTask = ipam.GetSubnetsAsync(cancellationToken);
        var locationTask = structure.GetLocationSummariesAsync(cancellationToken);
        var groupTask = structure.GetGroupSummariesAsync(cancellationToken);
        var mapTask = maps.GetAllAsync(cancellationToken);
        await Task.WhenAll(deviceTask, recordTask, subnetTask, locationTask, groupTask, mapTask);
        var allDevices = await deviceTask;
        var currentDevices = allDevices.ToDictionary(device => device.Id);
        var records = await recordTask;
        var subnets = await subnetTask;
        var locations = await locationTask;
        var groups = await groupTask;
        var allMaps = await mapTask;
        bool Has(string? value) => !string.IsNullOrEmpty(query) && value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
        return View(new GlobalSearchViewModel
        {
            Query = query,
            Devices = allDevices.Where(device => Has(device.Name) || Has(device.IpAddress) || Has(device.Location) || Has(device.Building) || Has(device.Group) || Has(device.Description))
                .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase).Take(100).ToArray(),
            IpRecords = records.Where(record => Has(record.IpAddress) || Has(record.DeviceName) || Has(record.VlanName) ||
                    record.VlanId?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) == true || Has(record.SwitchName) || Has(record.SwitchIpAddress) || Has(record.SwitchPort) || Has(record.Location) || Has(record.Building) || Has(record.Group))
                .OrderBy(record => record.IpAddress, StringComparer.OrdinalIgnoreCase).Take(100)
                .Select(record => new IpamSearchResult
                {
                    Record = record,
                    CurrentStatus = record.DeviceId is { } id && currentDevices.TryGetValue(id, out var device) ? device.Status : null
                }).ToArray(),
            Subnets = subnets.Where(subnet => Has(subnet.Cidr) || Has(subnet.VlanName) || Has(subnet.VlanId?.ToString()) || Has(subnet.Location) || Has(subnet.Building))
                .OrderBy(subnet => subnet.Cidr, StringComparer.OrdinalIgnoreCase).Take(100).ToArray(),
            Locations = locations.Where(item => Has(item.Path)).Take(100).ToArray(),
            Groups = groups.Where(item => Has(item.Path)).Take(100).ToArray(),
            Maps = allMaps.Where(map => Has(map.Name)).OrderBy(map => map.Name, StringComparer.OrdinalIgnoreCase).Take(100).ToArray()
        });
    }
}
