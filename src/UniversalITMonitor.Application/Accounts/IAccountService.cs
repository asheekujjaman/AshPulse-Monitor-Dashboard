namespace UniversalITMonitor.Application.Accounts;

public interface IAccountService
{
    Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default);

    Task<bool> TryCreateFirstAdministratorAsync(
        string displayName,
        string userName,
        string password,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedAccount?> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default);
}
