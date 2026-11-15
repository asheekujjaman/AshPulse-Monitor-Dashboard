using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Monitoring;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class IpamController(
    IIpamService ipam,
    IDeviceService devices,
    IInventoryStructureService structure,
    IIcmpProbe icmp) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        IpAssignmentStatus? status,
        CancellationToken cancellationToken)
    {
        var allRecords = await ipam.GetRecordsAsync(cancellationToken);
        var currentDevices = (await devices.GetAllAsync(cancellationToken)).ToDictionary(device => device.Id);
        var filtered = allRecords.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(record =>
                Has(record.IpAddress, term) || Has(record.DeviceName, term) || Has(record.VlanName, term) ||
                record.VlanId?.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase) == true ||
                Has(record.SwitchName, term) || Has(record.SwitchIpAddress, term) || Has(record.SwitchPort, term) ||
                Has(record.Location, term) || Has(record.Building, term) || Has(record.Group, term) || Has(record.MacAddress, term));
        }
        if (status is not null) filtered = filtered.Where(record => record.AssignmentStatus == status);

        var allSubnets = await ipam.GetSubnetsAsync(cancellationToken);
        var matchedSubnets = string.IsNullOrWhiteSpace(search)
            ? Array.Empty<IpamSubnet>()
            : allSubnets.Where(subnet => Has(subnet.Cidr, search) || Has(subnet.VlanName, search) ||
                subnet.VlanId?.ToString(CultureInfo.InvariantCulture).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true ||
                Has(subnet.Location, search) || Has(subnet.Building, search)).ToArray();
        return View(new IpamIndexViewModel
        {
            Search = search,
            Status = status,
            Records = filtered.OrderBy(record => record.IpAddress, StringComparer.OrdinalIgnoreCase).Select(record => new IpamRecordRow
            {
                Record = record,
                CurrentStatus = record.DeviceId is { } deviceId && currentDevices.TryGetValue(deviceId, out var device) ? device.Status : null,
                ResponseTimeMilliseconds = record.DeviceId is { } responseId && currentDevices.TryGetValue(responseId, out var responseDevice) ? responseDevice.ResponseTimeMilliseconds : null,
                LastCheckUtc = record.DeviceId is { } checkId && currentDevices.TryGetValue(checkId, out var checkDevice) ? checkDevice.LastCheckUtc : null
            }).ToArray(),
            Subnets = allSubnets,
            MatchedSubnets = matchedSubnets
        });
    }

    [HttpGet]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Discover(CancellationToken cancellationToken)
    {
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        var subnets = await ipam.GetSubnetsAsync(cancellationToken);
        return View(new DeviceDiscoveryViewModel
        {
            RegisteredSubnets = subnets.Select(s => new SubnetChoice(s.Cidr, s.Location ?? s.Building, s.Description)).ToArray(),
            SubnetCidr = subnets.FirstOrDefault()?.Cidr,
            LocationChoices = hierarchy.Locations.Where(item => item.Kind != LocationKind.Building)
                .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray(),
            BuildingChoices = hierarchy.Locations.Where(item => item.Kind == LocationKind.Building)
                .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray(),
            GroupChoices = hierarchy.Groups.OrderBy(item => InventoryStructureService.BuildGroupPath(hierarchy.Groups, item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildGroupPath(hierarchy.Groups, item.Id))).ToArray()
        });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> ScanSubnet([FromBody] SubnetScanRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !IpamService.TryNormalizeCidr(request.Cidr, out var network, out var prefix, out var canonical) ||
            !IPAddress.TryParse(network, out var networkIp) || networkIp.AddressFamily != AddressFamily.InterNetwork || prefix is < 22 or > 32)
            return Json(new SubnetScanResponse { Success = false, Error = "Choose a valid IPv4 subnet from /22 through /32 (max 1,024 hosts)." });

        var registered = await ipam.GetSubnetsAsync(cancellationToken);
        var selectedSubnet = registered.FirstOrDefault(item => string.Equals(item.Cidr, canonical, StringComparison.OrdinalIgnoreCase));
        if (selectedSubnet is null) return Json(new SubnetScanResponse { Success = false, Error = "Select a subnet registered in IPAM before scanning." });

        var hosts = GenerateIpv4Hosts(networkIp, prefix);
        var existingRecords = (await ipam.GetRecordsAsync(cancellationToken)).ToDictionary(record => record.IpAddress, StringComparer.OrdinalIgnoreCase);
        var existingDevices = (await devices.GetAllAsync(cancellationToken)).ToDictionary(device => device.IpAddress, StringComparer.OrdinalIgnoreCase);
        var timeout = Math.Clamp(request.TimeoutMilliseconds, 150, 3000);
        var responsiveByIp = new ConcurrentDictionary<string, IcmpProbeResult>(StringComparer.OrdinalIgnoreCase);
        await ScanAsync(hosts, timeout, 64);
        var missed = hosts.Where(address => !responsiveByIp.ContainsKey(address.ToString())).ToArray();
        if (missed.Length > 0)
            await ScanAsync(missed, Math.Clamp(Math.Max(timeout * 2, 1000), 1000, 3000), 32);

        var responsive = new ConcurrentBag<DiscoveredDeviceItem>();
        await Parallel.ForEachAsync(responsiveByIp, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = cancellationToken }, async (entry, ct) =>
        {
            string? hostName = null;
            try
            {
                using var dns = CancellationTokenSource.CreateLinkedTokenSource(ct);
                dns.CancelAfter(TimeSpan.FromMilliseconds(450));
                hostName = (await Dns.GetHostEntryAsync(entry.Key, dns.Token)).HostName;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch { }
            var name = string.IsNullOrWhiteSpace(hostName) ? $"Device-{entry.Key}" : hostName;
            var existingDevice = existingDevices.GetValueOrDefault(entry.Key);
            responsive.Add(new DiscoveredDeviceItem
            {
                IpAddress = entry.Key, ResponseTimeMilliseconds = entry.Value.ResponseTimeMilliseconds ?? 0, Hostname = hostName,
                SuggestedName = existingDevice?.Name ?? name, SuggestedType = existingDevice?.Type.ToString() ?? GuessDiscoveredType(name).ToString(),
                AlreadyInInventory = existingRecords.ContainsKey(entry.Key), ExistingDeviceId = existingDevice?.Id,
                ExistingIpamRecordId = existingRecords.GetValueOrDefault(entry.Key)?.Id
            });
        });
        var sorted = responsive.OrderBy(item => IPAddress.Parse(item.IpAddress).GetAddressBytes(), ByteArrayComparer.Instance).ToList();
        return Json(new SubnetScanResponse { Success = true, Cidr = canonical, TotalScanned = hosts.Count, ResponsiveCount = sorted.Count, Devices = sorted });

        async Task ScanAsync(IEnumerable<IPAddress> addresses, int probeTimeout, int concurrency)
        {
            await Parallel.ForEachAsync(addresses,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
                async (address, ct) =>
                {
                    var ip = address.ToString();
                    var result = await icmp.CheckAsync(ip, probeTimeout, ct);
                    if (result.IsReachable) responsiveByIp.TryAdd(ip, result);
                });
        }
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> AddDiscovered([FromBody] IpamDiscoveredAddRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IpAddress)) return Json(new AddDiscoveredResult { Error = "IP address is required." });
        var result = await SaveDiscoveredRecordAsync(request, cancellationToken);
        return Json(result);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> BatchAddDiscovered([FromBody] IpamDiscoveredBatchRequest request, CancellationToken cancellationToken)
    {
        if (request?.Devices is not { Count: > 0 } || request.Devices.Count > 1024) return Json(new AddDiscoveredResult { Error = "Select between 1 and 1,024 IP addresses." });
        var records = await ipam.GetRecordsAsync(cancellationToken);
        var existingIps = records.Select(record => record.IpAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedIps = new List<string>(); var addedRecords = new List<AddedIpamRecord>(); var skippedIps = new List<string>(); var errors = new List<string>();
        foreach (var device in request.Devices)
        {
            if (existingIps.Contains(device.IpAddress)) { skippedIps.Add(device.IpAddress); continue; }
            var result = await SaveDiscoveredRecordAsync(new IpamDiscoveredAddRequest
            {
                IpAddress = device.IpAddress, DeviceName = device.Name, DeviceType = string.IsNullOrWhiteSpace(request.DeviceType) ? device.DeviceType : request.DeviceType,
                SubnetCidr = request.SubnetCidr, AssignmentStatus = request.AssignmentStatus, VlanId = request.VlanId,
                VlanName = request.VlanName, LocationId = request.LocationId, BuildingId = request.BuildingId,
                GroupId = request.GroupId, Description = request.Description, SwitchName = request.SwitchName,
                SwitchIpAddress = request.SwitchIpAddress, SwitchPort = request.SwitchPort,
                MacAddress = request.MacAddress, AssignedTo = request.AssignedTo, Department = request.Department
            }, cancellationToken);
            if (result.Success) { addedIps.Add(device.IpAddress); addedRecords.AddRange(result.AddedRecords); existingIps.Add(device.IpAddress); }
            else errors.Add($"{device.IpAddress}: {result.Error}");
        }
        return Json(new AddDiscoveredResult
        {
            Success = addedIps.Count > 0 || (errors.Count == 0 && skippedIps.Count > 0), AddedCount = addedIps.Count,
            AddedIpAddresses = addedIps, AddedRecords = addedRecords, SkippedCount = skippedIps.Count, SkippedIpAddresses = skippedIps,
            Message = $"Added {addedIps.Count} IP record(s)." + (skippedIps.Count > 0 ? $" {skippedIps.Count} already existed." : string.Empty),
            Error = errors.Count > 0 ? string.Join("; ", errors.Take(4)) : null
        });
    }

    private async Task<AddDiscoveredResult> SaveDiscoveredRecordAsync(IpamDiscoveredAddRequest request, CancellationToken cancellationToken)
    {
        if (!IpamService.TryNormalizeIpAddress(request.IpAddress, out var ip)) return new AddDiscoveredResult { Error = "Enter a valid IP address." };
        var subnet = (await ipam.GetSubnetsAsync(cancellationToken)).FirstOrDefault(item => string.Equals(item.Cidr, request.SubnetCidr, StringComparison.OrdinalIgnoreCase));
        if (subnet is null) return new AddDiscoveredResult { Error = "The selected subnet is no longer registered." };
        var linkedDevice = (await devices.GetAllAsync(cancellationToken)).FirstOrDefault(item => string.Equals(item.IpAddress, ip, StringComparison.OrdinalIgnoreCase));
        var record = new IpamRecord
        {
            Id = Guid.NewGuid(), IpAddress = ip, SubnetId = subnet.Id, VlanId = request.VlanId ?? subnet.VlanId, VlanName = request.VlanName ?? subnet.VlanName,
            DeviceId = linkedDevice?.Id, DeviceName = linkedDevice?.Name ?? request.DeviceName?.Trim(), DeviceType = linkedDevice?.Type.ToString() ?? request.DeviceType,
            LocationId = linkedDevice?.LocationId ?? request.LocationId ?? subnet.LocationId,
            BuildingId = linkedDevice?.BuildingId ?? request.BuildingId ?? subnet.BuildingId, GroupId = linkedDevice?.GroupId ?? request.GroupId,
            SwitchName = request.SwitchName?.Trim(), SwitchIpAddress = request.SwitchIpAddress?.Trim(), SwitchPort = request.SwitchPort?.Trim(),
            MacAddress = request.MacAddress?.Trim(), AssignedTo = request.AssignedTo?.Trim(), Department = request.Department?.Trim(),
            Description = request.Description?.Trim(), AssignmentStatus = request.AssignmentStatus,
            Location = linkedDevice?.Location ?? subnet.Location, Building = linkedDevice?.Building ?? subnet.Building, Group = linkedDevice?.Group
        };
        var save = await ipam.SaveRecordAsync(record, cancellationToken);
        return save.Success
            ? new AddDiscoveredResult { Success = true, AddedCount = 1, AddedIpAddresses = [ip], AddedRecords = [new AddedIpamRecord(ip, record.Id)], Message = $"{ip} was added to IPAM." }
            : new AddDiscoveredResult { Error = save.Error ?? "Could not add IP record." };
    }

    private static List<IPAddress> GenerateIpv4Hosts(IPAddress networkAddress, int prefixLength)
    {
        var bytes = networkAddress.GetAddressBytes();
        var start = (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
        var count = 1u << (32 - prefixLength);
        var first = prefixLength <= 30 ? start + 1 : start;
        var last = prefixLength <= 30 ? start + count - 2 : start + count - 1;
        var result = new List<IPAddress>();
        for (var current = first; current <= last && result.Count < 1024; current++)
        {
            var host = BitConverter.GetBytes(current); if (BitConverter.IsLittleEndian) Array.Reverse(host); result.Add(new IPAddress(host));
        }
        return result;
    }

    private static DeviceType GuessDiscoveredType(string name) => name.Contains("router", StringComparison.OrdinalIgnoreCase) || name.Contains("gateway", StringComparison.OrdinalIgnoreCase) ? DeviceType.Router :
        name.Contains("switch", StringComparison.OrdinalIgnoreCase) || name.Contains("sw-", StringComparison.OrdinalIgnoreCase) ? DeviceType.AccessSwitch :
        name.Contains("firewall", StringComparison.OrdinalIgnoreCase) ? DeviceType.Firewall :
        name.Contains("server", StringComparison.OrdinalIgnoreCase) || name.Contains("srv", StringComparison.OrdinalIgnoreCase) ? DeviceType.Server : DeviceType.Other;

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public int Compare(byte[]? x, byte[]? y) { if (x is null || y is null) return 0; for (var i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]); return x.Length.CompareTo(y.Length); }
    }

    [HttpGet]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new IpamRecordEditViewModel();
        await PopulateRecordChoicesAsync(model, cancellationToken);
        return View("Edit", model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Create(IpamRecordEditViewModel model, CancellationToken cancellationToken) =>
        await SaveRecordAsync(model, create: true, cancellationToken);

    [HttpGet]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var record = await ipam.GetRecordAsync(id, cancellationToken);
        if (record is null) return NotFound();
        var model = new IpamRecordEditViewModel { Record = record };
        await PopulateRecordChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Edit(IpamRecordEditViewModel model, CancellationToken cancellationToken) =>
        await SaveRecordAsync(model, create: false, cancellationToken);

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await ipam.DeleteRecordAsync(id, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "The IP record was deleted." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Subnets(CancellationToken cancellationToken) =>
        View(await BuildSubnetsModelAsync(cancellationToken));

    [HttpGet]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> CreateSubnet(CancellationToken cancellationToken)
    {
        var model = new IpamSubnetEditViewModel();
        await PopulateSubnetChoicesAsync(model, cancellationToken);
        return View("SubnetEdit", model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> CreateSubnet(IpamSubnetEditViewModel model, CancellationToken cancellationToken) =>
        await SaveSubnetAsync(model, create: true, cancellationToken);

    [HttpGet]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> EditSubnet(Guid id, CancellationToken cancellationToken)
    {
        var subnet = await ipam.GetSubnetAsync(id, cancellationToken);
        if (subnet is null) return NotFound();
        var model = new IpamSubnetEditViewModel { Subnet = subnet };
        await PopulateSubnetChoicesAsync(model, cancellationToken);
        return View("SubnetEdit", model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> EditSubnet(IpamSubnetEditViewModel model, CancellationToken cancellationToken) =>
        await SaveSubnetAsync(model, create: false, cancellationToken);

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> DeleteSubnet(Guid id, CancellationToken cancellationToken)
    {
        var result = await ipam.DeleteSubnetAsync(id, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? "The subnet was deleted." : result.Error;
        return RedirectToAction(nameof(Subnets));
    }

    [HttpGet]
    [Authorize(Policy = "CanManageSettings")]
    public IActionResult Import() => View(new IpamImportViewModel());

    [HttpGet]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var records = await ipam.GetRecordsAsync(cancellationToken);
        if (records.Count > 25_000) return StatusCode(StatusCodes.Status413PayloadTooLarge, "The IPAM export exceeds the 25,000 row limit. Apply a filter or split the export.");
        var csv = new StringBuilder();
        csv.AppendLine("IpAddress,SubnetCidr,VlanId,VlanName,DeviceId,DeviceName,DeviceType,Location,Building,Group,SwitchName,SwitchIpAddress,SwitchPort,MacAddress,AssignedTo,Department,Description,AssignmentStatus");
        var subnets = (await ipam.GetSubnetsAsync(cancellationToken)).ToDictionary(subnet => subnet.Id);
        foreach (var record in records)
        {
            var row = new[]
            {
                record.IpAddress, record.SubnetId is { } subnetId && subnets.TryGetValue(subnetId, out var subnet) ? subnet.Cidr : string.Empty,
                record.VlanId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, record.VlanName ?? string.Empty,
                record.DeviceId?.ToString("D") ?? string.Empty, record.DeviceName ?? string.Empty, record.DeviceType ?? string.Empty,
                record.Location ?? string.Empty, record.Building ?? string.Empty, record.Group ?? string.Empty,
                record.SwitchName ?? string.Empty, record.SwitchIpAddress ?? string.Empty, record.SwitchPort ?? string.Empty,
                record.MacAddress ?? string.Empty, record.AssignedTo ?? string.Empty, record.Department ?? string.Empty,
                record.Description ?? string.Empty, record.AssignmentStatus.ToString()
            };
            csv.AppendLine(string.Join(',', row.Select(EscapeCsv)));
            if (csv.Length > 20_000_000) return StatusCode(StatusCodes.Status413PayloadTooLarge, "The IPAM export exceeds the 20 MB limit. Apply a filter or split the export.");
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"ipam-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    [HttpGet]
    public IActionResult Template()
    {
        const string template = "IpAddress,SubnetCidr,VlanId,VlanName,DeviceId,DeviceName,DeviceType,Location,Building,Group,SwitchName,SwitchIpAddress,SwitchPort,MacAddress,AssignedTo,Department,Description,AssignmentStatus\r\n";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(template)).ToArray(), "text/csv; charset=utf-8", "ipam-template.csv");
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    [RequestSizeLimit(5_250_000)]
    public async Task<IActionResult> PreviewImport([FromBody] IpamCsvRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.Csv is null || request.Mapping is null) return BadRequest(new { error = "Choose a CSV file and column mapping." });
        var preview = await ipam.PreviewCsvImportAsync(request.Csv, request.Mapping, cancellationToken);
        return Json(new IpamImportViewModel
        {
            Headers = preview.Headers,
            Rows = preview.Rows,
            Error = preview.Error,
            ValidCount = preview.ValidCount,
            InvalidCount = preview.InvalidCount,
            DuplicateCount = preview.DuplicateCount
        });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    [RequestSizeLimit(5_250_000)]
    public async Task<IActionResult> CommitImport([FromBody] IpamCsvRequest request, CancellationToken cancellationToken) =>
        request is null || request.Csv is null || request.Mapping is null
            ? BadRequest(new { error = "Choose a CSV file and column mapping." })
            : Json(await ipam.ImportCsvAsync(request.Csv, request.Mapping, cancellationToken));

    private async Task<IActionResult> SaveRecordAsync(IpamRecordEditViewModel model, bool create, CancellationToken cancellationToken)
    {
        if (create) model.Record.Id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(model.Record.IpAddress)) ModelState.AddModelError("Record.IpAddress", "IP address is required.");
        if (!ModelState.IsValid)
        {
            await PopulateRecordChoicesAsync(model, cancellationToken);
            return View("Edit", model);
        }
        var result = await ipam.SaveRecordAsync(model.Record, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The IP record could not be saved.");
            await PopulateRecordChoicesAsync(model, cancellationToken);
            return View("Edit", model);
        }
        TempData["Success"] = create ? "IP record added." : "IP record updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> SaveSubnetAsync(IpamSubnetEditViewModel model, bool create, CancellationToken cancellationToken)
    {
        if (create) model.Subnet.Id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(model.Subnet.Cidr)) ModelState.AddModelError("Subnet.Cidr", "CIDR is required.");
        if (!ModelState.IsValid)
        {
            await PopulateSubnetChoicesAsync(model, cancellationToken);
            return View("SubnetEdit", model);
        }
        var result = await ipam.SaveSubnetAsync(model.Subnet, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "The subnet could not be saved.");
            await PopulateSubnetChoicesAsync(model, cancellationToken);
            return View("SubnetEdit", model);
        }
        TempData["Success"] = create ? "Subnet registered." : "Subnet updated.";
        return RedirectToAction(nameof(Subnets));
    }

    private async Task PopulateRecordChoicesAsync(IpamRecordEditViewModel model, CancellationToken cancellationToken)
    {
        model.Devices = await devices.GetAllAsync(cancellationToken);
        model.Subnets = await ipam.GetSubnetsAsync(cancellationToken);
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        model.Locations = hierarchy.Locations.Where(item => item.Kind != LocationKind.Building)
            .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray();
        model.Buildings = hierarchy.Locations.Where(item => item.Kind == LocationKind.Building)
            .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray();
        model.Groups = hierarchy.Groups.OrderBy(item => InventoryStructureService.BuildGroupPath(hierarchy.Groups, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildGroupPath(hierarchy.Groups, item.Id))).ToArray();
    }

    private async Task PopulateSubnetChoicesAsync(IpamSubnetEditViewModel model, CancellationToken cancellationToken)
    {
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        model.Locations = hierarchy.Locations.Where(item => item.Kind != LocationKind.Building)
            .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray();
        model.Buildings = hierarchy.Locations.Where(item => item.Kind == LocationKind.Building)
            .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray();
    }

    private async Task<IpamSubnetsViewModel> BuildSubnetsModelAsync(CancellationToken cancellationToken)
    {
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        return new IpamSubnetsViewModel
        {
            Subnets = await ipam.GetSubnetsAsync(cancellationToken),
            Records = await ipam.GetRecordsAsync(cancellationToken),
            Locations = hierarchy.Locations.Where(item => item.Kind != LocationKind.Building)
                .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray(),
            Buildings = hierarchy.Locations.Where(item => item.Kind == LocationKind.Building)
                .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray()
        };
    }

    private static bool Has(string? value, string term) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static string EscapeCsv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
