using ToolKeeper.UI;

namespace MarkPad.Models;

public sealed class AppSettings
{
    public const int EmptyArticleCount = 3;
    public string Theme { get; set; } = "Ink";
    public string Language { get; set; } = "System";
    public string UiFontFamily { get; set; } = string.Empty;
    public string PreviewFontFamily { get; set; } = string.Empty;
    public string EditorFontFamily { get; set; } = "Cascadia Mono";
    public double UiFontSize { get; set; } = 15;
    public double PreviewFontSize { get; set; } = 16;
    public double EditorFontSize { get; set; } = 16;
    public bool InterfaceTextShadowEnabled { get; set; } = true;
    public int InterfaceTextShadowThickness { get; set; } = 1;
    public bool AutoSave { get; set; } = true;
    public bool RememberOpenFiles { get; set; }
    public List<string> SessionFiles { get; set; } = [];
    public string? SessionActiveFile { get; set; }
    public bool MatchCase { get; set; }
    // Retain the serialized key from 0.1.0; it now means manually expanded, with no hover behavior.
    public bool ToolbarPinned { get; set; } = true;
    public bool RememberWindowSize { get; set; } = true;
    public double WindowWidth { get; set; } = 900;
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool CodeLineNumbers { get; set; } = true;
    public bool EmojiShortcodes { get; set; } = true;
    // Keep the existing serialized key; these levels apply to preview and PDF outlines.
    public int[] PdfOutlineLevels { get; set; } = [1, 2, 3, 4, 5, 6];
    public List<string> RecentFiles { get; set; } = [];
    // Keep the old flag readable by earlier versions; the three-article index is authoritative.
    public bool NextEmptyArticleIsIntroduction { get; set; }
    public int? NextEmptyArticleIndex { get; set; }

    public string ResolveLanguage(string systemLanguage) => UiLanguage.Resolve(Language, systemLanguage, includeAdditionalLanguages: true);
}
