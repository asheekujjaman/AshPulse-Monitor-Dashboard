using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Presentation;

public sealed class AppPresentationService(IJsonDocumentStore documents) : IAppPresentationService
{
    public async Task<AppPresentation> GetAsync(CancellationToken cancellationToken = default)
    {
        var brandingTask = documents.ReadAsync("branding.json", static () => new BrandingSettings(), cancellationToken);
        var themeTask = documents.ReadAsync("themes.json", static () => new ThemeSettings(), cancellationToken);
        await Task.WhenAll(brandingTask, themeTask);

        return new AppPresentation(await brandingTask, await themeTask);
    }

    public async Task<bool> SetDefaultThemeAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.Equals(name, ThemePresetCatalog.Custom, StringComparison.Ordinal)) return true;
        if (!ThemePresetCatalog.IsPreset(name)) return false;
        await documents.WriteAsync("themes.json", ThemePresetCatalog.Create(name), cancellationToken);
        return true;
    }
}
