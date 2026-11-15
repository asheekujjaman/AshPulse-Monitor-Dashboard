using System.ComponentModel.DataAnnotations;
using UniversalITMonitor.Application.Accounts;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class UserAdministrationIndexViewModel
{
    public IReadOnlyList<ManagedLocalUser> Users { get; init; } = [];
}

public sealed class UserAdministrationEditViewModel
{
    public string? Id { get; set; }

    public bool IsCreate { get; set; }

    [Required, StringLength(80, MinimumLength = 1)]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 1)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = SystemRoles.ReadOnly;

    [Display(Name = "Account enabled")]
    public bool IsEnabled { get; set; } = true;

    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }

    public IReadOnlyList<UserRoleChoice> Roles { get; set; } = [];
}

public sealed record UserRoleChoice(string Name, string Label);
