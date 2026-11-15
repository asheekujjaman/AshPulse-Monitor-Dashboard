using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Setup;

public sealed class InitialDataSeeder(IJsonDocumentStore documents)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await documents.EnsureAsync("users.json", new UserDocument(), cancellationToken);
        await documents.EnsureAsync("roles.json", new RoleDocument
        {
            Roles =
            [
                new RoleDefinition
                {
                    Name = SystemRoles.Administrator,
                    Description = "Full application configuration and administration access."
                },
                new RoleDefinition
                {
                    Name = SystemRoles.Operator,
                    Description = "Operational access for managing network devices."
                },
                new RoleDefinition
                {
                    Name = SystemRoles.ReadOnly,
                    Description = "Read-only access to monitoring and inventory information."
                }
            ]
        }, cancellationToken);
        await documents.EnsureAsync("settings.json", new ApplicationSettings(), cancellationToken);
        await documents.EnsureAsync("devices.json", new DeviceDocument(), cancellationToken);
        await documents.EnsureAsync("inventory-structure.json", new InventoryStructureDocument(), cancellationToken);
        await documents.EnsureAsync("network-maps.json", new NetworkMapDocument(), cancellationToken);
        await documents.EnsureAsync("ipam.json", new IpamDocument(), cancellationToken);
        await documents.EnsureAsync("branding.json", new BrandingSettings(), cancellationToken);
        await documents.EnsureAsync("themes.json", new ThemeSettings(), cancellationToken);
        await documents.EnsureAsync("backup-settings.json", new BackupSettingsDocument(), cancellationToken);
        await documents.EnsureAsync("backup-history.json", new BackupHistoryDocument(), cancellationToken);
    }
}
