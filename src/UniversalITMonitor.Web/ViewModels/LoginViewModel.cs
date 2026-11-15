using System.ComponentModel.DataAnnotations;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class LoginViewModel
{
    [Required]
    [StringLength(64)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}
