using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ToolKeeper.UI;

/// <summary>The native interface colors for one resolved theme.</summary>
public sealed record UiPalette(Color WindowBackground, Color Surface, Color Text, Color Muted, Color Line, Color Hover, Color Accent);

/// <summary>Resolves themes and applies colors without owning windows, settings, or system events.</summary>
public static class UiTheme
{
    /// <summary>Checks a stored theme identifier, including the system preference.</summary>
    public static bool IsSupported(string? theme) => theme is "System" or "Light" or "Dark" or "Ink" or "InkDark";

    /// <summary>Indicates whether a theme uses the traditional paper palette.</summary>
    public static bool IsInk(string? theme) => theme is "Ink" or "InkDark";

    /// <summary>Resolves darkness; unsupported identifiers retain the light fallback.</summary>
    public static bool IsDark(string? theme, bool? systemDark = null) =>
        theme is "Dark" or "InkDark" || theme == "System" && (systemDark ?? ReadSystemDark());

    /// <summary>Reads the Windows application color preference, falling back to light.</summary>
    public static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch { return false; }
    }

    /// <summary>Cycles light, dark, bamboo light, and bamboo dark from the current appearance.</summary>
    public static string Next(string? theme, bool? systemDark = null) => theme switch
    {
        "Ink" => "InkDark",
        "InkDark" => "Light",
        _ => IsDark(theme, systemDark) ? "Ink" : "Dark"
    };

    /// <summary>Returns translated theme choices without resolving the system preference.</summary>
    public static IReadOnlyList<UiChoice> Choices(string language) =>
    [
        new("Light", UiLanguage.Text(language, "Light", "亮色", "ライト")),
        new("Dark", UiLanguage.Text(language, "Dark", "深色", "ダーク")),
        new("Ink", UiLanguage.Text(language, "Bamboo Light", "竹子（亮色）", "竹（ライト）")),
        new("InkDark", UiLanguage.Text(language, "Bamboo Dark", "竹子（深色）", "竹（ダーク）")),
        new("System", UiLanguage.Text(language, "System", "跟隨系統", "システム"))
    ];

    /// <summary>Returns the native colors used by the selected light or dark theme.</summary>
    public static UiPalette Palette(bool dark, bool ink) => ink
        ? dark
            ? new(C(0x181D1A), C(0x1F2421), C(0xE5E1D5), C(0xAAAFA3), C(0x414A42), C(0x303931), C(0xA2BEA9))
            : new(C(0xE3E1D8), C(0xF3F0E7), C(0x2C302D), C(0x73786F), C(0xCCC9BE), C(0xD6D8CD), C(0x456B61))
        : dark
            ? new(C(0x161B22), C(0x0D1117), C(0xE6EDF3), C(0x8B949E), C(0x30363D), C(0x252C36), C(0x58A6FF))
            : new(C(0xD9DEE4), C(0xE8EBEF), C(0x1F2328), C(0x656D76), C(0xBEC6CF), C(0xCDD4DD), C(0x0969DA));

    /// <summary>Replaces only the seven shared brush resources in the supplied scope.</summary>
    public static void ApplyResources(ResourceDictionary resources, string theme, bool? systemDark = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var palette = Palette(IsDark(theme, systemDark), IsInk(theme));
        resources["WindowBackground"] = new SolidColorBrush(palette.WindowBackground);
        resources["SurfaceBrush"] = new SolidColorBrush(palette.Surface);
        resources["TextBrush"] = new SolidColorBrush(palette.Text);
        resources["MutedBrush"] = new SolidColorBrush(palette.Muted);
        resources["LineBrush"] = new SolidColorBrush(palette.Line);
        resources["HoverBrush"] = new SolidColorBrush(palette.Hover);
        resources["AccentBrush"] = new SolidColorBrush(palette.Accent);
    }

    private static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
