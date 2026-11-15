using System.Net;
using System.Security.Cryptography;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.Monitoring;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Devices;

public sealed class DeviceService(
    IDeviceRepository devices,
    IMonitoringHistoryStore history,
    ISecretProtector secretProtector,
    ISnmpMetricsStore metricsStore,
    IMonitoringCycleTrigger cycleTrigger,
    IInventoryStructureRepository structure,
    INetworkMapRepository maps,
    IIpamRepository ipam) : IDeviceService
{
    public async Task<IReadOnlyList<DeviceInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await devices.GetAllAsync(cancellationToken);
        var metrics = await metricsStore.GetManyAsync(all.Select(device => device.Id), cancellationToken);
        var hierarchy = await structure.GetAsync(cancellationToken);
        return all.Select(device => ToInfo(device, metrics.GetValueOrDefault(device.Id), hierarchy)).ToArray();
    }

    public async Task<DeviceInfo?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await devices.GetAsync(id, cancellationToken);
        if (device is null)
        {
            return null;
        }

        var metrics = await metricsStore.GetManyAsync([id], cancellationToken);
        var hierarchy = await structure.GetAsync(cancellationToken);
        return ToInfo(device, metrics.GetValueOrDefault(id), hierarchy);
    }

    public async Task<DeviceCommandResult> CreateAsync(DeviceEditInput input, CancellationToken cancellationToken = default)
    {
        var validationError = Validate(input, isUpdate: false, existing: null);
        if (validationError is not null)
        {
            return new(DeviceWriteResult.ValidationFailed, validationError);
        }

        var assignmentError = await ValidateAssignmentAsync(input, cancellationToken);
        if (assignmentError is not null) return new(DeviceWriteResult.ValidationFailed, assignmentError);

        var device = CreateEntity(input);
        var result = await devices.CreateAsync(device, cancellationToken);
        if (result == DeviceWriteResult.Success)
        {
            cycleTrigger.RequestImmediateCycle();
        }

        return new(result);
    }

    public async Task<DeviceCommandResult> UpdateAsync(DeviceEditInput input, CancellationToken cancellationToken = default)
    {
        if (input.Id is not { } id)
        {
            return new(DeviceWriteResult.NotFound);
        }

        var existing = await devices.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return new(DeviceWriteResult.NotFound);
        }

        var validationError = Validate(input, isUpdate: true, existing);
        if (validationError is not null)
        {
            return new(DeviceWriteResult.ValidationFailed, validationError);
        }

        var assignmentError = await ValidateAssignmentAsync(input, cancellationToken);
        if (assignmentError is not null) return new(DeviceWriteResult.ValidationFailed, assignmentError);

        var updated = CreateEntity(input, existing);
        var normalizedIp = IPAddress.Parse(updated.IpAddress).ToString();
        var ipamRecords = await ipam.GetRecordsAsync(cancellationToken);
        var linkedIpamRecords = ipamRecords.Where(record => record.DeviceId == id).ToArray();
        var otherIpamRecordUsesAddress = !string.Equals(existing.IpAddress, normalizedIp, StringComparison.OrdinalIgnoreCase) &&
            ipamRecords.Any(record => record.DeviceId != id &&
                IPAddress.TryParse(record.IpAddress, out var existingAddress) && string.Equals(existingAddress.ToString(), normalizedIp, StringComparison.OrdinalIgnoreCase));
        if (otherIpamRecordUsesAddress)
        {
            return new(DeviceWriteResult.ValidationFailed, "An IPAM record already uses this address. Resolve that inventory entry before changing the device address.");
        }
        var result = await devices.UpdateAsync(updated, cancellationToken);
        if (result == DeviceWriteResult.Success && !string.Equals(existing.IpAddress, updated.IpAddress, StringComparison.OrdinalIgnoreCase))
        {
            await metricsStore.DeleteAsync(id, cancellationToken);
        }

        if (result == DeviceWriteResult.Success)
        {
            foreach (var record in linkedIpamRecords)
            {
                record.IpAddress = updated.IpAddress;
                record.DeviceName = updated.Name;
                record.DeviceType = updated.Type.ToString();
                record.LocationId = updated.LocationId;
                record.BuildingId = updated.BuildingId;
                record.GroupId = updated.GroupId;
                record.Location = updated.Location;
                record.Building = updated.Building;
                record.Group = updated.Group;
                await ipam.SaveRecordAsync(record, cancellationToken);
            }
        }

        if (result == DeviceWriteResult.Success)
        {
            cycleTrigger.RequestImmediateCycle();
        }

        return new(result);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await devices.GetAsync(id, cancellationToken) is null) return false;
        await maps.RemoveDeviceReferencesAsync(id, cancellationToken);
        var deleted = await devices.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            await ipam.DetachDeviceAsync(id, cancellationToken);
            await metricsStore.DeleteAsync(id, cancellationToken);
            cycleTrigger.RequestImmediateCycle();
        }

        return deleted;
    }

    public async Task<DeviceWriteResult> SetMonitoringEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var result = await devices.SetMonitoringEnabledAsync(id, enabled, cancellationToken);
        if (result == DeviceWriteResult.Success)
        {
            cycleTrigger.RequestImmediateCycle();
        }

        return result;
    }

    public async Task<IReadOnlyList<NetworkDevice>> GetForMonitoringAsync(CancellationToken cancellationToken = default)
    {
        var configured = await devices.GetAllAsync(cancellationToken);
        var monitoredDevices = configured.Where(device => device.MonitoringEnabled).ToArray();
        var currentMetrics = await metricsStore.GetManyAsync(monitoredDevices.Select(device => device.Id), cancellationToken);
        var monitored = new List<NetworkDevice>(monitoredDevices.Length);
        foreach (var device in monitoredDevices)
        {
            var clone = Clone(device);
            clone.LatestSnmpMetrics = currentMetrics.GetValueOrDefault(device.Id);
            if (clone.MonitoringMethod == MonitoringMethod.IcmpAndSnmp)
            {
                try
                {
                    if (clone.Snmp.Version is SnmpVersion.V1 or SnmpVersion.V2c)
                    {
                        clone.Snmp.ProtectedCommunity = UnprotectOrNull(clone.Snmp.ProtectedCommunity);
                    }
                    else
                    {
                        if (clone.Snmp.SecurityLevel is SnmpSecurityLevel.AuthNoPriv or SnmpSecurityLevel.AuthPriv)
                        {
                            clone.Snmp.ProtectedAuthenticationPassword = UnprotectOrNull(clone.Snmp.ProtectedAuthenticationPassword);
                        }

                        if (clone.Snmp.SecurityLevel == SnmpSecurityLevel.AuthPriv)
                        {
                            clone.Snmp.ProtectedPrivacyPassword = UnprotectOrNull(clone.Snmp.ProtectedPrivacyPassword);
                        }
                    }
                }
                catch (CryptographicException)
                {
                    clone.LastCheckError = "SNMP credentials could not be decrypted with the active data protection keys.";
                }
            }

            monitored.Add(clone);
        }

        return monitored;
    }

    public async Task<IReadOnlyList<MonitoringHistoryEntry>> GetHistoryAsync(
        Guid id,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (await devices.GetAsync(id, cancellationToken) is null)
        {
            return [];
        }

        return await history.GetForDeviceAsync(id, limit, cancellationToken);
    }

    public async Task<DeviceImportResult> ImportAsync(
        IReadOnlyCollection<NetworkDevice> imported,
        CancellationToken cancellationToken = default)
    {
        var sanitized = new List<NetworkDevice>();
        foreach (var device in imported)
        {
            if (string.IsNullOrWhiteSpace(device.Name) || device.Name.Length > 120 ||
                !IPAddress.TryParse(device.IpAddress, out var address) || !Enum.IsDefined(device.Type))
            {
                continue;
            }

            if (new[] { device.Location, device.Building, device.Group }.Any(value => value?.Length > 120))
            {
                continue;
            }

            var assignment = await structure.ResolveOrCreateLegacyAssignmentAsync(
                device.Location,
                device.Building,
                device.Group,
                cancellationToken);

            device.Id = Guid.NewGuid();
            device.Name = device.Name.Trim();
            device.IpAddress = address.ToString();
            device.MonitoringMethod = MonitoringMethod.Icmp;
            device.LocationId = assignment.LocationId;
            device.BuildingId = assignment.BuildingId;
            device.GroupId = assignment.GroupId;
            device.Snmp = new SnmpConfiguration();
            device.Status = DeviceStatus.Unknown;
            device.CreatedUtc = DateTimeOffset.UtcNow;
            device.UpdatedUtc = device.CreatedUtc;
            sanitized.Add(device);
        }

        return await ImportAndSignalAsync(sanitized, cancellationToken);
    }

    private async Task<DeviceImportResult> ImportAndSignalAsync(List<NetworkDevice> sanitized, CancellationToken cancellationToken)
    {
        var result = await devices.ImportAsync(sanitized, cancellationToken);
        if (result.Imported > 0)
        {
            cycleTrigger.RequestImmediateCycle();
        }

        return result;
    }

    private string? Validate(DeviceEditInput input, bool isUpdate, NetworkDevice? existing)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 120)
        {
            return "Enter a device name up to 120 characters long.";
        }

        if (!IPAddress.TryParse(input.IpAddress?.Trim(), out _))
        {
            return "Enter a valid IPv4 or IPv6 address.";
        }

        if (!Enum.IsDefined(input.Type) || !Enum.IsDefined(input.MonitoringMethod))
        {
            return "Choose a supported device type and monitoring method.";
        }

        if (input.MonitoringMethod == MonitoringMethod.IcmpAndSnmp)
        {
            if (input.SnmpPort is < 1 or > 65535 || !Enum.IsDefined(input.SnmpVersion))
            {
                return "Enter a valid SNMP port and version.";
            }

            var config = existing?.Snmp;
            if (input.SnmpVersion is SnmpVersion.V1 or SnmpVersion.V2c)
            {
                if (string.IsNullOrWhiteSpace(input.SnmpCommunity) && string.IsNullOrWhiteSpace(config?.ProtectedCommunity))
                {
                    return $"An SNMP {input.SnmpVersion} community string is required.";
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.SnmpUserName) || input.SnmpUserName.Trim().Length > 128 ||
                    !Enum.IsDefined(input.SnmpSecurityLevel) || !Enum.IsDefined(input.SnmpAuthenticationProtocol) ||
                    !Enum.IsDefined(input.SnmpPrivacyProtocol))
                {
                    return "Enter a valid SNMP v3 username, security level, and protocols.";
                }

                if (input.SnmpSecurityLevel is SnmpSecurityLevel.AuthNoPriv or SnmpSecurityLevel.AuthPriv &&
                    string.IsNullOrWhiteSpace(input.SnmpAuthenticationPassword) && string.IsNullOrWhiteSpace(config?.ProtectedAuthenticationPassword))
                {
                    return "An SNMP v3 authentication password is required for the selected security level.";
                }

                if (input.SnmpSecurityLevel is SnmpSecurityLevel.AuthNoPriv or SnmpSecurityLevel.AuthPriv &&
                    !string.IsNullOrWhiteSpace(input.SnmpAuthenticationPassword) && input.SnmpAuthenticationPassword.Length < 8)
                {
                    return "SNMP v3 authentication passwords must contain at least 8 characters.";
                }

                if (input.SnmpSecurityLevel == SnmpSecurityLevel.AuthPriv &&
                    string.IsNullOrWhiteSpace(input.SnmpPrivacyPassword) && string.IsNullOrWhiteSpace(config?.ProtectedPrivacyPassword))
                {
                    return "An SNMP v3 privacy password is required for authPriv security.";
                }

                if (input.SnmpSecurityLevel == SnmpSecurityLevel.AuthPriv &&
                    !string.IsNullOrWhiteSpace(input.SnmpPrivacyPassword) && input.SnmpPrivacyPassword.Length < 8)
                {
                    return "SNMP v3 privacy passwords must contain at least 8 characters.";
                }
            }
        }

        return null;
    }

    private async Task<string?> ValidateAssignmentAsync(DeviceEditInput input, CancellationToken cancellationToken)
    {
        if (input.LocationId is null && input.BuildingId is null && input.GroupId is null)
        {
            return null;
        }

        var hierarchy = await structure.GetAsync(cancellationToken);
        if (input.LocationId is { } locationId)
        {
            var location = hierarchy.Locations.FirstOrDefault(item => item.Id == locationId);
            if (location is null || location.Kind == LocationKind.Building)
            {
                return "Choose an existing location or area.";
            }
        }

        if (input.BuildingId is { } buildingId)
        {
            var building = hierarchy.Locations.FirstOrDefault(item => item.Id == buildingId);
            if (building is null || building.Kind != LocationKind.Building ||
                (input.LocationId is { } parentId && !IsDescendantLocation(hierarchy.Locations, buildingId, parentId)))
            {
                return "Choose a building under the selected location.";
            }
        }

        if (input.GroupId is { } groupId && !hierarchy.Groups.Any(item => item.Id == groupId))
        {
            return "Choose an existing group.";
        }

        return null;
    }

    private NetworkDevice CreateEntity(DeviceEditInput input, NetworkDevice? existing = null)
    {
        var ip = IPAddress.Parse(input.IpAddress.Trim()).ToString();
        var configuration = new SnmpConfiguration
        {
            Port = input.SnmpPort,
            Version = input.SnmpVersion,
            UserName = TrimOrNull(input.SnmpUserName),
            SecurityLevel = input.SnmpSecurityLevel,
            AuthenticationProtocol = input.SnmpAuthenticationProtocol,
            PrivacyProtocol = input.SnmpPrivacyProtocol,
            ProtectedCommunity = ProtectOrPreserve(input.SnmpCommunity, existing?.Snmp.ProtectedCommunity),
            ProtectedAuthenticationPassword = ProtectOrPreserve(input.SnmpAuthenticationPassword, existing?.Snmp.ProtectedAuthenticationPassword),
            ProtectedPrivacyPassword = ProtectOrPreserve(input.SnmpPrivacyPassword, existing?.Snmp.ProtectedPrivacyPassword)
        };

        return new NetworkDevice
        {
            Id = input.Id ?? Guid.NewGuid(),
            Name = input.Name.Trim(),
            IpAddress = ip,
            Type = input.Type,
            MonitoringMethod = input.MonitoringMethod,
            Location = TrimOrNull(input.Location),
            LocationId = input.LocationId,
            Group = TrimOrNull(input.Group),
            GroupId = input.GroupId,
            Building = TrimOrNull(input.Building),
            BuildingId = input.BuildingId,
            Description = TrimOrNull(input.Description),
            MonitoringEnabled = input.MonitoringEnabled,
            Snmp = configuration,
            CreatedUtc = existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private string? ProtectOrPreserve(string? plaintext, string? existing)
    {
        return string.IsNullOrWhiteSpace(plaintext) ? existing : secretProtector.Protect(plaintext);
    }

    private string? UnprotectOrNull(string? protectedValue) =>
        string.IsNullOrWhiteSpace(protectedValue) ? null : secretProtector.Unprotect(protectedValue);

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static NetworkDevice Clone(NetworkDevice source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        IpAddress = source.IpAddress,
        Type = source.Type,
        MonitoringMethod = source.MonitoringMethod,
        Location = source.Location,
        LocationId = source.LocationId,
        Group = source.Group,
        GroupId = source.GroupId,
        Building = source.Building,
        BuildingId = source.BuildingId,
        Description = source.Description,
        MonitoringEnabled = source.MonitoringEnabled,
        Snmp = new SnmpConfiguration
        {
            Port = source.Snmp.Port,
            Version = source.Snmp.Version,
            ProtectedCommunity = source.Snmp.ProtectedCommunity,
            UserName = source.Snmp.UserName,
            SecurityLevel = source.Snmp.SecurityLevel,
            AuthenticationProtocol = source.Snmp.AuthenticationProtocol,
            ProtectedAuthenticationPassword = source.Snmp.ProtectedAuthenticationPassword,
            PrivacyProtocol = source.Snmp.PrivacyProtocol,
            ProtectedPrivacyPassword = source.Snmp.ProtectedPrivacyPassword
        },
        CreatedUtc = source.CreatedUtc,
        UpdatedUtc = source.UpdatedUtc,
        Status = source.Status,
        LastCheckUtc = source.LastCheckUtc,
        LastSuccessfulCheckUtc = source.LastSuccessfulCheckUtc,
        LastFailedCheckUtc = source.LastFailedCheckUtc,
        CurrentStateSinceUtc = source.CurrentStateSinceUtc,
        ResponseTimeMilliseconds = source.ResponseTimeMilliseconds,
        LastCheckError = source.LastCheckError,
        LatestSnmpMetrics = source.LatestSnmpMetrics
    };

    private static DeviceInfo ToInfo(NetworkDevice device, SnmpMetrics? latestMetrics, InventoryStructureDocument hierarchy)
    {
        var location = device.LocationId is { } locationId ? hierarchy.Locations.FirstOrDefault(item => item.Id == locationId) : null;
        var building = device.BuildingId is { } buildingId ? hierarchy.Locations.FirstOrDefault(item => item.Id == buildingId) : null;
        var group = device.GroupId is { } groupId ? hierarchy.Groups.FirstOrDefault(item => item.Id == groupId) : null;
        return new DeviceInfo
        {
        Id = device.Id,
        Name = device.Name,
        IpAddress = device.IpAddress,
        Type = device.Type,
        MonitoringMethod = device.MonitoringMethod,
        LocationId = device.LocationId,
        BuildingId = device.BuildingId,
        GroupId = device.GroupId,
        Location = location is null ? device.Location : InventoryStructureService.BuildLocationPath(hierarchy.Locations, location.Id),
        Group = group is null ? device.Group : InventoryStructureService.BuildGroupPath(hierarchy.Groups, group.Id),
        Building = building is null ? device.Building : InventoryStructureService.BuildLocationPath(hierarchy.Locations, building.Id),
        Description = device.Description,
        MonitoringEnabled = device.MonitoringEnabled,
        SnmpVersion = device.Snmp.Version,
        SnmpPort = device.Snmp.Port,
        SnmpUserName = device.Snmp.UserName,
        SnmpSecurityLevel = device.Snmp.SecurityLevel,
        SnmpAuthenticationProtocol = device.Snmp.AuthenticationProtocol,
        SnmpPrivacyProtocol = device.Snmp.PrivacyProtocol,
        HasSnmpCommunity = !string.IsNullOrEmpty(device.Snmp.ProtectedCommunity),
        HasSnmpAuthenticationPassword = !string.IsNullOrEmpty(device.Snmp.ProtectedAuthenticationPassword),
        HasSnmpPrivacyPassword = !string.IsNullOrEmpty(device.Snmp.ProtectedPrivacyPassword),
        CreatedUtc = device.CreatedUtc,
        UpdatedUtc = device.UpdatedUtc,
        Status = device.Status,
        LastCheckUtc = device.LastCheckUtc,
        LastSuccessfulCheckUtc = device.LastSuccessfulCheckUtc,
        LastFailedCheckUtc = device.LastFailedCheckUtc,
        CurrentStateSinceUtc = device.CurrentStateSinceUtc,
        ResponseTimeMilliseconds = device.ResponseTimeMilliseconds,
        LastCheckError = device.LastCheckError,
        LatestSnmpMetrics = latestMetrics
        };
    }

    private static bool IsDescendantLocation(IReadOnlyCollection<ManagedLocation> locations, Guid childId, Guid ancestorId)
    {
        var byId = locations.ToDictionary(item => item.Id);
        var current = childId;
        var seen = new HashSet<Guid>();
        while (seen.Add(current) && byId.TryGetValue(current, out var node) && node.ParentLocationId is { } parent)
        {
            if (parent == ancestorId) return true;
            current = parent;
        }

        return false;
    }
}
