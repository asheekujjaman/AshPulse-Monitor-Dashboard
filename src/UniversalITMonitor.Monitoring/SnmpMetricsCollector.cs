using System.Globalization;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Monitoring;

namespace UniversalITMonitor.Monitoring;

public sealed class SnmpMetricsCollector : ISnmpMetricsCollector
{
    private const string SystemDescriptionOid = "1.3.6.1.2.1.1.1.0";
    private const string SystemObjectIdOid = "1.3.6.1.2.1.1.2.0";
    private const string SystemUptimeOid = "1.3.6.1.2.1.1.3.0";
    private const string SystemNameOid = "1.3.6.1.2.1.1.5.0";
    private const string CpuLoadTableOid = "1.3.6.1.2.1.25.3.3.1.2";
    private const string StorageTableOid = "1.3.6.1.2.1.25.2.3.1";
    private const string IfOperStatusTableOid = "1.3.6.1.2.1.2.2.1.8";
    private const string IfXTableOid = "1.3.6.1.2.1.31.1.1.1";
    private const string IfTableOid = "1.3.6.1.2.1.2.2.1";
    private const string IfDescrTableOid = "1.3.6.1.2.1.2.2.1.2";
    private const string IfAdminStatusTableOid = "1.3.6.1.2.1.2.2.1.7";
    private const string IfInOctetsTableOid = "1.3.6.1.2.1.2.2.1.10";
    private const string IfOutOctetsTableOid = "1.3.6.1.2.1.2.2.1.16";
    private const string IfSpeedTableOid = "1.3.6.1.2.1.2.2.1.5";
    private const string IfAliasTableOid = "1.3.6.1.2.1.31.1.1.1.18";
    private const string IfNameColumnOid = IfXTableOid + ".1";
    private const string IfHCInOctetsColumnOid = IfXTableOid + ".6";
    private const string IfHCOutOctetsColumnOid = IfXTableOid + ".10";
    private const string IfHighSpeedColumnOid = IfXTableOid + ".15";
    private const string BridgePortIfIndexTableOid = "1.3.6.1.2.1.17.1.4.1.2";
    private const string Dot1qPvidTableOid = "1.3.6.1.2.1.17.7.1.4.5.1.1";
    private const string CiscoVmVlanTableOid = "1.3.6.1.4.1.9.9.68.1.2.2.1.2";
    private const string RealMemoryTypeOid = "1.3.6.1.2.1.25.2.1.2";
    private const int RequestTimeoutMilliseconds = 5000;

    static SnmpMetricsCollector()
    {
        // Some network agents reject negative request IDs and return malformed BER data.
        Messenger.UseFullRange = false;
    }

    public async Task<SnmpMetrics> CollectAsync(NetworkDevice device, CancellationToken cancellationToken = default)
    {
        var collectedUtc = DateTimeOffset.UtcNow;
        var metrics = new SnmpMetrics { CollectedUtc = collectedUtc };
        if (device.MonitoringMethod != MonitoringMethod.IcmpAndSnmp)
        {
            return metrics;
        }

        if (!IPAddress.TryParse(device.IpAddress, out var address))
        {
            metrics.CollectionError = "The device IP address is invalid.";
            return metrics;
        }

        var configuration = device.Snmp;
        var endpoint = new IPEndPoint(address, configuration.Port);
        try
        {
            var session = await CreateSessionAsync(configuration, endpoint, cancellationToken);
            var system = await GetAsync(
                session,
                [SystemDescriptionOid, SystemObjectIdOid, SystemNameOid, SystemUptimeOid],
                cancellationToken);

            metrics.SystemDescription = GetValue(system, SystemDescriptionOid);
            var systemObjectId = GetValue(system, SystemObjectIdOid);
            metrics.SystemName = GetValue(system, SystemNameOid);
            metrics.SystemUptimeHundredths = ParseUInt(GetValue(system, SystemUptimeOid));

            var walkErrors = new List<string>();
            var cpuValues = await TryWalkAsync(session, CpuLoadTableOid, cancellationToken, walkErrors);
            var cpuSamples = cpuValues.Select(variable => ParseDouble(variable.Data.ToString()))
                .Where(value => value is >= 0 and <= 100)
                .Select(value => value!.Value)
                .ToArray();
            if (cpuSamples.Length > 0)
            {
                metrics.CpuUtilizationPercent = Math.Round(cpuSamples.Average(), 1);
            }

            var storageValues = await TryWalkAsync(session, StorageTableOid, cancellationToken, walkErrors);
            metrics.MemoryUtilizationPercent = GetMemoryUtilization(storageValues);

            var interfaceValues = new List<Variable>();
            foreach (var columnOid in new[] { IfNameColumnOid, IfHCInOctetsColumnOid, IfHCOutOctetsColumnOid, IfHighSpeedColumnOid, IfAliasTableOid })
            {
                interfaceValues.AddRange(await TryWalkAsync(session, columnOid, cancellationToken, walkErrors));
            }
            var legacyInterfaceValues = new List<Variable>();
            foreach (var columnOid in new[] { IfDescrTableOid, IfAdminStatusTableOid, IfOperStatusTableOid, IfInOctetsTableOid, IfOutOctetsTableOid, IfSpeedTableOid })
            {
                legacyInterfaceValues.AddRange(await TryWalkAsync(session, columnOid, cancellationToken, walkErrors));
            }
            // Some Cisco platforms return a generic or model-only sysDescr. sysObjectID is
            // the stable vendor identifier (Cisco enterprise number 9), so use both values.
            var isCisco = metrics.SystemDescription?.Contains("Cisco", StringComparison.OrdinalIgnoreCase) == true ||
                systemObjectId?.StartsWith("1.3.6.1.4.1.9.", StringComparison.Ordinal) == true;
            var ciscoVlanValues = isCisco
                ? await TryWalkAsync(session, CiscoVmVlanTableOid, cancellationToken, walkErrors)
                : Array.Empty<Variable>();
            var bridgePortValues = await TryWalkAsync(session, BridgePortIfIndexTableOid, cancellationToken, walkErrors);
            var pvidValues = await TryWalkAsync(session, Dot1qPvidTableOid, cancellationToken, walkErrors);
            metrics.Interfaces = BuildInterfaceMetrics(interfaceValues, legacyInterfaceValues, ciscoVlanValues,
                bridgePortValues, pvidValues, device.LatestSnmpMetrics, collectedUtc);
            if (walkErrors.Count > 0 && metrics.Interfaces.Count == 0)
            {
                var details = string.Join(" | ", walkErrors.Take(4));
                metrics.CollectionError = $"SNMP responded, but interface tables were unavailable. Check the SNMP view/permissions and whether IF-MIB is enabled on the device. {details}";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            metrics.CollectionError = "The SNMP agent did not respond before the request timed out.";
        }
        catch (System.TimeoutException)
        {
            metrics.CollectionError = "The SNMP agent did not respond before the request timed out.";
        }
        catch (Exception exception)
        {
            metrics.CollectionError = $"SNMP request failed: {SafeExceptionMessage(exception)} Check the SNMP version, credentials, UDP port, and device access rules.";
        }

        return metrics;
    }

    private static async Task<SnmpSession> CreateSessionAsync(
        SnmpConfiguration configuration,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        if (configuration.Version is SnmpVersion.V1 or SnmpVersion.V2c)
        {
            if (string.IsNullOrEmpty(configuration.ProtectedCommunity))
            {
                throw new InvalidOperationException($"The SNMP {configuration.Version} community string is missing.");
            }

            var version = configuration.Version == SnmpVersion.V1 ? VersionCode.V1 : VersionCode.V2;
            return new SnmpSession(endpoint, new OctetString(configuration.ProtectedCommunity), null, null, Version: version);
        }

        if (string.IsNullOrWhiteSpace(configuration.UserName))
        {
            throw new InvalidOperationException("The SNMP v3 username is missing.");
        }

        var provider = CreatePrivacyProvider(configuration);
        using var timeout = CreateTimeoutToken(cancellationToken);
        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        var report = await discovery.GetResponseAsync(endpoint, timeout.Token);
        return new SnmpSession(endpoint, OctetString.Empty, provider, report, configuration.UserName);
    }

    private static IPrivacyProvider CreatePrivacyProvider(SnmpConfiguration configuration)
    {
        if (configuration.SecurityLevel == SnmpSecurityLevel.NoAuthNoPriv)
        {
            return DefaultPrivacyProvider.DefaultPair;
        }

        if (string.IsNullOrEmpty(configuration.ProtectedAuthenticationPassword))
        {
            throw new InvalidOperationException("The SNMP v3 authentication password is missing.");
        }

        var authentication = CreateAuthenticationProvider(
            configuration.AuthenticationProtocol,
            configuration.ProtectedAuthenticationPassword);

        if (configuration.SecurityLevel == SnmpSecurityLevel.AuthNoPriv)
        {
            return new DefaultPrivacyProvider(authentication);
        }

        if (string.IsNullOrEmpty(configuration.ProtectedPrivacyPassword))
        {
            throw new InvalidOperationException("The SNMP v3 privacy password is missing.");
        }

        var privacyPassword = new OctetString(configuration.ProtectedPrivacyPassword);
        return configuration.PrivacyProtocol switch
        {
            SnmpPrivacyProtocol.Des => CreateLegacyDes(privacyPassword, authentication),
            SnmpPrivacyProtocol.Aes128 => new AESPrivacyProvider(privacyPassword, authentication),
            SnmpPrivacyProtocol.Aes192 => new AES192PrivacyProvider(privacyPassword, authentication),
            SnmpPrivacyProtocol.Aes256 => new AES256PrivacyProvider(privacyPassword, authentication),
            _ => throw new InvalidOperationException("The selected SNMP v3 privacy protocol is not supported.")
        };
    }

    private static IAuthenticationProvider CreateAuthenticationProvider(
        SnmpAuthenticationProtocol protocol,
        string password)
    {
        var phrase = new OctetString(password);
        return protocol switch
        {
            SnmpAuthenticationProtocol.Md5 => CreateLegacyMd5(phrase),
            SnmpAuthenticationProtocol.Sha1 => CreateLegacySha1(phrase),
            SnmpAuthenticationProtocol.Sha256 => new SHA256AuthenticationProvider(phrase),
            SnmpAuthenticationProtocol.Sha384 => new SHA384AuthenticationProvider(phrase),
            SnmpAuthenticationProtocol.Sha512 => new SHA512AuthenticationProvider(phrase),
            _ => throw new InvalidOperationException("The selected SNMP v3 authentication protocol is not supported.")
        };
    }

#pragma warning disable CS0618 // Older network appliances still require these user-selectable SNMP algorithms.
    private static IAuthenticationProvider CreateLegacyMd5(OctetString phrase) => new MD5AuthenticationProvider(phrase);

    private static IAuthenticationProvider CreateLegacySha1(OctetString phrase) => new SHA1AuthenticationProvider(phrase);

    private static IPrivacyProvider CreateLegacyDes(OctetString password, IAuthenticationProvider authentication) =>
        new DESPrivacyProvider(password, authentication);
#pragma warning restore CS0618

    private static async Task<IReadOnlyList<Variable>> GetAsync(
        SnmpSession session,
        IReadOnlyCollection<string> oids,
        CancellationToken cancellationToken)
    {
        var variables = oids.Select(oid => new Variable(new ObjectIdentifier(oid))).ToList();
        using var timeout = CreateTimeoutToken(cancellationToken);
        if (session.PrivacyProvider is null)
        {
            return (IReadOnlyList<Variable>)await Messenger.GetAsync(
                session.Version,
                session.Endpoint,
                session.Community,
                variables,
                timeout.Token);
        }

        var request = new GetRequestMessage(
            VersionCode.V3,
            Messenger.NextMessageId,
            Messenger.NextRequestId,
            new OctetString(session.UserName!),
            OctetString.Empty,
            variables,
            session.PrivacyProvider,
            Messenger.MaxMessageSize,
            session.Report!);
        var response = await request.GetResponseAsync(session.Endpoint, timeout.Token);
        var pdu = response.Pdu();
        if (pdu.ErrorStatus.ToInt32() != 0)
        {
            throw new InvalidOperationException("The SNMP agent returned an error response.");
        }

        return pdu.Variables.ToArray();
    }

    private static async Task<IReadOnlyList<Variable>> WalkAsync(
        SnmpSession session,
        string rootOid,
        CancellationToken cancellationToken)
    {
        var variables = new List<Variable>();
        using var timeout = CreateTimeoutToken(cancellationToken);
        if (session.PrivacyProvider is null)
        {
            if (session.Version == VersionCode.V1)
            {
                await Messenger.WalkAsync(
                    VersionCode.V1,
                    session.Endpoint,
                    session.Community,
                    new ObjectIdentifier(rootOid),
                    variables,
                    WalkMode.WithinSubtree,
                    timeout.Token);
            }
            else
            {
                await Messenger.BulkWalkAsync(
                    VersionCode.V2,
                    session.Endpoint,
                    session.Community,
                    OctetString.Empty,
                    new ObjectIdentifier(rootOid),
                    variables,
                    20,
                    WalkMode.WithinSubtree,
                    DefaultPrivacyProvider.DefaultPair,
                    new GetRequestMessage(
                        Messenger.NextRequestId,
                        VersionCode.V2,
                        session.Community,
                        [new Variable(new ObjectIdentifier(rootOid))]),
                    timeout.Token);
            }
        }
        else
        {
            await Messenger.BulkWalkAsync(
                VersionCode.V3,
                session.Endpoint,
                OctetString.Empty,
                OctetString.Empty,
                new ObjectIdentifier(rootOid),
                variables,
                10,
                WalkMode.WithinSubtree,
                session.PrivacyProvider,
                session.Report!,
                timeout.Token);
        }

        return variables;
    }

    private static async Task<IReadOnlyList<Variable>> TryWalkAsync(
        SnmpSession session,
        string rootOid,
        CancellationToken cancellationToken,
        ICollection<string> errors)
    {
        try
        {
            return await WalkAsync(session, rootOid, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            errors.Add($"{rootOid}: timed out after {RequestTimeoutMilliseconds} ms");
            return [];
        }
        catch (Exception exception)
        {
            errors.Add($"{rootOid}: {SafeExceptionMessage(exception)}");
            return [];
        }
    }

    private static string SafeExceptionMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Length > 240 ? message[..240] : message;
    }

    private static CancellationTokenSource CreateTimeoutToken(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(RequestTimeoutMilliseconds);
        return source;
    }

    private static string? GetValue(IEnumerable<Variable> values, string oid)
    {
        var value = values.FirstOrDefault(variable => variable.Id.ToString() == oid);
        return value?.Data.ToString();
    }

    private static double? GetMemoryUtilization(IReadOnlyList<Variable> values)
    {
        var types = GetIndexedValues(values, StorageTableOid + ".2");
        var sizes = GetIndexedValues(values, StorageTableOid + ".5");
        var used = GetIndexedValues(values, StorageTableOid + ".6");
        ulong totalUnits = 0;
        ulong usedUnits = 0;

        foreach (var (index, type) in types)
        {
            if (type != RealMemoryTypeOid || !sizes.TryGetValue(index, out var size) || !used.TryGetValue(index, out var utilization))
            {
                continue;
            }

            if (ulong.TryParse(size, CultureInfo.InvariantCulture, out var parsedSize) &&
                ulong.TryParse(utilization, CultureInfo.InvariantCulture, out var parsedUsed))
            {
                totalUnits += parsedSize;
                usedUnits += parsedUsed;
            }
        }

        return totalUnits == 0 ? null : Math.Round(Math.Min(100d, (double)usedUnits * 100 / totalUnits), 1);
    }

    private static List<SnmpInterfaceMetric> BuildInterfaceMetrics(
        IReadOnlyList<Variable> interfaceValues,
        IReadOnlyList<Variable> legacyInterfaceValues,
        IReadOnlyList<Variable> ciscoVlanValues,
        IReadOnlyList<Variable> bridgePortValues,
        IReadOnlyList<Variable> pvidValues,
        SnmpMetrics? previous,
        DateTimeOffset collectedUtc)
    {
        var names = GetIndexedValues(interfaceValues, IfXTableOid + ".1");
        var descriptions = GetIndexedValues(legacyInterfaceValues, IfDescrTableOid);
        foreach (var (index, description) in descriptions)
        {
            if (!names.ContainsKey(index) && !string.IsNullOrWhiteSpace(description)) names[index] = description;
        }
        var aliases = GetIndexedValues(interfaceValues, IfAliasTableOid);
        var inOctets = GetIndexedValues(interfaceValues, IfXTableOid + ".6");
        var outOctets = GetIndexedValues(interfaceValues, IfXTableOid + ".10");
        var speeds = GetIndexedValues(interfaceValues, IfXTableOid + ".15");
        var legacyInOctets = GetIndexedValues(legacyInterfaceValues, IfInOctetsTableOid);
        var legacyOutOctets = GetIndexedValues(legacyInterfaceValues, IfOutOctetsTableOid);
        var legacySpeeds = GetIndexedValues(legacyInterfaceValues, IfSpeedTableOid);
        var states = GetIndexedValues(legacyInterfaceValues, IfOperStatusTableOid);
        var adminStates = GetIndexedValues(legacyInterfaceValues, IfAdminStatusTableOid);
        var ciscoVlans = GetIndexedValues(ciscoVlanValues, CiscoVmVlanTableOid);
        var bridgePorts = GetIndexedValues(bridgePortValues, BridgePortIfIndexTableOid);
        var pvidsByBridgePort = GetIndexedValues(pvidValues, Dot1qPvidTableOid);
        var vlansByIfIndex = new Dictionary<int, int>();
        foreach (var (bridgePort, interfaceIndexText) in bridgePorts)
        {
            if (int.TryParse(interfaceIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interfaceIndex) &&
                pvidsByBridgePort.TryGetValue(bridgePort, out var vlanText) &&
                int.TryParse(vlanText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vlanId) && vlanId is > 0 and <= 4094)
            {
                vlansByIfIndex[interfaceIndex] = vlanId;
            }
        }
        var old = previous?.Interfaces.ToDictionary(metric => metric.Index) ?? [];
        var elapsedSeconds = previous is null ? 0 : (collectedUtc - previous.CollectedUtc).TotalSeconds;
        var result = new List<SnmpInterfaceMetric>();

        foreach (var (index, name) in names.OrderBy(pair => pair.Key).Take(256))
        {
            int? vlanId = null;
            if (ciscoVlans.TryGetValue(index, out var ciscoVlan) && int.TryParse(ciscoVlan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCiscoVlan) && parsedCiscoVlan is > 0 and <= 4094)
            {
                vlanId = parsedCiscoVlan;
            }
            else if (vlansByIfIndex.TryGetValue(index, out var standardVlan) && standardVlan is > 0 and <= 4094)
            {
                vlanId = standardVlan;
            }

            var metric = new SnmpInterfaceMetric
            {
                Index = index,
                Name = name,
                Description = aliases.GetValueOrDefault(index) is { Length: > 0 } alias ? alias : descriptions.GetValueOrDefault(index),
                OperationalStatus = states.TryGetValue(index, out var status) && int.TryParse(status, out var parsedStatus)
                    ? parsedStatus
                    : null,
                AdminStatus = adminStates.TryGetValue(index, out var adminStatus) && int.TryParse(adminStatus, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAdminStatus)
                    ? parsedAdminStatus
                    : null,
                InOctets = ParseCounter(inOctets.GetValueOrDefault(index), legacyInOctets.GetValueOrDefault(index)),
                OutOctets = ParseCounter(outOctets.GetValueOrDefault(index), legacyOutOctets.GetValueOrDefault(index)),
                SpeedBitsPerSecond = ParseSpeed(speeds.GetValueOrDefault(index), legacySpeeds.GetValueOrDefault(index)),
                VlanId = vlanId
            };

            if (elapsedSeconds > 0 && old.TryGetValue(index, out var prior))
            {
                metric.InBitsPerSecond = GetBitsPerSecond(prior.InOctets, metric.InOctets, elapsedSeconds, !inOctets.ContainsKey(index));
                metric.OutBitsPerSecond = GetBitsPerSecond(prior.OutOctets, metric.OutOctets, elapsedSeconds, !outOctets.ContainsKey(index));
            }

            result.Add(metric);
        }

        return result;
    }

    private static ulong? ParseCounter(string? highCapacity, string? legacy) => ParseULong(highCapacity) ?? ParseULong(legacy);

    private static long? ParseSpeed(string? highCapacity, string? legacy)
    {
        if (uint.TryParse(highCapacity, NumberStyles.Integer, CultureInfo.InvariantCulture, out var highSpeed))
        {
            return (long)highSpeed * 1_000_000L;
        }
        return uint.TryParse(legacy, NumberStyles.Integer, CultureInfo.InvariantCulture, out var speed) ? speed : null;
    }

    private static double? GetBitsPerSecond(ulong? previous, ulong? current, double elapsedSeconds, bool isLegacyCounter)
    {
        if (previous is null || current is null || elapsedSeconds <= 0)
        {
            return null;
        }

        ulong delta;
        if (current.Value >= previous.Value)
        {
            delta = current.Value - previous.Value;
        }
        else if (isLegacyCounter && previous <= uint.MaxValue && current <= uint.MaxValue)
        {
            delta = (1UL << 32) - previous.Value + current.Value;
        }
        else
        {
            return null;
        }

        return Math.Round(delta * 8d / elapsedSeconds, 1);
    }

    private static Dictionary<int, string> GetIndexedValues(IEnumerable<Variable> values, string oidPrefix)
    {
        var prefix = oidPrefix + ".";
        return values
            .Select(variable => (Id: variable.Id.ToString(), Value: variable.Data.ToString()))
            .Where(item => item.Id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(item => (Id: item.Id, item.Value))
            .Select(item =>
            {
                var suffix = item.Id[prefix.Length..];
                return (Valid: int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var index), Index: index, item.Value);
            })
            .Where(item => item.Valid)
            .ToDictionary(item => item.Index, item => item.Value);
    }

    private static uint? ParseUInt(string? value) =>
        uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static ulong? ParseULong(string? value) =>
        ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private sealed record SnmpSession(
        IPEndPoint Endpoint,
        OctetString Community,
        IPrivacyProvider? PrivacyProvider,
        ISnmpMessage? Report,
        string? UserName = null,
        VersionCode Version = VersionCode.V3);
}
