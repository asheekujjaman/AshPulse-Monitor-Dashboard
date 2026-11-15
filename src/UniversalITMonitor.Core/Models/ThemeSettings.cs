namespace UniversalITMonitor.Core.Models;

public sealed class ThemeSettings
{
    public string Name { get; set; } = ThemePresetCatalog.OceanBlue;

    public string PrimaryColor { get; set; } = "#0078D4";

    public string SidebarColor { get; set; } = "#0B2D5B";

    public string SurfaceColor { get; set; } = "#FFFFFF";

    public string PageColor { get; set; } = "#F4F8FD";

    public string HeaderColor { get; set; } = "#FFFFFF";

    public string BorderColor { get; set; } = "#D8E4F0";

    public string InkColor { get; set; } = "#172B4D";

    public string MutedColor { get; set; } = "#64748B";

    public string SoftColor { get; set; } = "#E6F2FF";
}

public static class ThemePresetCatalog
{
    public const string OceanBlue = "Ocean Blue";
    public const string MidnightPurple = "Midnight Purple";
    public const string EmeraldGreen = "Emerald Green";
    public const string Custom = "Custom";

    public static IReadOnlyList<string> Names { get; } = [OceanBlue, MidnightPurple, EmeraldGreen];

    public static string Key(string? name) => name switch
    {
        MidnightPurple => "midnight-purple",
        EmeraldGreen => "emerald-green",
        OceanBlue => "ocean-blue",
        _ => "custom"
    };

    public static bool IsPreset(string? name) => Names.Contains(name, StringComparer.Ordinal);

    public static ThemeSettings Create(string name) => name switch
    {
        OceanBlue => new ThemeSettings
        {
            Name = OceanBlue, PrimaryColor = "#0078D4", SidebarColor = "#0B2D5B", SurfaceColor = "#FFFFFF",
            PageColor = "#F4F8FD", HeaderColor = "#FFFFFF", BorderColor = "#D8E4F0", InkColor = "#172B4D",
            MutedColor = "#64748B", SoftColor = "#E6F2FF"
        },
        MidnightPurple => new ThemeSettings
        {
            Name = MidnightPurple, PrimaryColor = "#7651BD", SidebarColor = "#26183B", SurfaceColor = "#FFFEFE",
            PageColor = "#F6F1FB", HeaderColor = "#FCF9FF", BorderColor = "#E8DEF1", InkColor = "#2E2340",
            MutedColor = "#7D708D", SoftColor = "#F0E9F8"
        },
        EmeraldGreen => new ThemeSettings
        {
            Name = EmeraldGreen, PrimaryColor = "#17845C", SidebarColor = "#10372E", SurfaceColor = "#FFFFFF",
            PageColor = "#EFF7F2", HeaderColor = "#F8FCF9", BorderColor = "#DCEBE2", InkColor = "#18382D",
            MutedColor = "#71877B", SoftColor = "#E4F4EB"
        },
        _ => throw new ArgumentException("Choose Ocean Blue, Midnight Purple, or Emerald Green.", nameof(name))
    };
}
