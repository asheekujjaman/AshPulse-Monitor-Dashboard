using System.ComponentModel.DataAnnotations;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class AppearanceSettingsViewModel
{
    [Required]
    [StringLength(32)]
    public string ThemeName { get; set; } = ThemePresetCatalog.OceanBlue;

    public bool HasCustomTheme { get; set; }
}
