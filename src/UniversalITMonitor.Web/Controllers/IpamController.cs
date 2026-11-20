using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
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
            MatchedSubnets = matchedSubnets,
            TotalAssigned = allRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Assigned),
            TotalReserved = allRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Reserved),
            TotalConflict = allRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Conflict),
            TotalUnknown = allRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Unknown),
            SubnetSummaries = allSubnets.Select(subnet => BuildSubnetSummary(subnet, allRecords.Where(record => record.SubnetId == subnet.Id).ToArray())).ToArray()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Explore(Guid? id, int page = 1, int pageSize = 100, string? view = "grid", string? search = null, IpAssignmentStatus? status = null, CancellationToken cancellationToken = default)
    {
        if (id is null) return View("ExploreSelect", await ipam.GetSubnetsAsync(cancellationToken));
        var subnet = await ipam.GetSubnetAsync(id.Value, cancellationToken);
        if (subnet is null) return NotFound();
        page = Math.Max(1, page);
        pageSize = pageSize is 200 or 500 ? pageSize : 100;
        var records = (await ipam.GetRecordsAsync(cancellationToken)).Where(record => record.SubnetId == id.Value).ToArray();
        var monitoringStatuses = (await devices.GetAllAsync(cancellationToken)).ToDictionary(device => device.Id, device => device.Status);
        var summary = BuildSubnetSummary(subnet, records);
        var isIpv6 = IPAddress.TryParse(subnet.NetworkAddress, out var parsedNetwork) && parsedNetwork.AddressFamily == AddressFamily.InterNetworkV6;
        IEnumerable<IpamExplorerRow> matching;
        long total;
        if (isIpv6)
        {
            matching = records.OrderBy(record => record.IpAddress, StringComparer.OrdinalIgnoreCase)
                .Select(record => new IpamExplorerRow(record.IpAddress, record.AssignmentStatus, record));
            if (status is { } ipv6Status) matching = matching.Where(row => row.Status == ipv6Status);
            if (!string.IsNullOrWhiteSpace(search)) matching = matching.Where(row => MatchesSearch(row.Record, row.IpAddress, search));
            var rows = matching.Select(row => AddMonitoringStatus(row, monitoringStatuses)).ToArray(); total = rows.LongLength;
            return View("Explore", new IpamExplorerViewModel { Subnet = subnet, Rows = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), Page = page, PageSize = pageSize, Total = total, Assigned = summary.AssignedCount, Reserved = summary.ReservedCount, Available = summary.AvailableCount, Conflict = summary.ConflictCount, Unknown = summary.UnknownCount, View = "table", Search = search, Status = status, IsIpv6 = true, MonitoringStatuses = monitoringStatuses });
        }

        IpamAddressMath.TryGetIpv4UsableRange(subnet, out var usableStart, out var usableEnd);
        var usable = (Start: usableStart, End: usableEnd);
        var byIp = records.ToDictionary(record => record.IpAddress, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            IpamExplorerRow[] searchedRows;
            if (IpamService.TryNormalizeIpAddress(term, out var normalizedSearch) && IsInSubnet(normalizedSearch, subnet))
            {
                byIp.TryGetValue(normalizedSearch, out var searchedRecord);
                var searchedStatus = searchedRecord?.AssignmentStatus ?? IpAssignmentStatus.Available;
                searchedRows = status is null || status == searchedStatus ? [new(normalizedSearch, searchedStatus, searchedRecord)] : [];
            }
            else
            {
                searchedRows = records.Where(record => MatchesSearch(record, record.IpAddress, term))
                    .Where(record => status is null || record.AssignmentStatus == status)
                    .Select(record => new IpamExplorerRow(record.IpAddress, record.AssignmentStatus, record)).OrderBy(row => IpamAddressMath.ToIpv4Number(row.IpAddress)).ToArray();
            }
            searchedRows = searchedRows.Select(row => AddMonitoringStatus(row, monitoringStatuses)).ToArray();
            return View("Explore", new IpamExplorerViewModel { Subnet = subnet, Rows = searchedRows.Skip((page - 1) * pageSize).Take(pageSize).ToArray(), Page = page, PageSize = pageSize, Total = searchedRows.LongLength, Assigned = summary.AssignedCount, Reserved = summary.ReservedCount, Available = summary.AvailableCount, Conflict = summary.ConflictCount, Unknown = summary.UnknownCount, View = view == "table" ? "table" : "grid", Search = search, Status = status, MonitoringStatuses = monitoringStatuses });
        }
        if (status == IpAssignmentStatus.Available)
        {
            var availableRows = GetAvailablePage(usable.Start, usable.End, byIp, page, pageSize);
            matching = availableRows;
            total = summary.AvailableCount;
        }
        else if (status is { } selectedStatus)
        {
            matching = records.Where(record => record.AssignmentStatus == selectedStatus)
                .Select(record => new IpamExplorerRow(record.IpAddress, record.AssignmentStatus, record)).OrderBy(row => IpToNumber(row.IpAddress));
            if (!string.IsNullOrWhiteSpace(search)) matching = matching.Where(row => MatchesSearch(row.Record, row.IpAddress, search));
            var filtered = matching.ToArray(); total = filtered.LongLength;
            return View("Explore", new IpamExplorerViewModel { Subnet = subnet, Rows = filtered.Select(row => AddMonitoringStatus(row, monitoringStatuses)).Skip((page - 1) * pageSize).Take(pageSize).ToArray(), Page = page, PageSize = pageSize, Total = total, Assigned = summary.AssignedCount, Reserved = summary.ReservedCount, Available = summary.AvailableCount, Conflict = summary.ConflictCount, Unknown = summary.UnknownCount, View = view == "table" ? "table" : "grid", Search = search, Status = status, MonitoringStatuses = monitoringStatuses });
        }
        else
        {
            total = summary.UsableCount;
            var offset = (BigInteger)(page - 1) * pageSize;
            var start = usable.Start + offset;
            var end = BigInteger.Min(usable.End, start + pageSize - 1);
            var generated = new List<IpamExplorerRow>();
            for (var value = start; value <= end; value++)
            {
                var address = NumberToIp(value);
                byIp.TryGetValue(address, out var record);
                generated.Add(new IpamExplorerRow(address, record?.AssignmentStatus ?? IpAssignmentStatus.Available, record));
            }
            matching = generated;
        }

        var pageRows = matching.Select(row => AddMonitoringStatus(row, monitoringStatuses)).ToArray();
        if (!string.IsNullOrWhiteSpace(search)) pageRows = pageRows.Where(row => MatchesSearch(row.Record, row.IpAddress, search)).ToArray();
        return View("Explore", new IpamExplorerViewModel { Subnet = subnet, Rows = pageRows, Page = page, PageSize = pageSize, Total = total, Assigned = summary.AssignedCount, Reserved = summary.ReservedCount, Available = summary.AvailableCount, Conflict = summary.ConflictCount, Unknown = summary.UnknownCount, View = view == "table" ? "table" : "grid", Search = search, Status = status, MonitoringStatuses = monitoringStatuses });
    }

    [HttpGet]
    public async Task<IActionResult> Available(Guid? id, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default) => id is null
        ? View("AvailableSelect", await ipam.GetSubnetsAsync(cancellationToken))
        : RedirectToAction(nameof(Explore), new { id, page, pageSize, view = "table", status = IpAssignmentStatus.Available });

    [HttpGet]
    public async Task<IActionResult> Reservations(CancellationToken cancellationToken)
    {
        var records = (await ipam.GetRecordsAsync(cancellationToken)).Where(record => record.AssignmentStatus == IpAssignmentStatus.Reserved)
            .OrderBy(record => record.ReservedUntilUtc).ThenBy(record => record.IpAddress, StringComparer.OrdinalIgnoreCase).ToArray();
        return View(records);
    }

    [HttpGet]
    public async Task<IActionResult> History(string? search, string? action, CancellationToken cancellationToken)
    {
        var events = await ipam.GetHistoryAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(search)) events = events.Where(item => item.IpAddress.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || item.Actor?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true || item.Details?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (!string.IsNullOrWhiteSpace(action)) events = events.Where(item => string.Equals(item.Action, action, StringComparison.OrdinalIgnoreCase)).ToArray();
        return View(new IpamHistoryViewModel { Events = events.Take(1000).ToArray(), Search = search, Action = action });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Allocate(IpamRecord record, CancellationToken cancellationToken)
    {
        var action = record.AssignmentStatus switch
        {
            IpAssignmentStatus.Reserved => "Reserved",
            IpAssignmentStatus.Assigned => "Assigned or reservation converted",
            IpAssignmentStatus.Available => "Released",
            _ => "Allocation changed"
        };
        var result = await ipam.ChangeAllocationAsync(record, action, User.Identity?.Name, cancellationToken);
        TempData[result.Success ? "Success" : "Error"] = result.Success ? $"{record.IpAddress} was updated." : result.Error;
        return RedirectToAction(nameof(Explore), new { id = record.SubnetId, page = 1, view = "table" });
    }

    [HttpGet]
    [Authorize(Policy = "CanManageSettings")]
    public async Task<IActionResult> Discover(CancellationToken cancellationToken)
    {
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        var subnets = await ipam.GetSubnetsAsync(cancellationToken);
        return View(new DeviceDiscoveryViewModel
        {
            RegisteredSubnets = subnets.Select(s => new SubnetChoice(s.Cidr, s.Location ?? s.Building, s.Description, s.LocationId, s.BuildingId)).ToArray(),
            SubnetCidr = subnets.FirstOrDefault()?.Cidr,
            LocationChoices = hierarchy.Locations.Where(item => item.Kind != LocationKind.Building)
                .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id))).ToArray(),
            BuildingChoices = hierarchy.Locations.Where(item => item.Kind == LocationKind.Building)
                .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
                .Select(item => new HierarchyChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), GetLocationAncestorIds(hierarchy.Locations, item.ParentLocationId))).ToArray(),
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
        var locationId = linkedDevice?.LocationId ?? request.LocationId ?? subnet.LocationId;
        var buildingId = linkedDevice?.BuildingId ?? request.BuildingId;
        if (buildingId is null && (request.LocationId is null || request.LocationId == subnet.LocationId))
        {
            buildingId = subnet.BuildingId;
        }
        var record = new IpamRecord
        {
            Id = Guid.Empty, IpAddress = ip, SubnetId = subnet.Id, VlanId = request.VlanId ?? subnet.VlanId, VlanName = request.VlanName ?? subnet.VlanName,
            DeviceId = linkedDevice?.Id, DeviceName = linkedDevice?.Name ?? request.DeviceName?.Trim(), DeviceType = linkedDevice?.Type.ToString() ?? request.DeviceType,
            LocationId = locationId,
            BuildingId = buildingId, GroupId = linkedDevice?.GroupId ?? request.GroupId,
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

    private static IReadOnlyList<Guid> GetLocationAncestorIds(IReadOnlyCollection<ManagedLocation> locations, Guid? parentId)
    {
        var byId = locations.ToDictionary(item => item.Id);
        var ancestors = new List<Guid>();
        var seen = new HashSet<Guid>();
        while (parentId is { } id && seen.Add(id) && byId.TryGetValue(id, out var parent))
        {
            ancestors.Add(id);
            parentId = parent.ParentLocationId;
        }
        return ancestors;
    }

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
        csv.AppendLine("IpAddress,SubnetCidr,VlanId,VlanName,DeviceId,DeviceName,DeviceType,Location,Building,Group,SwitchName,SwitchIpAddress,SwitchPort,MacAddress,AssignedTo,Department,Description,AssignmentStatus,ReservationNote,ReservedUntilUtc");
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
                record.Description ?? string.Empty, record.AssignmentStatus.ToString(), record.ReservationNote ?? string.Empty,
                record.ReservedUntilUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
            };
            csv.AppendLine(string.Join(',', row.Select(EscapeCsv)));
            if (csv.Length > 20_000_000) return StatusCode(StatusCodes.Status413PayloadTooLarge, "The IPAM export exceeds the 20 MB limit. Apply a filter or split the export.");
        }
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"ipam-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    [HttpGet]
    public IActionResult Template()
    {
        const string template = "IpAddress,SubnetCidr,VlanId,VlanName,DeviceId,DeviceName,DeviceType,Location,Building,Group,SwitchName,SwitchIpAddress,SwitchPort,MacAddress,AssignedTo,Department,Description,AssignmentStatus,ReservationNote,ReservedUntilUtc\r\n";
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
        model.Record.UpdatedBy = User.Identity?.Name;
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

    private static bool MatchesSearch(IpamRecord? record, string ipAddress, string term) =>
        ipAddress.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        Has(record?.DeviceName, term) || Has(record?.DeviceType, term) || Has(record?.AssignedTo, term) ||
        Has(record?.Department, term) || Has(record?.MacAddress, term) || Has(record?.VlanName, term) ||
        Has(record?.SwitchName, term) || Has(record?.SwitchPort, term) || Has(record?.Location, term) ||
        Has(record?.Building, term) || Has(record?.Group, term) || record?.VlanId?.ToString(CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static IpamExplorerRow AddMonitoringStatus(IpamExplorerRow row, IReadOnlyDictionary<Guid, DeviceStatus> statuses) =>
        row.Record?.DeviceId is { } deviceId && statuses.TryGetValue(deviceId, out var status)
            ? row with { MonitoringStatus = status }
            : row;

    private static bool IsInSubnet(string ip, IpamSubnet subnet)
    {
        if (!IpamService.TryNormalizeCidr(subnet.Cidr, out var network, out var prefix, out _)) return false;
        var address = IPAddress.Parse(ip).GetAddressBytes();
        var networkBytes = IPAddress.Parse(network).GetAddressBytes();
        if (address.Length != networkBytes.Length) return false;
        for (var index = 0; index < address.Length; index++)
        {
            var bits = prefix - index * 8;
            var mask = bits >= 8 ? 255 : bits <= 0 ? 0 : 255 << (8 - bits) & 255;
            if ((address[index] & mask) != (networkBytes[index] & mask)) return false;
        }
        return true;
    }

    private static IpamSubnetSummaryRow BuildSubnetSummary(IpamSubnet subnet, IReadOnlyCollection<IpamRecord> records)
    {
        var assigned = records.LongCount(record => record.AssignmentStatus == IpAssignmentStatus.Assigned);
        var reserved = records.LongCount(record => record.AssignmentStatus == IpAssignmentStatus.Reserved);
        var conflict = records.LongCount(record => record.AssignmentStatus == IpAssignmentStatus.Conflict);
        var unknown = records.LongCount(record => record.AssignmentStatus == IpAssignmentStatus.Unknown);
        long usable = 0;
        if (IPAddress.TryParse(subnet.NetworkAddress, out var address) && address.AddressFamily == AddressFamily.InterNetwork && subnet.PrefixLength is >= 0 and <= 32)
        {
            var count = BigInteger.One << (32 - subnet.PrefixLength);
            if (subnet.PrefixLength <= 30) count -= 2;
            usable = (long)count;
        }
        var available = Math.Max(0, usable - assigned - reserved - conflict - unknown);
        return new(subnet, usable, assigned, reserved, available, conflict, unknown);
    }

    private static IReadOnlyList<IpamExplorerRow> GetAvailablePage(BigInteger start, BigInteger end, IReadOnlyDictionary<string, IpamRecord> records, int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        var take = new List<IpamExplorerRow>(pageSize);
        var blocked = records.Values.Where(record => record.AssignmentStatus != IpAssignmentStatus.Available)
            .Select(record => IpamAddressMath.ToIpv4Number(record.IpAddress)).Where(value => value >= start && value <= end).Order().ToArray();
        var cursor = start;
        foreach (var occupied in blocked.Append(end + 1))
        {
            var gapCount = (long)BigInteger.Max(BigInteger.Zero, occupied - cursor);
            if (skip >= gapCount) { skip -= gapCount; cursor = occupied + 1; continue; }
            var gapStart = cursor + skip;
            var remaining = gapCount - skip;
            skip = 0;
            var amount = (int)Math.Min(pageSize - take.Count, remaining);
            for (var index = 0; index < amount; index++)
            {
                var ip = IpamAddressMath.FromIpv4Number(gapStart + index);
                records.TryGetValue(ip, out var existingAvailableRecord);
                take.Add(new IpamExplorerRow(ip, IpAssignmentStatus.Available, existingAvailableRecord));
            }
            if (take.Count == pageSize) break;
            cursor = occupied + 1;
        }
        return take;
    }

    private static BigInteger IpToNumber(string ip) => IpamAddressMath.ToIpv4Number(ip);

    private static string NumberToIp(BigInteger value) => IpamAddressMath.FromIpv4Number(value);

    private static string EscapeCsv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
