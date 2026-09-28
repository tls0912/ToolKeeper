using System.Globalization;

namespace ToolKeeper.UI;

/// <summary>A persisted choice value and its display label.</summary>
public sealed record UiChoice(string Value, string Label);

/// <summary>Resolves language preferences without owning application settings.</summary>
public static class UiLanguage
{
    /// <summary>Checks a stored language identifier, including the system preference.</summary>
    public static bool IsSupported(string? preference) => preference is "System" or "en" or "zh-TW" or "ja";

    /// <summary>Resolves an explicit language or the current system culture to a supported language.</summary>
    public static string Resolve(string? preference, string? systemLanguage = null)
    {
        if (preference is "en" or "zh-TW" or "ja") return preference;
        systemLanguage ??= CultureInfo.CurrentUICulture.Name;
        if (systemLanguage.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
        return systemLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
    }

    /// <summary>Selects text for a Chinese or Japanese language tag, falling back to English.</summary>
    public static string Text(string language, string english, string chinese, string japanese) =>
        language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? chinese
        : language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? japanese : english;

    /// <summary>Returns language choices while retaining the raw system preference.</summary>
    public static IReadOnlyList<UiChoice> Choices(string language) =>
    [
        new("System", Text(language, "System", "跟隨系統", "システムに従う")),
        new("en", "English"),
        new("zh-TW", "繁體中文"),
        new("ja", "日本語")
    ];
}
