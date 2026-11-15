using Microsoft.AspNetCore.DataProtection;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Devices;

public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("UniversalITMonitor.DeviceSecrets.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
