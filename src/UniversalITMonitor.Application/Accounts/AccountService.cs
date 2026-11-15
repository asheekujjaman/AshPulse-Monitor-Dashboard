using Microsoft.AspNetCore.Identity;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Accounts;

public sealed class AccountService(
    IUserRepository users,
    IRoleRepository roles,
    IPasswordHasher<LocalUser> passwordHasher) : IAccountService
{
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<bool> HasAnyUsersAsync(CancellationToken cancellationToken = default)
    {
        var allUsers = await users.GetAllAsync(cancellationToken);
        return allUsers.Count > 0;
    }

    public async Task<bool> TryCreateFirstAdministratorAsync(
        string displayName,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (password.Length < 12)
        {
            throw new ArgumentException("Passwords must contain at least 12 characters.", nameof(password));
        }

        var roleNames = (await roles.GetAllAsync(cancellationToken))
            .Select(role => role.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!roleNames.Contains(SystemRoles.Administrator))
        {
            throw new InvalidDataException("The Administrator role is missing from roles.json.");
        }

        var user = new LocalUser
        {
            UserName = userName.Trim(),
            DisplayName = displayName.Trim(),
            Roles = [SystemRoles.Administrator],
            IsEnabled = true,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        return await users.TryAddFirstAsync(user, cancellationToken);
    }

    public async Task<AuthenticatedAccount?> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        var user = await users.FindByUserNameAsync(userName.Trim(), cancellationToken);
        if (user is null || !user.IsEnabled || string.IsNullOrEmpty(user.PasswordHash))
        {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > nowUtc) return null;

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification is PasswordVerificationResult.Failed)
        {
            await users.RegisterFailedLoginAsync(user.Id, nowUtc, MaximumFailedAttempts, LockoutDuration, cancellationToken);
            return null;
        }

        if (user.AccessFailedCount != 0 || user.LockoutEndUtc is not null)
        {
            await users.ResetFailedLoginsAsync(user.Id, cancellationToken);
        }

        var knownRoles = (await roles.GetAllAsync(cancellationToken))
            .Select(role => role.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var userRoles = user.Roles
            .Where(knownRoles.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (userRoles.Length == 0)
        {
            return null;
        }

        return new AuthenticatedAccount(user.Id, user.UserName, user.DisplayName, userRoles);
    }
}
