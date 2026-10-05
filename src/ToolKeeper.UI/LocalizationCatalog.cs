using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace ToolKeeper.UI;

/// <summary>Loads the optional languages from English-keyed embedded resources once per language.</summary>
internal static class LocalizationCatalog
{
    private static readonly string[] LanguageCodes = ["zh-CN", "es", "ar", "fr", "ko"];
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, string>>> Catalogs = new();

    internal static string? LanguageCode(string language)
    {
        foreach (var code in LanguageCodes)
            if (language.Equals(code, StringComparison.OrdinalIgnoreCase)
                || language.StartsWith(code + "-", StringComparison.OrdinalIgnoreCase)) return code;
        return null;
    }

    internal static string Text(string language, string english)
    {
        var catalog = Catalogs.GetOrAdd(language, code => new Lazy<IReadOnlyDictionary<string, string>>(() => Load(code))).Value;
        return catalog.TryGetValue(english, out var translated) && !string.IsNullOrWhiteSpace(translated) ? translated : english;
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(UiLanguage).Assembly.GetManifestResourceStream($"ToolKeeper.UI.Localization.{language}.json");
        return stream is null ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidDataException($"Localization catalog {language} must contain an object.");
    }
}
