using System.Globalization;

namespace ToolKeeper.UI;

/// <summary>A persisted choice value and its display label.</summary>
public sealed record UiChoice(string Value, string Label);

/// <summary>Resolves language preferences without owning application settings.</summary>
public static class UiLanguage
{
    /// <summary>Checks a stored language identifier, including the system preference.</summary>
    public static bool IsSupported(string? preference) => IsSupported(preference, includeAdditionalLanguages: false);

    /// <summary>Allows additional languages only for applications that explicitly opt in.</summary>
    public static bool IsSupported(string? preference, bool includeAdditionalLanguages) =>
        preference is "System" or "en" or "zh-TW" or "ja"
        || includeAdditionalLanguages && (preference is "zh-CN" or "es" or "ar" or "fr" or "ko");

    /// <summary>Resolves an explicit language or the current system culture to a supported language.</summary>
    public static string Resolve(string? preference, string? systemLanguage = null) =>
        Resolve(preference, systemLanguage, includeAdditionalLanguages: false);

    /// <summary>Resolves the system language with optional additional-language support.</summary>
    public static string Resolve(string? preference, string? systemLanguage, bool includeAdditionalLanguages)
    {
        if (preference != "System" && IsSupported(preference, includeAdditionalLanguages)) return preference!;
        systemLanguage ??= CultureInfo.CurrentUICulture.Name;
        if (systemLanguage.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
        if (systemLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (includeAdditionalLanguages)
        {
            if (systemLanguage.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                || systemLanguage.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)
                || systemLanguage.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            return LocalizationCatalog.LanguageCode(systemLanguage) ?? "en";
        }
        return "en";
    }

    /// <summary>Selects inline translations or an additional-language catalog entry, falling back to English.</summary>
    public static string Text(string language, string english, string chinese, string japanese)
    {
        var catalogLanguage = LocalizationCatalog.LanguageCode(language);
        if (catalogLanguage is not null) return LocalizationCatalog.Text(catalogLanguage, english);
        return language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? chinese
            : language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? japanese : english;
    }

    /// <summary>Formats a translated composite string while preserving runtime values.</summary>
    public static string Format(string language, string english, string chinese, string japanese, params object[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(Resolve(language, language, includeAdditionalLanguages: true)),
            Text(language, english, chinese, japanese), arguments);

    /// <summary>Reports the direction of the supported Modern Standard Arabic language.</summary>
    public static bool IsRightToLeft(string language) =>
        language.Equals("ar", StringComparison.OrdinalIgnoreCase)
        || language.StartsWith("ar-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns language choices while retaining the raw system preference.</summary>
    public static IReadOnlyList<UiChoice> Choices(string language) => Choices(language, includeAdditionalLanguages: false);

    /// <summary>Returns additional choices only when the application opts in.</summary>
    public static IReadOnlyList<UiChoice> Choices(string language, bool includeAdditionalLanguages)
    {
        List<UiChoice> choices =
        [
            new("System", Text(language, "System", "跟隨系統", "システムに従う")),
            new("en", "English"),
            new("zh-TW", "繁體中文"),
            new("ja", "日本語")
        ];
        if (includeAdditionalLanguages)
            choices.AddRange([
                new("zh-CN", "简体中文"),
                new("es", "Español"),
                new("ar", "العربية"),
                new("fr", "Français"),
                new("ko", "한국어")
            ]);
        return choices;
    }
}
