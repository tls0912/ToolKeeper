namespace MarkPad.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "System";
    public string UiFontFamily { get; set; } = string.Empty;
    public string PreviewFontFamily { get; set; } = string.Empty;
    public string EditorFontFamily { get; set; } = "Cascadia Mono";
    public double UiFontSize { get; set; } = 16;
    public double PreviewFontSize { get; set; } = 16;
    public double EditorFontSize { get; set; } = 16;
    public bool AutoSave { get; set; } = true;
    public bool MatchCase { get; set; }
    // Retain the serialized key from 0.1.0; it now means manually expanded, with no hover behavior.
    public bool ToolbarPinned { get; set; } = true;
    public bool RememberWindowSize { get; set; } = true;
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; }
    public bool CodeLineNumbers { get; set; } = true;
    public bool EmojiShortcodes { get; set; } = true;
    public List<string> RecentFiles { get; set; } = [];

    public string ResolveLanguage(string systemLanguage)
    {
        if (Language is "en" or "zh-TW" or "ja") return Language;
        if (systemLanguage.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)
            || systemLanguage.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
        return systemLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
    }
}
