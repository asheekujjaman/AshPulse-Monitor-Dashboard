namespace UniversalITMonitor.Application.Accounts;

public sealed record AuthenticatedAccount(
    string Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles);
