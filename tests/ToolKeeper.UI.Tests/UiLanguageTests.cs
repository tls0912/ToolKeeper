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
        Assert.Equal("甲", UiLanguage.Text("zh-CN", "A", "甲", "あ"));
        Assert.Equal("あ", UiLanguage.Text("JA-jp", "A", "甲", "あ"));
        Assert.Equal("A", UiLanguage.Text("unsupported", "A", "甲", "あ"));
        Assert.Equal("甲", UiLanguage.Text("zh-TW", "A", "甲", "あ"));
    }
}
