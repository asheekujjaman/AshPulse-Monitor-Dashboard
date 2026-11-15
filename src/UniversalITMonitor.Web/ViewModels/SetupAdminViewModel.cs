using System.ComponentModel.DataAnnotations;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class SetupAdminViewModel
{
    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = "Administrator";

    [Required]
    [StringLength(64, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9._@-]{2,63}$", ErrorMessage = "Use 3–64 letters, digits, or . _ @ - characters, starting with a letter or digit.")]
    public string UserName { get; set; } = "admin";

    [Required]
    [StringLength(256, MinimumLength = 12, ErrorMessage = "Use at least 12 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
