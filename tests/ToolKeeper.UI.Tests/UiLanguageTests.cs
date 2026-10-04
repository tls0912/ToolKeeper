using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace ToolKeeper.UI.Tests;

public sealed class UiLanguageTests
{
    [Theory]
    [InlineData("en", "ja-JP", "en")]
    [InlineData("zh-TW", "en-US", "zh-TW")]
    [InlineData("ja", "zh-TW", "ja")]
    [InlineData("System", "zh-TW", "zh-TW")]
    [InlineData("System", "zh-HK", "zh-TW")]
    [InlineData("System", "zh-MO", "zh-TW")]
    [InlineData("System", "zh-Hant", "zh-TW")]
    [InlineData("System", "ja-JP", "ja")]
    [InlineData("System", "zh-CN", "en")]
    [InlineData("System", "fr-FR", "en")]
    [InlineData("unknown", "ja-JP", "ja")]
    public void ResolutionHonorsExplicitChoicesAndSupportedSystemLanguages(string preference, string systemLanguage, string expected)
        => Assert.Equal(expected, UiLanguage.Resolve(preference, systemLanguage));

    [Fact]
    public void ChoicesUseStablePreferenceIdsWhileLabelsFollowTheOwnerLanguage()
    {
        var english = UiLanguage.Choices("en"); var chinese = UiLanguage.Choices("zh-TW");
        Assert.Equal(new[] { "System", "en", "zh-TW", "ja" }, english.Select(choice => choice.Value));
        Assert.Equal(english.Select(choice => choice.Value), chinese.Select(choice => choice.Value));
        Assert.All(english, choice => Assert.True(UiLanguage.IsSupported(choice.Value)));
        Assert.False(UiLanguage.IsSupported("fr-FR")); Assert.False(UiLanguage.IsSupported(null));
        Assert.NotEqual(english[0].Label, chinese[0].Label);
        Assert.All(chinese, choice => Assert.False(string.IsNullOrWhiteSpace(choice.Label)));
    }

    [Fact]
    public void IndependentCallersCanTranslateWithoutChangingGlobalLanguage()
    {
        Assert.Equal("甲", UiLanguage.Text("zh-TW", "A", "甲", "あ"));
        Assert.Equal("あ", UiLanguage.Text("ja", "A", "甲", "あ"));
        Assert.Equal("A", UiLanguage.Text("zh-CN", "A", "甲", "あ"));
        Assert.Equal("あ", UiLanguage.Text("JA-jp", "A", "甲", "あ"));
        Assert.Equal("A", UiLanguage.Text("unsupported", "A", "甲", "あ"));
        Assert.Equal("甲", UiLanguage.Text("zh-TW", "A", "甲", "あ"));
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN", "zh-CN")]
    [InlineData("zh-CN", "en-US", "zh-CN")]
    [InlineData("es", "ja-JP", "es")]
    [InlineData("ar", "zh-TW", "ar")]
    [InlineData("fr", "ja-JP", "fr")]
    [InlineData("ko", "en-US", "ko")]
    [InlineData("System", "zh-SG", "zh-CN")]
    [InlineData("System", "zh-Hans", "zh-CN")]
    [InlineData("System", "zh-Hans-CN", "zh-CN")]
    [InlineData("System", "zh-Hant-HK", "zh-TW")]
    [InlineData("System", "es-MX", "es")]
    [InlineData("System", "ar-SA", "ar")]
    [InlineData("System", "fr-CA", "fr")]
    [InlineData("System", "ko-KR", "ko")]
    [InlineData("System", "FR-fr", "fr")]
    [InlineData("System", "et-EE", "en")]
    [InlineData("System", "french", "en")]
    [InlineData("unknown", "es-ES", "es")]
    public void OptedInApplicationsResolveAdditionalLanguages(string preference, string systemLanguage, string expected)
        => Assert.Equal(expected, UiLanguage.Resolve(preference, systemLanguage, includeAdditionalLanguages: true));

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("ko")]
    public void AdditionalPreferenceIdsRequireExplicitOptIn(string language)
    {
        Assert.False(UiLanguage.IsSupported(language));
        Assert.False(UiLanguage.IsSupported(language, includeAdditionalLanguages: false));
        Assert.True(UiLanguage.IsSupported(language, includeAdditionalLanguages: true));
        Assert.Equal("en", UiLanguage.Resolve(language, "en-US"));
        Assert.Equal("en", UiLanguage.Resolve("System", language, includeAdditionalLanguages: false));
    }

    [Fact]
    public void ExtendedChoicesHaveStableIdsAndNativeLabelsWithoutAffectingDefaultChoices()
    {
        var extended = UiLanguage.Choices("fr", includeAdditionalLanguages: true);
        Assert.Equal(new[] { "System", "en", "zh-TW", "ja", "zh-CN", "es", "ar", "fr", "ko" }, extended.Select(choice => choice.Value));
        Assert.All(extended, choice => Assert.True(UiLanguage.IsSupported(choice.Value, includeAdditionalLanguages: true)));
        Assert.Equal("简体中文", extended.Single(choice => choice.Value == "zh-CN").Label);
        Assert.Equal("العربية", extended.Single(choice => choice.Value == "ar").Label);
        Assert.Equal(new[] { "System", "en", "zh-TW", "ja" }, UiLanguage.Choices("fr").Select(choice => choice.Value));
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("ko")]
    public void AdditionalLanguagesLoadEmbeddedCatalogsAndFallBackToEnglishForUnknownKeys(string language)
    {
        using var resource = typeof(UiLanguage).Assembly.GetManifestResourceStream($"ToolKeeper.UI.Localization.{language}.json");
        Assert.NotNull(resource);
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(resource);
        Assert.NotNull(entries);
        Assert.Contains("Close", entries.Keys);
        Assert.Contains("Author: {0}", entries.Keys);
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));
        var close = UiLanguage.Text(language, "Close", "關閉", "閉じる");
        Assert.Equal(entries["Close"], close);
        Assert.NotEqual("Close", close);
        Assert.Equal("__unknown_catalog_key__", UiLanguage.Text(language, "__unknown_catalog_key__", "甲", "あ"));
    }

    [Theory]
    [InlineData("en", "Author: Pat {notes}")]
    [InlineData("zh-TW", "作者：Pat {notes}")]
    [InlineData("ja", "作者：Pat {notes}")]
    public void CompositeFormattingPreservesExistingTranslationsAndLiteralArgumentBraces(string language, string expected)
        => Assert.Equal(expected, UiLanguage.Format(language, "Author: {0}", "作者：{0}", "作者：{0}", "Pat {notes}"));

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("ko")]
    public void LocalizedAuthorFormattingKeepsTheRuntimeAuthorAndConsumesItsPlaceholder(string language)
    {
        var formatted = UiLanguage.Format(language, "Author: {0}", "作者：{0}", "作者：{0}", "Pat {notes}");
        Assert.Contains("Pat {notes}", formatted);
        Assert.DoesNotContain("{0}", formatted);
        Assert.NotEqual("Author: Pat {notes}", formatted);
    }

    [Fact]
    public void CompositeFormattingUsesTheChosenLanguageForNumbersAndEscapedBraces()
    {
        Assert.Equal("Value {kept}: 1,234.5", UiLanguage.Format("en", "Value {{kept}}: {0:N1}", "unused", "unused", 1234.5));
        Assert.Equal("Value {kept}: 1.234,5", UiLanguage.Format("es", "Value {{kept}}: {0:N1}", "unused", "unused", 1234.5));
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("ar-SA", true)]
    [InlineData("AR-eg", true)]
    [InlineData("arbitrary", false)]
    [InlineData("zh-CN", false)]
    [InlineData("fr", false)]
    public void OnlyArabicUsesRightToLeftDirection(string language, bool expected)
        => Assert.Equal(expected, UiLanguage.IsRightToLeft(language));

    [Fact]
    public Task LanguageMenusRequireOptInAndKeepTheSelectedAdditionalChoice() => StaTest.Run(() =>
    {
        var defaultMenu = new ContextMenu(); var extendedMenu = new ContextMenu(); var selected = "ko";
        PreferenceMenus.AddLanguageChoices(defaultMenu.Items, "en", "en", _ => { });
        PreferenceMenus.AddLanguageChoices(extendedMenu.Items, selected, "en", value => selected = value, includeAdditionalLanguages: true);
        Assert.Equal(4, defaultMenu.Items.Count);
        Assert.Equal(9, extendedMenu.Items.Count);
        var checkedItem = Assert.Single(extendedMenu.Items.OfType<MenuItem>(), item => item.IsChecked);
        Assert.Equal("한국어", checkedItem.Header);
        var arabic = extendedMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "العربية"));
        arabic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("ar", selected);
    });
}
