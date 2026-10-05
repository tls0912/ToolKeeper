using System.Windows;
using System.Windows.Media;

namespace ToolKeeper.UI;

/// <summary>Resolves automatic interface/reading fonts and the shared control size resources.</summary>
public static class UiTypography
{
    public static string ResolveInterfaceFont(string? choice, string language, bool ink) =>
        ResolveFont(choice, language, ink, forInterface: true);

    public static string ResolveReadingFont(string? choice, string language, bool ink) =>
        ResolveFont(choice, language, ink, forInterface: false);

    private static string ResolveFont(string? choice, string language, bool ink, bool forInterface)
    {
        if (choice == InkTypography.FontChoice || string.IsNullOrWhiteSpace(choice) && ink)
            return InkTypography.Resolve(language);
        if (!string.IsNullOrWhiteSpace(choice)) return choice;
        return language switch
        {
            "zh-TW" => forInterface ? "Microsoft JhengHei UI" : "Microsoft JhengHei",
            "zh-CN" => InkTypography.ResolveInstalled(forInterface ? "Microsoft YaHei UI" : "Microsoft YaHei", "Microsoft YaHei", "SimSun", "Segoe UI"),
            "ja" => forInterface ? "Yu Gothic UI" : "Yu Gothic",
            "ko" => InkTypography.ResolveInstalled("Malgun Gothic", "맑은 고딕", "Segoe UI"),
            "ar" => InkTypography.ResolveInstalled("Segoe UI", "Tahoma", "Traditional Arabic"),
            _ => "Segoe UI"
        };
    }

    /// <summary>Writes only to the given owner; no application-wide settings or font downloads.</summary>
    public static void ApplyResources(ResourceDictionary resources, FontFamily family, double fontSize)
    {
        var size = double.IsFinite(fontSize) ? Math.Clamp(fontSize, 10, 20) : 16;
        resources["UiFontFamily"] = family;
        resources["UiFontSize"] = size;
        resources["UiSmallFontSize"] = size * 9 / 13;
        resources["UiHeadingFontSize"] = size * 17 / 13;
        resources["UiSectionFontSize"] = size * 16 / 13;
        resources["UiMenuMaxWidth"] = Math.Max(320, size * 360 / 13);
    }
}
