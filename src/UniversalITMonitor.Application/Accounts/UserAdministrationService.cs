using Microsoft.AspNetCore.Identity;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Accounts;

public sealed class UserAdministrationService(
    IUserRepository users,
    IPasswordHasher<LocalUser> passwordHasher) : IUserAdministrationService
{
    private static readonly HashSet<string> AllowedRoles =
    [
        SystemRoles.Administrator,
        SystemRoles.Operator,
        SystemRoles.ReadOnly
    ];

    public async Task<IReadOnlyList<ManagedLocalUser>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await users.GetAllAsync(cancellationToken))
            .OrderBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(ToManagedUser)
            .ToArray();

    public async Task<UserAdministrationResult> CreateAsync(UserAdministrationInput input, CancellationToken cancellationToken = default)
    {
        var validation = Validate(input, isCreate: true);
        if (validation is not null) return new(UserWriteResult.ValidationFailed, validation);

        var user = new LocalUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = input.UserName.Trim(),
            DisplayName = input.DisplayName.Trim(),
            Roles = [input.Role],
            IsEnabled = input.IsEnabled,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, input.Password!);
        return ToResult(await users.CreateAsync(user, cancellationToken));
    }

    public async Task<UserAdministrationResult> UpdateAsync(string id, UserAdministrationInput input, CancellationToken cancellationToken = default)
    {
        var validation = Validate(input, isCreate: false);
        if (validation is not null) return new(UserWriteResult.ValidationFailed, validation);
        var current = await users.FindByIdAsync(id, cancellationToken);
        if (current is null) return new(UserWriteResult.NotFound);

        var updated = new LocalUser
        {
            Id = current.Id,
            UserName = input.UserName.Trim(),
            DisplayName = input.DisplayName.Trim(),
            Roles = [input.Role],
            IsEnabled = input.IsEnabled,
            CreatedUtc = current.CreatedUtc,
            PasswordHash = current.PasswordHash
        };
        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            updated.PasswordHash = passwordHasher.HashPassword(updated, input.Password);
        }

        return ToResult(await users.UpdateAsync(updated, cancellationToken));
    }

    public Task<UserAdministrationResult> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        DeleteCoreAsync(id, cancellationToken);

    private async Task<UserAdministrationResult> DeleteCoreAsync(string id, CancellationToken cancellationToken) =>
        ToResult(await users.DeleteAsync(id, cancellationToken));

    private static string? Validate(UserAdministrationInput input, bool isCreate)
    {
        if (input is null) return "Enter the required user details.";
        var userName = input.UserName?.Trim();
        var displayName = input.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(userName) || userName.Length > 80)
            return "Enter a username up to 80 characters long.";
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120)
            return "Enter a display name up to 120 characters long.";
        if (string.IsNullOrWhiteSpace(input.Role) || !AllowedRoles.Contains(input.Role))
            return "Choose Administrator, Read-only, or the existing Operator role.";
        if ((isCreate || !string.IsNullOrEmpty(input.Password)) &&
            (input.Password?.Length is < 12 or > 128))
            return "Passwords must contain between 12 and 128 characters.";
        return null;
    }

    private static ManagedLocalUser ToManagedUser(LocalUser user) => new(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Roles.ToArray(),
        user.IsEnabled,
        user.CreatedUtc);

    private static UserAdministrationResult ToResult(UserWriteResult result) => new(result, result switch
    {
        UserWriteResult.DuplicateUserName => "That username is already in use.",
        UserWriteResult.LastActiveAdministrator => "The last active administrator cannot be disabled, deleted, or changed to another role.",
        UserWriteResult.ValidationFailed => "Check the supplied user details.",
        UserWriteResult.NotFound => "The user no longer exists.",
        _ => null
    });
}

public interface IUserAdministrationService
{
    Task<IReadOnlyList<ManagedLocalUser>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> CreateAsync(UserAdministrationInput input, CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> UpdateAsync(string id, UserAdministrationInput input, CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class UserAdministrationInput
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = SystemRoles.ReadOnly;
    public bool IsEnabled { get; set; } = true;
    public string? Password { get; set; }
}

public sealed record ManagedLocalUser(
    string Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<string> Roles,
    bool IsEnabled,
    DateTimeOffset CreatedUtc);

public sealed record UserAdministrationResult(UserWriteResult Result, string? Error = null)
{
    public bool Succeeded => Result == UserWriteResult.Success;
}
