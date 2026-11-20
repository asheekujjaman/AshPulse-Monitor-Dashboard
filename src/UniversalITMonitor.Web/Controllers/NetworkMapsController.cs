using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.NetworkMaps;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class NetworkMapsController(
    INetworkMapService maps,
    IDeviceService devices,
    IInventoryStructureService structure) : Controller
{
    private static readonly JsonSerializerOptions ExportOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new NetworkMapsIndexViewModel { Maps = await maps.GetAllAsync(cancellationToken) });

    [HttpGet]
    public async Task<IActionResult> Editor(Guid? id, CancellationToken cancellationToken)
    {
        if (id is { } mapId && await maps.GetAsync(mapId, cancellationToken) is null) return NotFound();
        return View(new NetworkMapEditorViewModel { MapId = id, Maps = await maps.GetAllAsync(cancellationToken) });
    }

    [HttpGet]
    public async Task<IActionResult> EditorData(Guid? id, CancellationToken cancellationToken)
    {
        var map = id is { } mapId ? await maps.GetAsync(mapId, cancellationToken) : new NetworkMap { Id = Guid.Empty };
        if (map is null) return NotFound();
        var allDevices = await devices.GetAllAsync(cancellationToken);
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        var mapped = allDevices.Select(device => new MapDeviceData
        {
            Id = device.Id,
            Name = device.Name,
            IpAddress = device.IpAddress,
            DeviceType = DeviceLabels.Type(device.Type),
            Status = GetDisplayStatus(device),
            ResponseTimeMilliseconds = device.ResponseTimeMilliseconds,
            LastCheckUtc = device.LastCheckUtc,
            CurrentStateSinceUtc = device.CurrentStateSinceUtc,
            MonitoringEnabled = device.MonitoringEnabled,
            Location = device.Location,
            Building = device.Building,
            Group = device.Group,
            LocationId = device.LocationId,
            BuildingId = device.BuildingId,
            GroupId = device.GroupId,
            DetailsUrl = Url.Action("Details", "Devices", new { id = device.Id }),
            LocationUrl = device.LocationId is { } locationId ? Url.Action("Details", "Locations", new { id = locationId }) : null,
            GroupUrl = device.GroupId is { } groupId ? Url.Action("Details", "Groups", new { id = groupId }) : null
        }).ToArray();
        return Json(new NetworkMapEditorData
        {
            Map = map,
            Devices = mapped,
            Locations = hierarchy.Locations
                .Where(location => location.Kind != LocationKind.Building)
                .OrderBy(location => InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
                .Select(location => new HierarchyChoice(location.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id)))
                .ToArray(),
            Buildings = hierarchy.Locations
                .Where(location => location.Kind == LocationKind.Building)
                .OrderBy(location => InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
                .Select(location => new HierarchyChoice(location.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id)))
                .ToArray(),
            Groups = hierarchy.Groups
                .OrderBy(group => InventoryStructureService.BuildGroupPath(hierarchy.Groups, group.Id), StringComparer.OrdinalIgnoreCase)
                .Select(group => new MapGroupChoice(group.Id, InventoryStructureService.BuildGroupPath(hierarchy.Groups, group.Id), group.ParentGroupId))
                .ToArray(),
            MapLocations = hierarchy.Locations
                .OrderBy(location => InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
                .Select(location => new MapLocationChoice(
                    location.Id,
                    InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id),
                    location.Kind.ToString(),
                    location.ParentLocationId))
                .ToArray()
        });
    }

    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LiveStatus(CancellationToken cancellationToken)
    {
        var allDevices = await devices.GetAllAsync(cancellationToken);
        return Json(allDevices.Select(device => new MapDeviceStatusData
        {
            Id = device.Id,
            Status = GetDisplayStatus(device),
            ResponseTimeMilliseconds = device.ResponseTimeMilliseconds,
            LastCheckUtc = device.LastCheckUtc,
            CurrentStateSinceUtc = device.CurrentStateSinceUtc,
            MonitoringEnabled = device.MonitoringEnabled
        }));
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    [RequestSizeLimit(2_200_000)]
    public async Task<IActionResult> Save([FromBody] NetworkMap map, CancellationToken cancellationToken)
    {
        if (map is null) return BadRequest(new MapSaveResponse(false, Error: "The request did not contain a network map."));
        var result = await maps.SaveAsync(map, cancellationToken);
        return Json(result.Result == MapWriteResult.Success
            ? new MapSaveResponse(true, result.MapId)
            : new MapSaveResponse(false, result.MapId, result.Error ?? "The map could not be saved.", result.MissingDeviceIds));
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await maps.DeleteAsync(id, cancellationToken)) return NotFound();
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var map = await maps.GetAsync(id, cancellationToken);
        if (map is null) return NotFound();
        var json = JsonSerializer.Serialize(map, ExportOptions);
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", $"network-map-{SafeFileName(map.Name)}.json");
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    [RequestSizeLimit(2_100_000)]
    public async Task<IActionResult> PreviewImport([FromBody] MapImportRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.Json is null) return BadRequest(new MapImportResponse(false, true, "Choose a map JSON file."));
        var preview = await maps.PreviewImportAsync(request.Json, cancellationToken);
        return Json(new MapImportResponse(
            preview.Map is not null && preview.Error is null && preview.MissingDeviceIds.Count == 0,
            true,
            preview.Error ?? (preview.MissingDeviceIds.Count > 0 ? "Resolve missing device references before importing this map." : null),
            preview.MissingDeviceIds,
            MapName: preview.Map?.Name));
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    [RequestSizeLimit(2_100_000)]
    public async Task<IActionResult> Import([FromBody] MapImportRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.Json is null) return BadRequest(new MapImportResponse(false, false, "Choose a map JSON file."));
        var preview = await maps.PreviewImportAsync(request.Json, cancellationToken);
        if (preview.Map is null || preview.Error is not null || preview.MissingDeviceIds.Count > 0)
        {
            return Json(new MapImportResponse(false, false, preview.Error ?? "Resolve missing device references before importing this map.", preview.MissingDeviceIds, MapName: preview.Map?.Name));
        }

        var result = await maps.ImportAsync(preview.Map, request.Name, cancellationToken);
        return Json(result.Result == MapWriteResult.Success
            ? new MapImportResponse(true, false, MapId: result.MapId, MapName: preview.Map.Name)
            : new MapImportResponse(false, false, result.Error ?? "The map could not be imported.", result.MissingDeviceIds, MapName: preview.Map.Name));
    }

    private static string GetDisplayStatus(DeviceInfo device)
    {
        if (device.Status == DeviceStatus.Up && device.MonitoringMethod == MonitoringMethod.IcmpAndSnmp &&
            !string.IsNullOrWhiteSpace(device.LatestSnmpMetrics?.CollectionError))
        {
            return "Warning";
        }

        return device.Status.ToString();
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Where(character => !invalid.Contains(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "map" : safe;
    }
}
