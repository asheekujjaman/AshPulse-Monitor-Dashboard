using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Core.Persistence;

public interface IUserRepository
{
    Task<IReadOnlyList<LocalUser>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<LocalUser?> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalUser?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    Task RegisterFailedLoginAsync(string id, DateTimeOffset nowUtc, int maximumAttempts, TimeSpan lockoutDuration, CancellationToken cancellationToken = default);

    Task ResetFailedLoginsAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> TryAddFirstAsync(LocalUser user, CancellationToken cancellationToken = default);

    Task<UserWriteResult> CreateAsync(LocalUser user, CancellationToken cancellationToken = default);

    Task<UserWriteResult> UpdateAsync(LocalUser user, CancellationToken cancellationToken = default);

    Task<UserWriteResult> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public enum UserWriteResult
{
    Success,
    NotFound,
    DuplicateUserName,
    LastActiveAdministrator,
    ValidationFailed
}
