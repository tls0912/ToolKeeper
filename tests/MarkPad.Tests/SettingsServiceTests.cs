using System.IO;
using MarkPad.Models;
using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void FirstRunUsesCapturedDefaultsWithoutRecentFiles()
    {
        using var directory = new TestDirectory();
        var service = new SettingsService(directory.PathName);

        AssertCapturedDefaults(service.Settings);
        service.Save();
        AssertCapturedDefaults(new SettingsService(directory.PathName).Settings);
    }

    [Theory]
    [InlineData("System", "ja-JP", "ja")]
    [InlineData("System", "zh-TW", "zh-TW")]
    [InlineData("System", "zh-HK", "zh-TW")]
    [InlineData("System", "zh-MO", "zh-TW")]
    [InlineData("System", "zh-Hant-TW", "zh-TW")]
    [InlineData("System", "en-US", "en")]
    [InlineData("System", "de-DE", "en")]
    [InlineData("en", "ja-JP", "en")]
    [InlineData("zh-TW", "en-US", "zh-TW")]
    [InlineData("ja", "de-DE", "ja")]
    [InlineData("", "ja-JP", "ja")]
    public void LanguageResolvesForDisplayWithoutSavingTheResolvedSystemLanguage(
        string language, string systemLanguage, string expectedLanguage)
    {
        using var directory = new TestDirectory();
        var service = new SettingsService(directory.PathName);
        service.Settings.Language = language;

        Assert.Equal(expectedLanguage, service.Settings.ResolveLanguage(systemLanguage));
        Assert.Equal(language, service.Settings.Language);
        service.Save();

        var restored = new SettingsService(directory.PathName).Settings;
        Assert.Equal(language.Length == 0 ? "System" : language, restored.Language);
        Assert.Equal(expectedLanguage, restored.ResolveLanguage(systemLanguage));
    }

    [Fact]
    public void ExistingPreferencesKeepExplicitValuesInsteadOfReceivingNewDefaults()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.FilePath("settings.json"), """
            { "Theme": "Light", "Language": "en", "UiFontFamily": "", "UiFontSize": 13,
              "PreviewFontFamily": "", "PreviewFontSize": 20, "EditorFontFamily": "Consolas", "EditorFontSize": 18,
              "AutoSave": false, "MatchCase": true, "ToolbarPinned": false, "RememberWindowSize": false,
              "WindowWidth": 1200, "WindowHeight": 720, "CodeLineNumbers": false, "EmojiShortcodes": false,
              "RecentFiles": ["existing.md"] }
            """);

        var service = new SettingsService(directory.PathName);
        service.Save();
        var restored = new SettingsService(directory.PathName).Settings;
        Assert.Equal("Light", restored.Theme);
        Assert.Equal("en", restored.Language);
        Assert.Equal(string.Empty, restored.UiFontFamily);
        Assert.Equal(13, restored.UiFontSize);
        Assert.Equal(string.Empty, restored.PreviewFontFamily);
        Assert.Equal(20, restored.PreviewFontSize);
        Assert.Equal("Consolas", restored.EditorFontFamily);
        Assert.Equal(18, restored.EditorFontSize);
        Assert.False(restored.AutoSave);
        Assert.True(restored.MatchCase);
        Assert.False(restored.ToolbarPinned);
        Assert.False(restored.RememberWindowSize);
        Assert.Equal(1200, restored.WindowWidth);
        Assert.Equal(720, restored.WindowHeight);
        Assert.False(restored.CodeLineNumbers);
        Assert.False(restored.EmojiShortcodes);
        Assert.Equal("existing.md", Assert.Single(restored.RecentFiles));
    }

    [Fact]
    public void PreferencesPersistAndRecentFilesAreCaseInsensitiveAndLimitedToTwenty()
    {
        using var directory = new TestDirectory();
        var settings = new SettingsService(directory.PathName);
        settings.Settings.Theme = "Dark";
        settings.Settings.Language = "zh-TW";
        settings.Settings.AutoSave = true;
        settings.Settings.UiFontFamily = "Segoe UI";
        settings.Settings.UiFontSize = 15;
        settings.Settings.PreviewFontFamily = "Georgia";
        settings.Settings.PreviewFontSize = 22;
        settings.Settings.EditorFontFamily = "Consolas";
        settings.Settings.EditorFontSize = 18;
        for (var index = 0; index < 25; index++) settings.AddRecent(directory.FilePath($"file-{index}.md"));
        settings.AddRecent(directory.FilePath("FILE-24.md"));

        var restored = new SettingsService(directory.PathName).Settings;
        Assert.Equal("Dark", restored.Theme);
        Assert.Equal("zh-TW", restored.Language);
        Assert.True(restored.AutoSave);
        Assert.Equal("Segoe UI", restored.UiFontFamily);
        Assert.Equal(15, restored.UiFontSize);
        Assert.Equal("Georgia", restored.PreviewFontFamily);
        Assert.Equal(22, restored.PreviewFontSize);
        Assert.Equal("Consolas", restored.EditorFontFamily);
        Assert.Equal(18, restored.EditorFontSize);
        Assert.Equal(20, restored.RecentFiles.Count);
        Assert.Equal(directory.FilePath("FILE-24.md"), restored.RecentFiles[0]);
        Assert.DoesNotContain(directory.FilePath("file-0.md"), restored.RecentFiles);
    }

    [Fact]
    public void InvalidSettingsFallBackToUsableDefaults()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.FilePath("settings.json"), """
            { "Theme": "unknown", "Language": "unknown", "UiFontFamily": null, "UiFontSize": 500, "PreviewFontSize": -20,
              "EditorFontSize": 500, "WindowWidth": 2, "WindowHeight": -1,
              "EditorFontFamily": null, "PreviewFontFamily": null, "RecentFiles": null }
            """);
        var settings = new SettingsService(directory.PathName).Settings;
        Assert.Equal("System", settings.Theme);
        Assert.Equal("System", settings.Language);
        Assert.Equal(string.Empty, settings.UiFontFamily);
        Assert.Equal(16, settings.UiFontSize);
        Assert.Equal(16, settings.PreviewFontSize);
        Assert.Equal(16, settings.EditorFontSize);
        Assert.Equal(900, settings.WindowWidth);
        Assert.Equal(0, settings.WindowHeight);
        Assert.Equal("Cascadia Mono", settings.EditorFontFamily);
        Assert.Equal(string.Empty, settings.PreviewFontFamily);
        Assert.Empty(settings.RecentFiles);
    }

    [Theory]
    [InlineData(9, 16)]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    [InlineData(21, 16)]
    [InlineData(double.NaN, 16)]
    [InlineData(double.PositiveInfinity, 16)]
    public void UiFontSizeUsesItsOwnRangeWithoutChangingContentFonts(double requestedSize, double expectedSize)
    {
        using var directory = new TestDirectory();
        var service = new SettingsService(directory.PathName);
        service.Settings.UiFontSize = requestedSize;
        service.Settings.PreviewFontSize = 72;
        service.Settings.EditorFontSize = 8;
        service.Save();

        var restored = new SettingsService(directory.PathName).Settings;
        Assert.Equal(expectedSize, restored.UiFontSize);
        Assert.Equal(72, restored.PreviewFontSize);
        Assert.Equal(8, restored.EditorFontSize);
    }

    [Fact]
    public void LegacyPreferencesKeepContentFontsAndReceiveUiFontDefaults()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.FilePath("settings.json"), """
            { "Language": "ja", "PreviewFontFamily": "Yu Gothic UI", "PreviewFontSize": 24,
              "EditorFontFamily": "Consolas", "EditorFontSize": 18 }
            """);

        var restored = new SettingsService(directory.PathName).Settings;
        Assert.Equal(string.Empty, restored.UiFontFamily);
        Assert.Equal(16, restored.UiFontSize);
        Assert.Equal("Yu Gothic UI", restored.PreviewFontFamily);
        Assert.Equal(24, restored.PreviewFontSize);
        Assert.Equal("Consolas", restored.EditorFontFamily);
        Assert.Equal(18, restored.EditorFontSize);
    }

    [Fact]
    public void ResetRestoresDefaultsAndDoesNotRemoveOtherAppData()
    {
        using var directory = new TestDirectory();
        var snapshot = directory.FilePath("untouched.recovery.json");
        File.WriteAllText(snapshot, "preserve");
        var settings = new SettingsService(directory.PathName);
        settings.Settings.Theme = "Dark";
        settings.Settings.Language = "en";
        settings.Settings.UiFontFamily = "Segoe UI";
        settings.Settings.UiFontSize = 13;
        settings.Settings.PreviewFontFamily = "Georgia";
        settings.Settings.PreviewFontSize = 22;
        settings.Settings.EditorFontFamily = "Consolas";
        settings.Settings.EditorFontSize = 18;
        settings.Settings.AutoSave = false;
        settings.Settings.MatchCase = true;
        settings.Settings.ToolbarPinned = false;
        settings.Settings.RememberWindowSize = false;
        settings.Settings.WindowWidth = 1200;
        settings.Settings.WindowHeight = 720;
        settings.Settings.CodeLineNumbers = false;
        settings.Settings.EmojiShortcodes = false;
        settings.AddRecent(directory.FilePath("recent.md"));
        settings.Reset();

        var restored = new SettingsService(directory.PathName).Settings;
        AssertCapturedDefaults(settings.Settings);
        AssertCapturedDefaults(restored);
        Assert.Equal("preserve", File.ReadAllText(snapshot));
        Assert.Empty(Directory.EnumerateFiles(directory.PathName, "*.tmp"));
    }

    private static void AssertCapturedDefaults(AppSettings settings)
    {
        Assert.Equal("System", settings.Theme);
        Assert.Equal("System", settings.Language);
        Assert.Equal(string.Empty, settings.UiFontFamily);
        Assert.Equal(string.Empty, settings.PreviewFontFamily);
        Assert.Equal("Cascadia Mono", settings.EditorFontFamily);
        Assert.Equal(16, settings.UiFontSize);
        Assert.Equal(16, settings.PreviewFontSize);
        Assert.Equal(16, settings.EditorFontSize);
        Assert.True(settings.AutoSave);
        Assert.False(settings.MatchCase);
        Assert.True(settings.ToolbarPinned);
        Assert.True(settings.RememberWindowSize);
        Assert.Equal(900, settings.WindowWidth);
        Assert.Equal(0, settings.WindowHeight);
        Assert.True(settings.CodeLineNumbers);
        Assert.True(settings.EmojiShortcodes);
        Assert.Empty(settings.RecentFiles);
    }
}
