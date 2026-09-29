using ToolKeeper.UI;

namespace MarkPad.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Ink";
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
    public bool WindowMaximized { get; set; }
    public bool CodeLineNumbers { get; set; } = true;
    public bool EmojiShortcodes { get; set; } = true;
    public int[] PdfOutlineLevels { get; set; } = [1, 2, 3, 4, 5, 6];
    public List<string> RecentFiles { get; set; } = [];

    public string ResolveLanguage(string systemLanguage) => UiLanguage.Resolve(Language, systemLanguage);
}
