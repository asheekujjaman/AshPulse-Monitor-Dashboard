using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonUserRepository(IJsonDocumentStore documents) : IUserRepository
{
    public async Task<IReadOnlyList<LocalUser>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var userDocument = await documents.ReadAsync("users.json", static () => new UserDocument(), cancellationToken);
        return userDocument.Users.ToArray();
    }

    public async Task<LocalUser?> FindByUserNameAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        var userDocument = await documents.ReadAsync("users.json", static () => new UserDocument(), cancellationToken);
        return userDocument.Users.FirstOrDefault(user =>
            string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<LocalUser?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var userDocument = await documents.ReadAsync("users.json", static () => new UserDocument(), cancellationToken);
        return userDocument.Users.FirstOrDefault(user => string.Equals(user.Id, id, StringComparison.Ordinal));
    }

    public Task RegisterFailedLoginAsync(
        string id,
        DateTimeOffset nowUtc,
        int maximumAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<UserDocument, bool>(
            "users.json",
            static () => new UserDocument(),
            document =>
            {
                var user = document.Users.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
                if (user is null || !user.IsEnabled) return (document, false);
                if (user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > nowUtc) return (document, false);
                if (user.LockoutEndUtc is not null && user.LockoutEndUtc <= nowUtc) user.AccessFailedCount = 0;
                user.AccessFailedCount = Math.Min(maximumAttempts, user.AccessFailedCount + 1);
                user.LockoutEndUtc = user.AccessFailedCount >= maximumAttempts ? nowUtc.Add(lockoutDuration) : null;
                return (document, true);
            },
            cancellationToken);

    public Task ResetFailedLoginsAsync(string id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<UserDocument, bool>(
            "users.json",
            static () => new UserDocument(),
            document =>
            {
                var user = document.Users.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
                if (user is null) return (document, false);
                user.AccessFailedCount = 0;
                user.LockoutEndUtc = null;
                return (document, true);
            },
            cancellationToken);

    public Task<bool> TryAddFirstAsync(LocalUser user, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<UserDocument, bool>(
            "users.json",
            static () => new UserDocument(),
            document =>
            {
                if (document.Users.Count > 0)
                {
                    return (document, false);
                }

                document.Users.Add(user);
                return (document, true);
            },
            cancellationToken);

    public Task<UserWriteResult> CreateAsync(LocalUser user, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<UserDocument, UserWriteResult>(
            "users.json",
            static () => new UserDocument(),
            document =>
            {
                if (document.Users.Any(existing => string.Equals(existing.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (document, UserWriteResult.DuplicateUserName);
                }

                document.Users.Add(user);
                return (document, UserWriteResult.Success);
            },
            cancellationToken);

    public Task<UserWriteResult> UpdateAsync(LocalUser user, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<UserDocument, UserWriteResult>(
            "users.json",
            static () => new UserDocument(),
            document =>
            {
                var index = document.Users.FindIndex(existing => string.Equals(existing.Id, user.Id, StringComparison.Ordinal));
                if (index < 0)
                {
                    return (document, UserWriteResult.NotFound);
                }

                if (document.Users.Any(existing => existing.Id != user.Id &&
                    string.Equals(existing.UserName, user.UserName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (document, UserWriteResult.DuplicateUserName);
                }

                var existing = document.Users[index];
                if (IsActiveAdministrator(existing) && !IsActiveAdministrator(user) &&
                    document.Users.Count(IsActiveAdministrator) <= 1)
                {
                    return (document, UserWriteResult.LastActiveAdministrator);
                }

                user.CreatedUtc = existing.CreatedUtc;
                user.AccessFailedCount = existing.AccessFailedCount;
                user.LockoutEndUtc = existing.LockoutEndUtc;
                document.Users[index] = user;
                return (document, UserWriteResult.Success);
            },
            cancellationToken);

    public Task<UserWriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<UserDocument, UserWriteResult>(
            "users.json",
            static () => new UserDocument(),
            document =>
            {
                var user = document.Users.FirstOrDefault(existing => string.Equals(existing.Id, id, StringComparison.Ordinal));
                if (user is null)
                {
                    return (document, UserWriteResult.NotFound);
                }

                if (IsActiveAdministrator(user) && document.Users.Count(IsActiveAdministrator) <= 1)
                {
                    return (document, UserWriteResult.LastActiveAdministrator);
                }

                document.Users.Remove(user);
                return (document, UserWriteResult.Success);
            },
            cancellationToken);

    private static bool IsActiveAdministrator(LocalUser user) =>
        user.IsEnabled && user.Roles.Contains(SystemRoles.Administrator, StringComparer.OrdinalIgnoreCase);
}
