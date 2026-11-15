using Microsoft.AspNetCore.DataProtection;

namespace UniversalITMonitor.Application.Notifications;

public sealed class NotificationSecretProtector : INotificationSecretProtector
{
    private readonly IDataProtector _protector;

    public NotificationSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("UniversalITMonitor.NotificationSecrets.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
