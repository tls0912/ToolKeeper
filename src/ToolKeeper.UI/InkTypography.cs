using System.Windows;
using System.Windows.Media;

namespace ToolKeeper.UI;

/// <summary>Chooses a traditional font already installed on this computer.</summary>
public static class InkTypography
{
    /// <summary>The legacy persisted choice for an automatically resolved traditional font.</summary>
    public const string FontChoice = "@ink";

    private static readonly Lazy<Dictionary<string, string>> InstalledFonts = new(() =>
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            names.TryAdd(family.Source, family.Source);
            foreach (var localizedName in family.FamilyNames.Values)
                names.TryAdd(localizedName, family.Source);
        }
        return names;
    });

    /// <summary>Returns an installed font family appropriate for the resolved language.</summary>
    public static string Resolve(string language)
    {
        // CJK language packs are optional on Windows. Resolve aliases against installed
        // families so both WPF and WebView2 receive an actual, available family name.
        string[] candidates = language switch
        {
            "zh-TW" => ["DFKai-SB", "標楷體", "BiauKai", "KaiTi", "楷体", "Yu Mincho", "PMingLiU", "MingLiU", "Microsoft JhengHei", "Georgia"],
            "ja" => ["Yu Mincho", "游明朝", "YuMincho", "MS PMincho", "MS Mincho", "DFKai-SB", "Georgia"],
            _ => ["Georgia", "Palatino Linotype", "Book Antiqua", "Times New Roman"]
        };
        foreach (var candidate in candidates)
            if (InstalledFonts.Value.TryGetValue(candidate, out var installed)) return installed;
        return SystemFonts.MessageFontFamily.Source;
    }
}
