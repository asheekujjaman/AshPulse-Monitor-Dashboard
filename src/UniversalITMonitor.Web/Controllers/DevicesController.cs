using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Monitoring;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

[Authorize]
public sealed class DevicesController(
    IDeviceService devices,
    IInventoryStructureService structure,
    IIcmpProbe icmp,
    IIpamService? ipam = null,
    ISnmpMetricsCollector? snmpCollector = null,
    ISnmpMetricsStore? snmpMetricsStore = null) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        DeviceStatus? status,
        DeviceType? type,
        string? location,
        string? group,
        bool? monitoringEnabled,
        CancellationToken cancellationToken)
    {
        var allDevices = await devices.GetAllAsync(cancellationToken);
        var filtered = allDevices.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            filtered = filtered.Where(device =>
                Contains(device.Name, term) || Contains(device.IpAddress, term) || Contains(device.Location, term) ||
                Contains(device.Group, term) || Contains(device.Building, term) || Contains(device.Description, term));
        }

        if (status is not null)
        {
            filtered = filtered.Where(device => device.Status == status);
        }

        if (type is not null)
        {
            filtered = filtered.Where(device => device.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            filtered = filtered.Where(device => string.Equals(device.Location, location, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            filtered = filtered.Where(device => string.Equals(device.Group, group, StringComparison.OrdinalIgnoreCase));
        }

        if (monitoringEnabled is not null)
        {
            filtered = filtered.Where(device => device.MonitoringEnabled == monitoringEnabled);
        }

        return View(new DeviceListViewModel
        {
            Devices = filtered.ToArray(),
            Search = search,
            Status = status,
            Type = type,
            Location = location,
            Group = group,
            MonitoringEnabled = monitoringEnabled,
            TotalCount = allDevices.Count,
            UpCount = allDevices.Count(device => device.Status == DeviceStatus.Up),
            DownCount = allDevices.Count(device => device.Status == DeviceStatus.Down),
            UnknownCount = allDevices.Count(device => device.Status == DeviceStatus.Unknown),
            Locations = allDevices.Select(device => device.Location).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).Cast<string>().ToArray(),
            Groups = allDevices.Select(device => device.Group).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).Cast<string>().ToArray()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var device = await devices.GetAsync(id, cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        ViewBag.History = await devices.GetHistoryAsync(id, 100, cancellationToken);
        return View(device);
    }

    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> SnmpMetrics(Guid id, CancellationToken cancellationToken)
    {
        var device = await devices.GetAsync(id, cancellationToken);
        if (device is null)
        {
            return NotFound();
        }

        var metrics = device.LatestSnmpMetrics;
        return Json(new
        {
            status = device.Status.ToString(),
            monitoringEnabled = device.MonitoringEnabled,
            lastCheckUtc = device.LastCheckUtc,
            lastSuccessfulCheckUtc = device.LastSuccessfulCheckUtc,
            lastFailedCheckUtc = device.LastFailedCheckUtc,
            currentStateSinceUtc = device.CurrentStateSinceUtc,
            responseTimeMilliseconds = device.ResponseTimeMilliseconds,
            lastCheckError = device.LastCheckError,
            snmp = metrics is null ? null : new
            {
                collectedUtc = metrics.CollectedUtc,
                collectionError = metrics.CollectionError,
                systemDescription = metrics.SystemDescription,
                systemName = metrics.SystemName,
                systemUptimeHundredths = metrics.SystemUptimeHundredths,
                cpuUtilizationPercent = metrics.CpuUtilizationPercent,
                memoryUtilizationPercent = metrics.MemoryUtilizationPercent,
                interfaces = metrics.Interfaces.Select(iface => new
                {
                    index = iface.Index,
                    name = iface.Name,
                    description = iface.Description,
                    operationalStatus = iface.OperationalStatus,
                    adminStatus = iface.AdminStatus,
                    vlanId = iface.VlanId,
                    inOctets = iface.InOctets,
                    outOctets = iface.OutOctets,
                    inBitsPerSecond = iface.InBitsPerSecond,
                    outBitsPerSecond = iface.OutBitsPerSecond,
                    speedBitsPerSecond = iface.SpeedBitsPerSecond
                })
            }
        });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> TestSnmp(Guid id, CancellationToken cancellationToken)
    {
        if (snmpCollector is null || snmpMetricsStore is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "SNMP testing is unavailable." });
        var device = (await devices.GetForMonitoringAsync(cancellationToken)).FirstOrDefault(item => item.Id == id);
        if (device is null) return NotFound(new { error = "Enable device monitoring before testing its SNMP settings." });
        if (device.MonitoringMethod != MonitoringMethod.IcmpAndSnmp) return BadRequest(new { error = "Set the monitoring method to ICMP + SNMP first." });
        if (device.LastCheckError?.StartsWith("SNMP credentials could not be decrypted", StringComparison.Ordinal) == true)
            return BadRequest(new { error = device.LastCheckError });

        var metrics = await snmpCollector.CollectAsync(device, cancellationToken);
        await snmpMetricsStore.WriteAsync(id, metrics, cancellationToken);
        var receivedData = metrics.SystemName is not null || metrics.SystemDescription is not null || metrics.SystemUptimeHundredths is not null ||
            metrics.CpuUtilizationPercent is not null || metrics.MemoryUtilizationPercent is not null || metrics.Interfaces.Count > 0;
        return Json(new
        {
            success = receivedData && metrics.CollectionError is null,
            message = receivedData && metrics.CollectionError is null
                ? $"SNMP responded. Found {metrics.Interfaces.Count} interface(s)."
                : metrics.CollectionError ?? "The agent answered no standard system or interface objects. Check its SNMP view and enabled MIBs.",
            collectedUtc = metrics.CollectedUtc,
            interfaceCount = metrics.Interfaces.Count,
            collectionError = metrics.CollectionError
        });
    }

    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var all = await devices.GetAllAsync(cancellationToken);
        return Json(all.Select(device => new
        {
            id = device.Id,
            name = device.Name,
            ipAddress = device.IpAddress,
            status = device.Status.ToString(),
            monitoringEnabled = device.MonitoringEnabled,
            lastCheckUtc = device.LastCheckUtc,
            lastSuccessfulCheckUtc = device.LastSuccessfulCheckUtc,
            lastFailedCheckUtc = device.LastFailedCheckUtc,
            currentStateSinceUtc = device.CurrentStateSinceUtc,
            responseTimeMilliseconds = device.ResponseTimeMilliseconds,
            lastCheckError = device.LastCheckError
        }));
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Discover(CancellationToken cancellationToken)
    {
        var model = new DeviceDiscoveryViewModel();
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        model.LocationChoices = hierarchy.Locations
            .Where(location => location.Kind != LocationKind.Building)
            .OrderBy(location => InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
            .Select(location => new HierarchyChoice(location.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id)))
            .ToArray();
        model.BuildingChoices = hierarchy.Locations
            .Where(location => location.Kind == LocationKind.Building)
            .OrderBy(location => InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
            .Select(location => new HierarchyChoice(location.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id)))
            .ToArray();
        model.GroupChoices = hierarchy.Groups
            .OrderBy(group => InventoryStructureService.BuildGroupPath(hierarchy.Groups, group.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => new HierarchyChoice(group.Id, InventoryStructureService.BuildGroupPath(hierarchy.Groups, group.Id)))
            .ToArray();

        if (ipam != null)
        {
            var subnets = await ipam.GetSubnetsAsync(cancellationToken);
            model.RegisteredSubnets = subnets
                .Select(s => new SubnetChoice(s.Cidr, s.Location ?? s.Building, s.Description, s.LocationId, s.BuildingId))
                .ToArray();
            if (model.RegisteredSubnets.Count > 0)
            {
                model.SubnetCidr = model.RegisteredSubnets[0].Cidr;
            }
        }

        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> ScanSubnet([FromBody] SubnetScanRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Cidr))
        {
            return Json(new SubnetScanResponse { Success = false, Error = "Enter a valid CIDR subnet, e.g. 192.168.1.0/24." });
        }

        if (!IpamService.TryNormalizeCidr(request.Cidr, out var networkAddressStr, out var prefixLength, out var canonicalCidr))
        {
            return Json(new SubnetScanResponse { Success = false, Error = "Invalid CIDR notation. Example: 192.168.1.0/24" });
        }

        if (!IPAddress.TryParse(networkAddressStr, out var netAddress) || netAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return Json(new SubnetScanResponse { Success = false, Error = "Discovery currently supports IPv4 subnets." });
        }

        if (prefixLength is < 22 or > 32)
        {
            return Json(new SubnetScanResponse { Success = false, Error = "Subnet is too large or invalid. Please specify a subnet between /22 and /32 (max 1,024 hosts)." });
        }

        var hostAddresses = GenerateIpv4Hosts(netAddress, prefixLength);
        if (hostAddresses.Count == 0)
        {
            return Json(new SubnetScanResponse { Success = false, Error = "No host addresses found in this subnet." });
        }

        var allExisting = await devices.GetAllAsync(cancellationToken);
        var existingByIp = allExisting.ToDictionary(d => d.IpAddress, StringComparer.OrdinalIgnoreCase);

        var timeout = Math.Clamp(request.TimeoutMilliseconds, 150, 3000);
        var responsiveByIp = new ConcurrentDictionary<string, IcmpProbeResult>(StringComparer.OrdinalIgnoreCase);
        await ScanAsync(hostAddresses, timeout, 64);

        // Retry misses once with a longer timeout to recover replies lost to a busy device
        // or a burst of parallel probes. Name lookups run only after probing is complete.
        var missed = hostAddresses.Where(address => !responsiveByIp.ContainsKey(address.ToString())).ToArray();
        if (missed.Length > 0)
        {
            await ScanAsync(missed, Math.Clamp(Math.Max(timeout * 2, 1000), 1000, 3000), 32);
        }

        var responsiveBag = new ConcurrentBag<DiscoveredDeviceItem>();
        await Parallel.ForEachAsync(responsiveByIp, new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = cancellationToken }, async (entry, ct) =>
        {
            string? hostName = null;
            try
            {
                using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                dnsCts.CancelAfter(TimeSpan.FromMilliseconds(450));
                hostName = (await Dns.GetHostEntryAsync(entry.Key, dnsCts.Token)).HostName;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch { }

            var existing = existingByIp.GetValueOrDefault(entry.Key);
            var suggestedName = !string.IsNullOrWhiteSpace(hostName) ? hostName : $"Device-{entry.Key}";
            var guessedType = GuessDeviceType(suggestedName, entry.Key);
            responsiveBag.Add(new DiscoveredDeviceItem
            {
                IpAddress = entry.Key,
                ResponseTimeMilliseconds = entry.Value.ResponseTimeMilliseconds ?? 0,
                Hostname = hostName,
                SuggestedName = suggestedName,
                SuggestedType = guessedType.ToString(),
                AlreadyInInventory = existing != null,
                ExistingDeviceId = existing?.Id
            });
        });

        var sorted = responsiveBag
            .OrderBy(d => IPAddress.Parse(d.IpAddress), new IpAddressComparer())
            .ToList();

        return Json(new SubnetScanResponse
        {
            Success = true,
            Cidr = canonicalCidr,
            TotalScanned = hostAddresses.Count,
            ResponsiveCount = sorted.Count,
            Devices = sorted
        });

        async Task ScanAsync(IEnumerable<IPAddress> addresses, int probeTimeout, int concurrency)
        {
            await Parallel.ForEachAsync(addresses,
                new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
                async (ip, ct) =>
                {
                    var ipText = ip.ToString();
                    var result = await icmp.CheckAsync(ipText, probeTimeout, ct);
                    if (result.IsReachable) responsiveByIp.TryAdd(ipText, result);
                });
        }
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> AddDiscovered([FromBody] SingleAddDiscoveredRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.IpAddress))
        {
            return Json(new AddDiscoveredResult { Success = false, Error = "Device name and IP address are required." });
        }

        var input = new DeviceEditInput
        {
            Name = request.Name.Trim(),
            IpAddress = request.IpAddress.Trim(),
            Type = request.Type,
            MonitoringMethod = request.MonitoringMethod,
            LocationId = request.LocationId,
            BuildingId = request.BuildingId,
            GroupId = request.GroupId,
            Description = request.Description?.Trim(),
            MonitoringEnabled = request.MonitoringEnabled,
            SnmpVersion = request.SnmpVersion,
            SnmpPort = request.SnmpPort > 0 ? request.SnmpPort : 161,
            SnmpCommunity = request.SnmpCommunity,
            SnmpUserName = request.SnmpUserName,
            SnmpSecurityLevel = request.SnmpSecurityLevel,
            SnmpAuthenticationProtocol = request.SnmpAuthenticationProtocol,
            SnmpAuthenticationPassword = request.SnmpAuthenticationPassword,
            SnmpPrivacyProtocol = request.SnmpPrivacyProtocol,
            SnmpPrivacyPassword = request.SnmpPrivacyPassword
        };

        var result = await devices.CreateAsync(input, cancellationToken);
        if (!result.Succeeded)
        {
            return Json(new AddDiscoveredResult { Success = false, Error = result.Error ?? "Could not add device." });
        }

        return Json(new AddDiscoveredResult
        {
            Success = true,
            AddedCount = 1,
            AddedIpAddresses = [input.IpAddress],
            Message = $"\"{input.Name}\" was successfully added to inventory."
        });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> BatchAddDiscovered([FromBody] BatchAddDiscoveredRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Json(new AddDiscoveredResult { Success = false, Error = "No IP addresses selected." });
        }

        var requestedDevices = request.Devices ?? [];
        if (requestedDevices.Count == 0)
        {
            requestedDevices = (request.IpAddresses ?? [])
                .Select(ip => new DiscoveredDeviceAddItem { Name = $"Device-{ip}", IpAddress = ip })
                .ToList();
        }

        if (requestedDevices.Count == 0)
        {
            return Json(new AddDiscoveredResult { Success = false, Error = "No IP addresses selected." });
        }

        if (requestedDevices.Count > 1024)
        {
            return Json(new AddDiscoveredResult { Success = false, Error = "Select no more than 1,024 devices at a time." });
        }

        var allExisting = await devices.GetAllAsync(cancellationToken);
        var existingIps = allExisting
            .Select(device => IPAddress.TryParse(device.IpAddress, out var address) ? address.ToString() : device.IpAddress)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var skipped = 0;
        var addedIps = new List<string>();
        var skippedIps = new List<string>();
        var errors = new List<string>();

        foreach (var item in requestedDevices)
        {
            if (string.IsNullOrWhiteSpace(item.IpAddress) ||
                !IPAddress.TryParse(item.IpAddress.Trim(), out var address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                errors.Add($"{item.IpAddress}: enter a valid IPv4 address.");
                continue;
            }

            var ip = address.ToString();
            if (existingIps.Contains(ip))
            {
                skipped++;
                skippedIps.Add(ip);
                continue;
            }

            var name = string.IsNullOrWhiteSpace(item.Name) ? $"Device-{ip}" : item.Name.Trim();

            var input = new DeviceEditInput
            {
                Name = name,
                IpAddress = ip,
                Type = request.CommonType,
                MonitoringMethod = request.CommonMonitoringMethod,
                MonitoringEnabled = request.MonitoringEnabled,
                LocationId = request.LocationId,
                BuildingId = request.BuildingId,
                GroupId = request.GroupId,
                Description = request.Description?.Trim(),
                SnmpVersion = request.SnmpVersion,
                SnmpCommunity = request.SnmpCommunity,
                SnmpUserName = request.SnmpUserName,
                SnmpSecurityLevel = request.SnmpSecurityLevel,
                SnmpAuthenticationProtocol = request.SnmpAuthenticationProtocol,
                SnmpAuthenticationPassword = request.SnmpAuthenticationPassword,
                SnmpPrivacyProtocol = request.SnmpPrivacyProtocol,
                SnmpPrivacyPassword = request.SnmpPrivacyPassword,
                SnmpPort = request.SnmpPort > 0 ? request.SnmpPort : 161
            };

            var res = await devices.CreateAsync(input, cancellationToken);
            if (res.Succeeded)
            {
                added++;
                addedIps.Add(ip);
                existingIps.Add(ip);
            }
            else
            {
                errors.Add($"{ip}: {res.Error}");
            }
        }

        return Json(new AddDiscoveredResult
        {
            Success = added > 0 || (errors.Count == 0 && skipped > 0),
            AddedCount = added,
            AddedIpAddresses = addedIps,
            SkippedCount = skipped,
            SkippedIpAddresses = skippedIps,
            Message = added > 0
                ? $"Successfully added {added} device(s) to inventory.{(skipped > 0 ? $" {skipped} were already in inventory." : string.Empty)}"
                : skipped > 0 && errors.Count == 0
                    ? $"No new devices were added; {skipped} selected device(s) are already in inventory."
                    : "No devices could be added.",
            Error = errors.Count > 0 ? string.Join("; ", errors.Take(4)) : null
        });
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new DeviceEditViewModel();
        await PopulateEditChoicesAsync(model, cancellationToken);
        return View("Edit", model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Create(DeviceEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateEditChoicesAsync(model, cancellationToken);
            return View("Edit", model);
        }

        var result = await devices.CreateAsync(model.ToInput(), cancellationToken);
        if (!result.Succeeded)
        {
            AddDeviceError(result);
            await PopulateEditChoicesAsync(model, cancellationToken);
            return View("Edit", model);
        }

        TempData["Success"] = $"{model.Name.Trim()} was added. The monitoring service will check it during its next cycle.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var device = await devices.GetAsync(id, cancellationToken);
        if (device is null) return NotFound();
        var model = DeviceEditViewModel.FromInfo(device);
        await PopulateEditChoicesAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Edit(DeviceEditViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateEditChoicesAsync(model, cancellationToken);
            return View(model);
        }

        var result = await devices.UpdateAsync(model.ToInput(), cancellationToken);
        if (!result.Succeeded)
        {
            AddDeviceError(result);
            await PopulateEditChoicesAsync(model, cancellationToken);
            return View(model);
        }

        TempData["Success"] = $"{model.Name.Trim()} was updated.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> ToggleMonitoring(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var result = await devices.SetMonitoringEnabledAsync(id, enabled, cancellationToken);
        if (result == DeviceWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = enabled ? "Monitoring enabled." : "Monitoring paused. The device remains in inventory.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await devices.DeleteAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["Success"] = "The device was removed from inventory.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public IActionResult Import() => View(new DeviceImportViewModel());

    [HttpGet]
    [Authorize(Policy = "CanManageDevices")]
    public IActionResult ImportTemplate()
    {
        var content = "DeviceName,IpAddress,DeviceType,Location,Group,Building,Description,MonitoringEnabled\r\nRouter 01,192.168.1.1,Router,Head Office,Network,Building A,Core gateway,true\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "devices-template.csv");
    }

    [HttpPost]
    [Authorize(Policy = "CanManageDevices")]
    [RequestSizeLimit(2_100_000)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return View(new DeviceImportViewModel { Message = "Choose a non-empty CSV file to import." });
        }

        if (file.Length > 2_000_000 || !Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return View(new DeviceImportViewModel { Message = "Choose a CSV file no larger than 2 MB." });
        }

        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            var records = CsvRecords.Parse(text);
            if (records.Count < 2)
            {
                return View(new DeviceImportViewModel { Message = "The CSV file must contain a header and at least one device row." });
            }

            var report = await ImportRecordsAsync(records, cancellationToken);
            return View(report);
        }
        catch (FormatException exception)
        {
            return View(new DeviceImportViewModel { Message = exception.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var all = await devices.GetAllAsync(cancellationToken);
        if (all.Count > 25_000) return StatusCode(StatusCodes.Status413PayloadTooLarge, "The device export exceeds the 25,000 row limit. Apply a filter or split the export.");
        var csv = new StringBuilder();
        csv.AppendLine("DeviceId,DeviceName,IpAddress,DeviceType,MonitoringMethod,Location,Group,Building,Description,MonitoringEnabled,Status,LastCheckUtc");
        foreach (var device in all)
        {
            var row = new[]
            {
                device.Id.ToString("D"), device.Name, device.IpAddress, DeviceLabels.Type(device.Type),
                device.MonitoringMethod == MonitoringMethod.Icmp ? "ICMP" : "ICMP+SNMP",
                device.Location ?? string.Empty, device.Group ?? string.Empty, device.Building ?? string.Empty,
                device.Description ?? string.Empty, device.MonitoringEnabled.ToString(), device.Status.ToString(),
                device.LastCheckUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
            };
            csv.AppendLine(string.Join(',', row.Select(EscapeCsv)));
            if (csv.Length > 20_000_000) return StatusCode(StatusCodes.Status413PayloadTooLarge, "The device export exceeds the 20 MB limit. Apply a filter or split the export.");
        }

        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(content, "text/csv; charset=utf-8", $"devices-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.csv");
    }

    private async Task<DeviceImportViewModel> ImportRecordsAsync(
        IReadOnlyList<string[]> records,
        CancellationToken cancellationToken)
    {
        var header = records[0].Select(NormalizeHeader).ToArray();
        var nameIndex = FindHeader(header, "devicename", "name");
        var ipIndex = FindHeader(header, "ipaddress", "ip");
        if (nameIndex < 0 || ipIndex < 0)
        {
            return new DeviceImportViewModel { Message = "The CSV header must include DeviceName (or Name) and IpAddress." };
        }

        var typeIndex = FindHeader(header, "devicetype", "type");
        var locationIndex = FindHeader(header, "location");
        var groupIndex = FindHeader(header, "group");
        var buildingIndex = FindHeader(header, "building");
        var descriptionIndex = FindHeader(header, "description");
        var enabledIndex = FindHeader(header, "monitoringenabled", "enabled");
        var methodIndex = FindHeader(header, "monitoringmethod");
        var parsedDevices = new List<NetworkDevice>();
        var errors = new List<string>();

        for (var rowIndex = 1; rowIndex < records.Count; rowIndex++)
        {
            var row = records[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var name = Cell(row, nameIndex).Trim();
            var ip = Cell(row, ipIndex).Trim();
            if (name.Length is 0 or > 120 || !IPAddress.TryParse(ip, out var address))
            {
                errors.Add($"Row {rowIndex + 1}: enter a device name (up to 120 characters) and a valid IP address.");
                continue;
            }

            var type = DeviceType.Other;
            var typeText = Cell(row, typeIndex).Trim();
            if (typeText.Length > 0 && !TryParseType(typeText, out type))
            {
                errors.Add($"Row {rowIndex + 1}: unsupported device type '{typeText}'.");
                continue;
            }

            var enabled = true;
            var enabledText = Cell(row, enabledIndex).Trim();
            if (enabledText.Length > 0 && !bool.TryParse(enabledText, out enabled))
            {
                errors.Add($"Row {rowIndex + 1}: MonitoringEnabled must be true or false.");
                continue;
            }

            var method = Cell(row, methodIndex).Trim();
            if (method.Length > 0 && !method.Equals("ICMP", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Row {rowIndex + 1}: SNMP credentials are not exported; this device was imported with ICMP monitoring only.");
            }

            parsedDevices.Add(new NetworkDevice
            {
                Name = name,
                IpAddress = address.ToString(),
                Type = type,
                MonitoringMethod = MonitoringMethod.Icmp,
                Location = CellOrNull(row, locationIndex),
                Group = CellOrNull(row, groupIndex),
                Building = CellOrNull(row, buildingIndex),
                Description = CellOrNull(row, descriptionIndex),
                MonitoringEnabled = enabled
            });
        }

        var result = await devices.ImportAsync(parsedDevices, cancellationToken);
        errors.AddRange(result.Errors);
        return new DeviceImportViewModel
        {
            Imported = result.Imported,
            Errors = errors,
            Message = $"Imported {result.Imported} device(s)."
        };
    }

    private static string Cell(IReadOnlyList<string> row, int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;

    private static string? CellOrNull(IReadOnlyList<string> row, int index)
    {
        var value = Cell(row, index).Trim();
        return value.Length == 0 ? null : value;
    }

    private static int FindHeader(IReadOnlyList<string> header, params string[] names)
    {
        for (var index = 0; index < header.Count; index++)
        {
            if (names.Contains(header[index], StringComparer.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeHeader(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool TryParseType(string value, out DeviceType type)
    {
        var normalized = NormalizeHeader(value);
        type = normalized switch
        {
            "coreswitch" => DeviceType.CoreSwitch,
            "distributionswitch" => DeviceType.DistributionSwitch,
            "accessswitch" => DeviceType.AccessSwitch,
            "wirelessaccesspoint" or "accesspoint" => DeviceType.WirelessAccessPoint,
            "ups" => DeviceType.Ups,
            "pcworkstation" or "workstation" => DeviceType.Workstation,
            _ => default
        };
        if (normalized is "coreswitch" or "distributionswitch" or "accessswitch" or "wirelessaccesspoint" or "accesspoint" or "ups" or "pcworkstation" or "workstation")
        {
            return true;
        }

        return Enum.TryParse(value.Replace("/", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal), true, out type);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private void AddDeviceError(DeviceCommandResult result)
    {
        if (result.Result == DeviceWriteResult.DuplicateIpAddress)
        {
            ModelState.AddModelError(nameof(DeviceEditViewModel.IpAddress), "A device with this IP address already exists.");
        }
        else if (!string.IsNullOrWhiteSpace(result.Error))
        {
            ModelState.AddModelError(string.Empty, result.Error);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "The device could not be saved.");
        }
    }

    private async Task PopulateEditChoicesAsync(DeviceEditViewModel model, CancellationToken cancellationToken)
    {
        var document = await structure.GetStructureAsync(cancellationToken);
        model.LocationChoices = document.Locations
            .Where(location => location.Kind != LocationKind.Building)
            .OrderBy(location => InventoryStructureService.BuildLocationPath(document.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
            .Select(location => new HierarchyChoice(location.Id, InventoryStructureService.BuildLocationPath(document.Locations, location.Id)))
            .ToArray();
        model.BuildingChoices = document.Locations
            .Where(location => location.Kind == LocationKind.Building)
            .OrderBy(location => InventoryStructureService.BuildLocationPath(document.Locations, location.Id), StringComparer.OrdinalIgnoreCase)
            .Select(location => new HierarchyChoice(location.Id, InventoryStructureService.BuildLocationPath(document.Locations, location.Id)))
            .ToArray();
        model.GroupChoices = document.Groups
            .OrderBy(group => InventoryStructureService.BuildGroupPath(document.Groups, group.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => new HierarchyChoice(group.Id, InventoryStructureService.BuildGroupPath(document.Groups, group.Id)))
            .ToArray();
    }

    private static class CsvRecords
    {
        private const int MaximumRows = 10_000;
        private const int MaximumColumns = 64;
        private const int MaximumFieldLength = 4_096;

        public static List<string[]> Parse(string text)
        {
            if (text.Length > 2_000_000) throw new FormatException("CSV files must be 2 MB or smaller.");
            var records = new List<string[]>();
            var record = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var fieldStarted = false;

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];
                if (inQuotes)
                {
                    if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        if (field.Length > MaximumFieldLength) throw new FormatException("CSV fields cannot exceed 4,096 characters.");
                        index++;
                    }
                    else if (character == '"')
                    {
                        inQuotes = false;
                    }
                    else
                    {
                        field.Append(character);
                        if (field.Length > MaximumFieldLength) throw new FormatException("CSV fields cannot exceed 4,096 characters.");
                    }

                    continue;
                }

                if (character == '"')
                {
                    if (fieldStarted || field.Length > 0)
                    {
                        throw new FormatException("The CSV contains a quote in an unquoted field.");
                    }

                    fieldStarted = true;
                    inQuotes = true;
                }
                else if (character == ',')
                {
                    record.Add(field.ToString());
                    if (record.Count > MaximumColumns) throw new FormatException("CSV rows cannot contain more than 64 columns.");
                    field.Clear();
                    fieldStarted = false;
                }
                else if (character is '\r' or '\n')
                {
                    record.Add(field.ToString());
                    if (record.Count > MaximumColumns) throw new FormatException("CSV rows cannot contain more than 64 columns.");
                    field.Clear();
                    fieldStarted = false;
                    if (record.Any(value => value.Length > 0))
                    {
                        records.Add(record.ToArray());
                        if (records.Count > MaximumRows + 1) throw new FormatException($"CSV files can contain up to {MaximumRows} device rows.");
                    }

                    record.Clear();
                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        index++;
                    }
                }
                else
                {
                    field.Append(character);
                    if (field.Length > MaximumFieldLength) throw new FormatException("CSV fields cannot exceed 4,096 characters.");
                    fieldStarted = true;
                }
            }

            if (inQuotes)
            {
                throw new FormatException("The CSV has an unterminated quoted field.");
            }

            if (field.Length > 0 || record.Count > 0)
            {
                record.Add(field.ToString());
                if (record.Count > MaximumColumns) throw new FormatException("CSV rows cannot contain more than 64 columns.");
                if (record.Any(value => value.Length > 0))
                {
                    records.Add(record.ToArray());
                    if (records.Count > MaximumRows + 1) throw new FormatException($"CSV files can contain up to {MaximumRows} device rows.");
                }
            }

            return records;
        }
    }

    private static List<IPAddress> GenerateIpv4Hosts(IPAddress networkAddress, int prefixLength)
    {
        var bytes = networkAddress.GetAddressBytes();
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        uint startIp = BitConverter.ToUInt32(bytes, 0);
        uint hostCount = prefixLength == 32 ? 1 : (uint)(1 << (32 - prefixLength));

        var list = new List<IPAddress>();
        uint first = prefixLength <= 30 ? startIp + 1 : startIp;
        uint last = prefixLength <= 30 ? startIp + hostCount - 2 : startIp + hostCount - 1;

        for (uint cur = first; cur <= last && list.Count < 1024; cur++)
        {
            var curBytes = BitConverter.GetBytes(cur);
            if (BitConverter.IsLittleEndian) Array.Reverse(curBytes);
            list.Add(new IPAddress(curBytes));
        }

        return list;
    }

    private static DeviceType GuessDeviceType(string name, string ip)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("router") || lower.Contains("rt-") || lower.Contains("gateway") || ip.EndsWith(".1") || ip.EndsWith(".254"))
            return DeviceType.Router;
        if (lower.Contains("core") && lower.Contains("sw"))
            return DeviceType.CoreSwitch;
        if (lower.Contains("dist") && lower.Contains("sw"))
            return DeviceType.DistributionSwitch;
        if (lower.Contains("sw") || lower.Contains("switch"))
            return DeviceType.AccessSwitch;
        if (lower.Contains("firewall") || lower.Contains("fw-") || lower.Contains("utm") || lower.Contains("forti"))
            return DeviceType.Firewall;
        if (lower.Contains("ap-") || lower.Contains("wifi") || lower.Contains("wireless") || lower.Contains("accesspoint"))
            return DeviceType.WirelessAccessPoint;
        if (lower.Contains("srv") || lower.Contains("server") || lower.Contains("host") || lower.Contains("dc-") || lower.Contains("ad-"))
            return DeviceType.Server;
        if (lower.Contains("pc") || lower.Contains("desktop") || lower.Contains("workstation") || lower.Contains("user-"))
            return DeviceType.Workstation;
        if (lower.Contains("print") || lower.Contains("prn"))
            return DeviceType.Printer;
        if (lower.Contains("ups") || lower.Contains("apc") || lower.Contains("power"))
            return DeviceType.Ups;
        if (lower.Contains("storage") || lower.Contains("nas") || lower.Contains("san") || lower.Contains("synology"))
            return DeviceType.Storage;
        return DeviceType.Other;
    }

    private sealed class IpAddressComparer : IComparer<IPAddress>
    {
        public int Compare(IPAddress? x, IPAddress? y)
        {
            if (x is null || y is null) return 0;
            var b1 = x.GetAddressBytes();
            var b2 = y.GetAddressBytes();
            for (int i = 0; i < Math.Min(b1.Length, b2.Length); i++)
            {
                if (b1[i] != b2[i]) return b1[i].CompareTo(b2[i]);
            }
            return b1.Length.CompareTo(b2.Length);
        }
    }
}
