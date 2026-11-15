namespace UniversalITMonitor.Core.Models;

public sealed class LocalUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = [];

    public bool IsEnabled { get; set; } = true;

    public int AccessFailedCount { get; set; }

    public DateTimeOffset? LockoutEndUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
