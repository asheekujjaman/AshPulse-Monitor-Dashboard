using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Ipam;

public sealed class IpamService(
    IIpamRepository repository,
    IDeviceService devices,
    IInventoryStructureService structure) : IIpamService
{
    private const int MaxCsvRows = 10000;

    public async Task<IReadOnlyList<IpamRecord>> GetRecordsAsync(CancellationToken cancellationToken = default)
    {
        var records = await repository.GetRecordsAsync(cancellationToken);
        var allDevices = (await devices.GetAllAsync(cancellationToken)).ToDictionary(device => device.Id);
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        return records.Select(record => Enrich(record, allDevices, hierarchy)).ToArray();
    }

    public async Task<IpamRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await repository.GetRecordAsync(id, cancellationToken);
        if (record is null) return null;
        var allDevices = (await devices.GetAllAsync(cancellationToken)).ToDictionary(device => device.Id);
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        return Enrich(record, allDevices, hierarchy);
    }

    public async Task<IpamMutationResult> SaveRecordAsync(IpamRecord record, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateRecordAsync(record, cancellationToken);
        if (validation is not null) return new(false, Error: validation);
        var result = await repository.SaveRecordAsync(record, cancellationToken);
        return result switch
        {
            IpamWriteResult.Success => new(true, record.Id),
            IpamWriteResult.DuplicateIpAddress => new(false, Error: "An IP record already exists for this address."),
            IpamWriteResult.NotFound => new(false, Error: "The IP record no longer exists."),
            _ => new(false, Error: "The IP record could not be saved.")
        };
    }

    public async Task<IpamMutationResult> ChangeAllocationAsync(IpamRecord record, string action, string? actor, CancellationToken cancellationToken = default)
    {
        if (record.AssignmentStatus is not (IpAssignmentStatus.Assigned or IpAssignmentStatus.Reserved or IpAssignmentStatus.Available))
            return new(false, Error: "Choose Assigned, Reserved, or Available for an allocation action.");
        var validation = await ValidateRecordAsync(record, cancellationToken);
        if (validation is not null) return new(false, Error: validation);
        var result = await repository.ChangeAllocationAsync(record, new IpamHistoryEvent
        {
            Action = action,
            Actor = string.IsNullOrWhiteSpace(actor) ? "Administrator" : actor,
            Details = record.ReservationNote
        }, cancellationToken);
        return result switch
        {
            IpamWriteResult.Success => new(true, record.Id),
            IpamWriteResult.AllocationConflict => new(false, Error: "This address changed or is already allocated. Refresh the subnet and try again."),
            _ => new(false, Error: "The address or subnet is invalid. Refresh the subnet and try again.")
        };
    }

    public Task<IReadOnlyList<IpamHistoryEvent>> GetHistoryAsync(CancellationToken cancellationToken = default) => repository.GetHistoryAsync(cancellationToken);

    public async Task<IpamMutationResult> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default) =>
        await repository.DeleteRecordAsync(id, cancellationToken)
            ? new(true, id)
            : new(false, Error: "The IP record no longer exists.");

    public Task<IReadOnlyList<IpamSubnet>> GetSubnetsAsync(CancellationToken cancellationToken = default) => repository.GetSubnetsAsync(cancellationToken);

    public Task<IpamSubnet?> GetSubnetAsync(Guid id, CancellationToken cancellationToken = default) => repository.GetSubnetAsync(id, cancellationToken);

    public async Task<IpamMutationResult> SaveSubnetAsync(IpamSubnet subnet, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeCidr(subnet.Cidr, out var networkAddress, out var prefixLength, out var canonical))
        {
            return new(false, Error: "Enter a valid IPv4 or IPv6 subnet in CIDR notation, such as 192.0.2.0/24.");
        }

        if (subnet.VlanId is < 1 or > 4094) return new(false, Error: "VLAN ID must be between 1 and 4094.");
        if (TooLong(subnet.VlanName, 120) || TooLong(subnet.Description, 500)) return new(false, Error: "Subnet text fields exceed their maximum length.");
        if (await structure.ValidateDeviceAssignmentAsync(subnet.LocationId, subnet.BuildingId, null, cancellationToken) is { } assignmentError)
        {
            return new(false, Error: assignmentError);
        }

        subnet.Cidr = canonical;
        subnet.NetworkAddress = networkAddress;
        subnet.PrefixLength = prefixLength;
        if (subnet.LocationId is { } locationId)
        {
            var locations = (await structure.GetStructureAsync(cancellationToken)).Locations;
            subnet.Location = locations.FirstOrDefault(location => location.Id == locationId)?.Name;
        }
        if (subnet.BuildingId is { } buildingId)
        {
            var locations = (await structure.GetStructureAsync(cancellationToken)).Locations;
            subnet.Building = locations.FirstOrDefault(location => location.Id == buildingId)?.Name;
        }

        var result = await repository.SaveSubnetAsync(subnet, cancellationToken);
        return result switch
        {
            IpamWriteResult.Success => new(true, subnet.Id),
            IpamWriteResult.DuplicateSubnet => new(false, Error: "A subnet with this CIDR already exists."),
            IpamWriteResult.NotFound => new(false, Error: "The subnet no longer exists."),
            _ => new(false, Error: "The subnet could not be saved.")
        };
    }

    public async Task<IpamMutationResult> DeleteSubnetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await repository.DeleteSubnetAsync(id, cancellationToken);
        return result switch
        {
            IpamWriteResult.Success => new(true, id),
            IpamWriteResult.InUse => new(false, Error: "Reassign or remove the IP records in this subnet before deleting it."),
            _ => new(false, Error: "The subnet no longer exists.")
        };
    }

    public async Task<IpamCsvImportPreview> PreviewCsvImportAsync(
        string csv,
        IReadOnlyDictionary<string, string?> mapping,
        CancellationToken cancellationToken = default)
    {
        if (csv.Length > 5_000_000) return new([], [], "CSV files must be 5 MB or smaller.", 0, 0, 0);
        if (mapping.Count > 64 || mapping.Any(item => item.Key.Length > 64 || item.Value?.Length > 128))
            return new([], [], "The column mapping is invalid or exceeds its size limit.", 0, 0, 0);
        List<string[]> table;
        try
        {
            table = ParseCsv(csv);
        }
        catch (FormatException exception)
        {
            return new([], [], exception.Message, 0, 0, 0);
        }

        if (table.Count < 2) return new(table.FirstOrDefault() ?? [], [], "Include a header and at least one data row.", 0, 0, 0);
        if (table.Count - 1 > MaxCsvRows) return new(table[0], [], $"Import files can contain up to {MaxCsvRows} data rows.", 0, 0, 0);
        var header = table[0].Select(value => value.Trim()).ToArray();
        var ipHeader = MapHeader("IpAddress", "IPAddress", mapping, header);
        if (ipHeader < 0) return new(header, [], "Map the IP address column to continue.", 0, table.Count - 1, 0);

        var existing = (await GetRecordsAsync(cancellationToken)).Select(record => NormalizeIp(record.IpAddress)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var rows = new List<IpamCsvPreviewRow>();
        for (var rowIndex = 1; rowIndex < table.Count; rowIndex++)
        {
            var input = table[rowIndex];
            if (input.All(string.IsNullOrWhiteSpace)) continue;
            var errors = new List<string>();
            var record = await CreateRecordFromCsvRowAsync(input, header, mapping, rowIndex + 1, errors, cancellationToken);
            var duplicate = false;
            if (record is not null && errors.Count == 0)
            {
                var normalized = NormalizeIp(record.IpAddress);
                if (normalized is not null && !seen.Add(normalized))
                {
                    duplicate = true;
                    errors.Add("Duplicate IP address; the existing record will not be overwritten.");
                }
            }
            rows.Add(new IpamCsvPreviewRow(rowIndex + 1, record, errors, duplicate));
        }

        return new IpamCsvImportPreview(
            header,
            rows,
            null,
            rows.Count(row => row.Errors.Count == 0 && !row.Duplicate),
            rows.Count(row => row.Errors.Count > 0 && !row.Duplicate),
            rows.Count(row => row.Duplicate));
    }

    public async Task<IpamCsvImportReport> ImportCsvAsync(
        string csv,
        IReadOnlyDictionary<string, string?> mapping,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewCsvImportAsync(csv, mapping, cancellationToken);
        if (preview.Error is not null)
        {
            return new(0, 0, 0, [preview.Error], []);
        }

        var candidates = preview.Rows.Where(row => row.Record is not null && row.Errors.Count == 0 && !row.Duplicate).Select(row => row.Record!).ToArray();
        var storageResult = await repository.AddRecordsIfUniqueAsync(candidates, cancellationToken);
        var errors = preview.Rows.Where(row => row.Errors.Count > 0 && !row.Duplicate)
            .Select(row => $"Row {row.RowNumber}: {string.Join(" ", row.Errors)}")
            .ToList();
        var duplicateIps = preview.Rows.Where(row => row.Duplicate && row.Record is not null).Select(row => NormalizeIp(row.Record!.IpAddress)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        duplicateIps.UnionWith(storageResult.DuplicateIpAddresses);
        return new(
            storageResult.ImportedCount,
            preview.InvalidCount,
            duplicateIps.Count,
            errors,
            duplicateIps.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static bool TryNormalizeIpAddress(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!IPAddress.TryParse(value, out var address)) return false;
        normalized = address.ToString();
        return true;
    }

    public static bool TryNormalizeCidr(string? value, out string networkAddress, out int prefixLength, out string canonical)
    {
        networkAddress = string.Empty;
        prefixLength = 0;
        canonical = string.Empty;
        var parts = value?.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } || !IPAddress.TryParse(parts[0], out var address) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength)) return false;
        var bytes = address.GetAddressBytes();
        var bitCount = bytes.Length * 8;
        if (prefixLength < 0 || prefixLength > bitCount) return false;
        var network = bytes.ToArray();
        for (var index = 0; index < network.Length; index++)
        {
            var remaining = prefixLength - index * 8;
            var mask = remaining >= 8 ? 0xff : remaining <= 0 ? 0 : 0xff << (8 - remaining) & 0xff;
            network[index] = (byte)(network[index] & mask);
        }
        networkAddress = new IPAddress(network).ToString();
        canonical = $"{networkAddress}/{prefixLength}";
        return true;
    }

    private async Task<string?> ValidateRecordAsync(IpamRecord record, CancellationToken cancellationToken)
    {
        if (!TryNormalizeIpAddress(record.IpAddress, out var ip)) return "Enter a valid IPv4 or IPv6 address.";
        if (!Enum.IsDefined(record.AssignmentStatus)) return "Choose a valid assignment status.";
        if (record.VlanId is < 1 or > 4094) return "VLAN ID must be between 1 and 4094.";
        if (TooLong(record.VlanName, 120) || TooLong(record.DeviceName, 120) || TooLong(record.DeviceType, 80) ||
            TooLong(record.SwitchName, 120) || TooLong(record.SwitchPort, 80) || TooLong(record.AssignedTo, 160) ||
            TooLong(record.Department, 120) || TooLong(record.Description, 500) || TooLong(record.ReservationNote, 300)) return "One or more text fields exceed their maximum length.";
        record.IpAddress = ip;

        if (!string.IsNullOrWhiteSpace(record.SwitchIpAddress))
        {
            if (!TryNormalizeIpAddress(record.SwitchIpAddress, out var switchIp)) return "Enter a valid switch IP address.";
            record.SwitchIpAddress = switchIp;
        }
        if (!string.IsNullOrWhiteSpace(record.MacAddress))
        {
            if (!TryNormalizeMac(record.MacAddress, out var mac)) return "Enter a valid MAC address, such as 00:11:22:33:44:55.";
            record.MacAddress = mac;
        }

        if (record.DeviceId is { } deviceId)
        {
            var device = (await devices.GetAllAsync(cancellationToken)).FirstOrDefault(item => item.Id == deviceId);
            if (device is null) return "Choose an existing monitored device or clear the linked device.";
            if (!string.Equals(ip, device.IpAddress, StringComparison.OrdinalIgnoreCase)) return "A linked IPAM record must use the device's current IP address.";
            record.DeviceName = device.Name;
            record.DeviceType = device.Type.ToString();
            record.LocationId = device.LocationId;
            record.BuildingId = device.BuildingId;
            record.GroupId = device.GroupId;
            record.Location = device.Location;
            record.Building = device.Building;
            record.Group = device.Group;
        }
        else
        {
            var assignmentError = await structure.ValidateDeviceAssignmentAsync(record.LocationId, record.BuildingId, record.GroupId, cancellationToken);
            if (assignmentError is not null) return assignmentError;
            await ResolveAssignmentLabelsAsync(record, cancellationToken);
        }

        if (record.SubnetId is { } subnetId)
        {
            var subnet = await repository.GetSubnetAsync(subnetId, cancellationToken);
            if (subnet is null) return "Choose an existing subnet or clear the subnet assignment.";
            if (!TryNormalizeCidr(subnet.Cidr, out var network, out var prefix, out _) || !IsInCidr(ip, network, prefix))
            {
                return "The IP address is outside the selected subnet.";
            }
        }

        return null;
    }

    private async Task ResolveAssignmentLabelsAsync(IpamRecord record, CancellationToken cancellationToken)
    {
        var hierarchy = await structure.GetStructureAsync(cancellationToken);
        if (record.LocationId is { } locationId)
        {
            record.Location = hierarchy.Locations.FirstOrDefault(item => item.Id == locationId)?.Name;
        }
        if (record.BuildingId is { } buildingId)
        {
            record.Building = hierarchy.Locations.FirstOrDefault(item => item.Id == buildingId)?.Name;
        }
        if (record.GroupId is { } groupId)
        {
            record.Group = hierarchy.Groups.FirstOrDefault(item => item.Id == groupId)?.Name;
        }
    }

    private async Task<IpamRecord?> CreateRecordFromCsvRowAsync(
        IReadOnlyList<string> row,
        IReadOnlyList<string> header,
        IReadOnlyDictionary<string, string?> mapping,
        int rowNumber,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        string Cell(string field) => CellByMappedHeader(row, MapHeader(field, field, mapping, header));
        if (!TryNormalizeIpAddress(Cell("IpAddress"), out var normalizedIp))
        {
            errors.Add("Enter a valid IP address.");
            return null;
        }

        var record = new IpamRecord { IpAddress = normalizedIp };
        var statusText = Cell("AssignmentStatus");
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            if (!Enum.TryParse<IpAssignmentStatus>(statusText, true, out var status) || !Enum.IsDefined(status)) errors.Add("Assignment status is not supported.");
            else record.AssignmentStatus = status;
        }
        if (int.TryParse(Cell("VlanId"), NumberStyles.None, CultureInfo.InvariantCulture, out var vlanId)) record.VlanId = vlanId;
        else if (!string.IsNullOrWhiteSpace(Cell("VlanId"))) errors.Add("VLAN ID must be a number between 1 and 4094.");
        var deviceIdText = Cell("DeviceId");
        if (!string.IsNullOrWhiteSpace(deviceIdText))
        {
            if (!Guid.TryParse(deviceIdText, out var deviceId)) errors.Add("DeviceId must be a valid device ID.");
            else record.DeviceId = deviceId;
        }
        record.VlanName = NullIfBlank(Cell("VlanName"));
        record.DeviceName = NullIfBlank(Cell("DeviceName"));
        record.DeviceType = NullIfBlank(Cell("DeviceType"));
        record.SwitchName = NullIfBlank(Cell("SwitchName"));
        record.SwitchIpAddress = NullIfBlank(Cell("SwitchIpAddress"));
        record.SwitchPort = NullIfBlank(Cell("SwitchPort"));
        record.MacAddress = NullIfBlank(Cell("MacAddress"));
        record.AssignedTo = NullIfBlank(Cell("AssignedTo"));
        record.Department = NullIfBlank(Cell("Department"));
        record.Description = NullIfBlank(Cell("Description"));
        record.ReservationNote = NullIfBlank(Cell("ReservationNote"));
        if (!string.IsNullOrWhiteSpace(Cell("ReservedUntilUtc")))
        {
            if (DateTimeOffset.TryParse(Cell("ReservedUntilUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var reservedUntil)) record.ReservedUntilUtc = reservedUntil;
            else errors.Add("ReservedUntilUtc must be a valid ISO 8601 date and time.");
        }
        var subnetCidr = Cell("SubnetCidr");
        if (!string.IsNullOrWhiteSpace(subnetCidr))
        {
            if (!TryNormalizeCidr(subnetCidr, out _, out _, out var canonical)) errors.Add("Subnet CIDR is invalid.");
            else
            {
                var subnet = (await repository.GetSubnetsAsync(cancellationToken)).FirstOrDefault(item => string.Equals(item.Cidr, canonical, StringComparison.OrdinalIgnoreCase));
                if (subnet is null) errors.Add($"Subnet {canonical} is not registered. Register it before importing this row.");
                else record.SubnetId = subnet.Id;
            }
        }

        var locationName = Cell("Location");
        var buildingName = Cell("Building");
        var groupName = Cell("Group");
        if (!string.IsNullOrWhiteSpace(locationName) || !string.IsNullOrWhiteSpace(buildingName) || !string.IsNullOrWhiteSpace(groupName))
        {
            var hierarchy = await structure.GetStructureAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(locationName))
            {
                var candidates = hierarchy.Locations.Where(item => item.Kind != LocationKind.Building &&
                    (string.Equals(item.Name, locationName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), locationName, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (candidates.Length != 1) errors.Add("Location must match one existing location name or path.");
                else record.LocationId = candidates[0].Id;
            }
            if (!string.IsNullOrWhiteSpace(buildingName))
            {
                var candidates = hierarchy.Locations.Where(item => item.Kind == LocationKind.Building &&
                    (string.Equals(item.Name, buildingName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), buildingName, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (candidates.Length != 1) errors.Add("Building must match one existing building name or path.");
                else
                {
                    record.BuildingId = candidates[0].Id;
                    if (record.LocationId is null) record.LocationId = candidates[0].ParentLocationId;
                    else if (candidates[0].ParentLocationId != record.LocationId) errors.Add("Building is not under the selected location.");
                }
            }
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                var candidates = hierarchy.Groups.Where(item => string.Equals(item.Name, groupName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(InventoryStructureService.BuildGroupPath(hierarchy.Groups, item.Id), groupName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length != 1) errors.Add("Group must match one existing group name or path.");
                else record.GroupId = candidates[0].Id;
            }
        }

        if (errors.Count == 0 && await ValidateRecordAsync(record, cancellationToken) is { } validationError) errors.Add(validationError);
        if (errors.Count > 0) return record;
        return record;
    }

    private static IpamRecord Enrich(IpamRecord record, IReadOnlyDictionary<Guid, DeviceInfo> devicesById, InventoryStructureDocument hierarchy)
    {
        var copy = Copy(record);
        if (copy.DeviceId is { } deviceId && devicesById.TryGetValue(deviceId, out var device))
        {
            copy.IpAddress = device.IpAddress;
            copy.DeviceName = device.Name;
            copy.DeviceType = device.Type.ToString();
            copy.LocationId = device.LocationId;
            copy.BuildingId = device.BuildingId;
            copy.GroupId = device.GroupId;
            copy.Location = device.Location;
            copy.Building = device.Building;
            copy.Group = device.Group;
        }
        if (copy.LocationId is { } locationId) copy.Location = hierarchy.Locations.FirstOrDefault(item => item.Id == locationId)?.Name ?? copy.Location;
        if (copy.BuildingId is { } buildingId) copy.Building = hierarchy.Locations.FirstOrDefault(item => item.Id == buildingId)?.Name ?? copy.Building;
        if (copy.GroupId is { } groupId) copy.Group = hierarchy.Groups.FirstOrDefault(item => item.Id == groupId)?.Name ?? copy.Group;
        return copy;
    }

    private static IpamRecord Copy(IpamRecord source) => new()
    {
        Id = source.Id, DeviceId = source.DeviceId, IpAddress = source.IpAddress, SubnetId = source.SubnetId,
        VlanId = source.VlanId, VlanName = source.VlanName, DeviceName = source.DeviceName, DeviceType = source.DeviceType,
        LocationId = source.LocationId, BuildingId = source.BuildingId, GroupId = source.GroupId,
        Location = source.Location, Building = source.Building, Group = source.Group, SwitchName = source.SwitchName,
        SwitchIpAddress = source.SwitchIpAddress, SwitchPort = source.SwitchPort, MacAddress = source.MacAddress,
        AssignedTo = source.AssignedTo, Department = source.Department, Description = source.Description,
        ReservationNote = source.ReservationNote, ReservedUntilUtc = source.ReservedUntilUtc, UpdatedBy = source.UpdatedBy,
        AssignmentStatus = source.AssignmentStatus, CreatedUtc = source.CreatedUtc, UpdatedUtc = source.UpdatedUtc
    };

    private static string CellByMappedHeader(IReadOnlyList<string> row, int headerIndex) =>
        headerIndex >= 0 && headerIndex < row.Count ? row[headerIndex].Trim() : string.Empty;

    private static int MapHeader(string field, string defaultName, IReadOnlyDictionary<string, string?> mapping, IReadOnlyList<string> header)
    {
        if (mapping.TryGetValue(field, out var mapped) && string.IsNullOrWhiteSpace(mapped)) return -1;
        var target = mapping.TryGetValue(field, out mapped) ? mapped! : defaultName;
        return header.ToList().FindIndex(value => string.Equals(value.Trim(), target.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryNormalizeMac(string input, out string normalized)
    {
        normalized = string.Empty;
        var digits = Regex.Replace(input, "[^0-9a-fA-F]", string.Empty);
        if (digits.Length != 12 || !Regex.IsMatch(input, "^[0-9a-fA-F:.-]+$")) return false;
        normalized = string.Join(':', Enumerable.Range(0, 6).Select(index => digits.Substring(index * 2, 2).ToUpperInvariant()));
        return true;
    }

    private static bool IsInCidr(string ip, string network, int prefixLength)
    {
        if (!IPAddress.TryParse(ip, out var address) || !IPAddress.TryParse(network, out var baseAddress)) return false;
        var addressBytes = address.GetAddressBytes();
        var networkBytes = baseAddress.GetAddressBytes();
        if (addressBytes.Length != networkBytes.Length) return false;
        for (var index = 0; index < addressBytes.Length; index++)
        {
            var remaining = prefixLength - index * 8;
            var mask = remaining >= 8 ? 0xff : remaining <= 0 ? 0 : 0xff << (8 - remaining) & 0xff;
            if ((addressBytes[index] & mask) != networkBytes[index]) return false;
        }
        return true;
    }

    private static string? NormalizeIp(string? value) => TryNormalizeIpAddress(value, out var normalized) ? normalized : null;

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TooLong(string? value, int limit) => value?.Length > limit;

    private static List<string[]> ParseCsv(string text)
    {
        const int maximumColumns = 64;
        const int maximumFieldLength = 4_096;
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
                    if (field.Length > maximumFieldLength) throw new FormatException("CSV fields cannot exceed 4,096 characters.");
                    index++;
                }
                else if (character == '"') inQuotes = false;
                else
                {
                    field.Append(character);
                    if (field.Length > maximumFieldLength) throw new FormatException("CSV fields cannot exceed 4,096 characters.");
                }
                continue;
            }
            if (character == '"')
            {
                if (fieldStarted || field.Length > 0) throw new FormatException("CSV has a quote in an unquoted field.");
                fieldStarted = true;
                inQuotes = true;
            }
            else if (character == ',')
            {
                record.Add(field.ToString());
                if (record.Count > maximumColumns) throw new FormatException("CSV rows cannot contain more than 64 columns.");
                field.Clear(); fieldStarted = false;
            }
            else if (character is '\r' or '\n')
            {
                record.Add(field.ToString());
                if (record.Count > maximumColumns) throw new FormatException("CSV rows cannot contain more than 64 columns.");
                field.Clear(); fieldStarted = false;
                if (record.Any(value => value.Length > 0))
                {
                    records.Add(record.ToArray());
                    if (records.Count > MaxCsvRows + 1) throw new FormatException($"Import files can contain up to {MaxCsvRows} data rows.");
                }
                record.Clear();
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            }
            else
            {
                field.Append(character);
                if (field.Length > maximumFieldLength) throw new FormatException("CSV fields cannot exceed 4,096 characters.");
                fieldStarted = true;
            }
        }
        if (inQuotes) throw new FormatException("CSV has an unterminated quoted field.");
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            if (record.Count > maximumColumns) throw new FormatException("CSV rows cannot contain more than 64 columns.");
            if (record.Any(value => value.Length > 0)) records.Add(record.ToArray());
            if (records.Count > MaxCsvRows + 1) throw new FormatException($"Import files can contain up to {MaxCsvRows} data rows.");
        }
        return records;
    }
}
