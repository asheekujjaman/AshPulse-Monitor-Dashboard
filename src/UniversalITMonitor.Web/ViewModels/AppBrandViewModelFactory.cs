using System.Text.RegularExpressions;
using UniversalITMonitor.Application.Presentation;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public static partial class AppBrandViewModelFactory
{
    public static AppBrandViewModel Create(AppPresentation settings)
    {
        var branding = settings.Branding;
        var theme = IsLegacyOceanDefault(settings.Theme)
            ? ThemePresetCatalog.Create(ThemePresetCatalog.OceanBlue)
            : settings.Theme;
        var preset = ThemePresetCatalog.IsPreset(theme.Name) ? ThemePresetCatalog.Create(theme.Name) : null;
        return new AppBrandViewModel(
            CleanBrandName(branding.ProductName),
            CleanOrganizationName(branding.OrganizationName),
            ThemePresetCatalog.Key(theme.Name),
            SafeColor(theme.PrimaryColor, preset?.PrimaryColor ?? "#0078D4"),
            SafeColor(theme.SidebarColor, preset?.SidebarColor ?? "#0B2D5B"),
            SafeColor(theme.SurfaceColor, preset?.SurfaceColor ?? "#FFFFFF"),
            SafeColor(theme.PageColor, preset?.PageColor ?? "#F4F8FD"),
            SafeColor(theme.HeaderColor, preset?.HeaderColor ?? "#FFFFFF"),
            SafeColor(theme.BorderColor, preset?.BorderColor ?? "#D8E4F0"),
            SafeColor(theme.InkColor, preset?.InkColor ?? "#172B4D"),
            SafeColor(theme.MutedColor, preset?.MutedColor ?? "#64748B"),
            SafeColor(theme.SoftColor, preset?.SoftColor ?? "#E6F2FF"));
    }

    private static bool IsLegacyOceanDefault(ThemeSettings theme) =>
        string.Equals(theme.Name, ThemePresetCatalog.OceanBlue, StringComparison.OrdinalIgnoreCase) &&
        MatchesColor(theme.PrimaryColor, "#2563EB") &&
        MatchesColor(theme.SidebarColor, "#101B31") &&
        MatchesColor(theme.SurfaceColor, "#FFFFFF") &&
        MatchesColor(theme.PageColor, "#F4F7FB") &&
        MatchesColor(theme.HeaderColor, "#FFFFFF") &&
        MatchesColor(theme.BorderColor, "#E7EBF2") &&
        MatchesColor(theme.InkColor, "#18243A") &&
        MatchesColor(theme.MutedColor, "#77849A") &&
        MatchesColor(theme.SoftColor, "#EDF3FC");

    private static bool MatchesColor(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static string CleanBrandName(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "Universal IT Monitor", StringComparison.OrdinalIgnoreCase)
            ? "AshPulse"
            : value.Trim();

    private static string CleanOrganizationName(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "Network Operations", StringComparison.OrdinalIgnoreCase)
            ? "Infrastructure Monitor"
            : value.Trim();

    private static string SafeColor(string? value, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && HexColorPattern().IsMatch(value) ? value : fallback;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorPattern();
}
