namespace UniversalITMonitor.Application.Presentation;

public interface IAppPresentationService
{
    Task<AppPresentation> GetAsync(CancellationToken cancellationToken = default);

    Task<bool> SetDefaultThemeAsync(string name, CancellationToken cancellationToken = default);
}
