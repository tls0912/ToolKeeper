using System.Windows;
using System.Windows.Media;
using Xunit;

namespace ToolKeeper.UI.Tests;

public sealed class UiThemeTests
{
    [Theory]
    [InlineData("Ink", false, true, "#FFF3F0E7", "#FF2C302D")]
    [InlineData("InkDark", true, true, "#FF1F2421", "#FFE5E1D5")]
    [InlineData("Light", false, false, "#FFE8EBEF", "#FF1F2328")]
    [InlineData("Dark", true, false, "#FF0D1117", "#FFE6EDF3")]
    public Task ApplyingAPreferencePreservesTheExistingAppearance(string theme, bool dark, bool ink, string surface, string text) => StaTest.Run(() =>
    {
        var resources = new ResourceDictionary { ["ProductSpecificResource"] = "kept" };
        UiTheme.ApplyResources(resources, theme);
        Assert.True(UiTheme.IsSupported(theme)); Assert.Equal(dark, UiTheme.IsDark(theme)); Assert.Equal(ink, UiTheme.IsInk(theme));
        Assert.Equal(surface, ((SolidColorBrush)resources["SurfaceBrush"]).Color.ToString());
        Assert.Equal(text, ((SolidColorBrush)resources["TextBrush"]).Color.ToString());
        Assert.Equal("kept", resources["ProductSpecificResource"]);
        Assert.Equal(8, resources.Count);
        Assert.Equal(UiTheme.Palette(dark, ink).Accent, ((SolidColorBrush)resources["AccentBrush"]).Color);
    });

    [Fact]
    public void ThemeCyclingAndSystemResolutionDoNotDependOnOtherCallers()
    {
        Assert.Equal("InkDark", UiTheme.Next("Ink")); Assert.Equal("Light", UiTheme.Next("InkDark"));
        Assert.Equal("Dark", UiTheme.Next("Light")); Assert.Equal("Ink", UiTheme.Next("Dark"));
        Assert.Equal("Dark", UiTheme.Next("System", false)); Assert.Equal("Ink", UiTheme.Next("System", true));
        Assert.True(UiTheme.IsDark("System", true)); Assert.False(UiTheme.IsDark("System", false));
        Assert.False(UiTheme.IsDark("unknown", true)); Assert.False(UiTheme.IsInk(null));
        Assert.False(UiTheme.IsSupported("unknown"));
        Assert.Equal(new[] { "Light", "Dark", "Ink", "InkDark", "System" }, UiTheme.Choices("en").Select(choice => choice.Value));
        Assert.Equal("竹子（深色）", UiTheme.Choices("zh-TW").Single(choice => choice.Value == "InkDark").Label);
    }

    [Fact]
    public Task ThemeChangesAreScopedToTheSuppliedWindowResources() => StaTest.Run(() =>
    {
        var first = new Window(); var second = new Window();
        UiTheme.ApplyResources(first.Resources, "Ink"); UiTheme.ApplyResources(second.Resources, "Dark");
        var secondSurface = (SolidColorBrush)second.Resources["SurfaceBrush"];
        UiTheme.ApplyResources(first.Resources, "InkDark");
        Assert.Equal("#FF1F2421", ((SolidColorBrush)first.Resources["SurfaceBrush"]).Color.ToString());
        Assert.Same(secondSurface, second.Resources["SurfaceBrush"]);
        Assert.Equal("#FF0D1117", secondSurface.Color.ToString());
        Assert.NotSame(first.Resources["SurfaceBrush"], secondSurface);
        first.Close(); second.Close();
    });

    [Fact]
    public Task InkFontChoiceIsReusableWithoutAnApplicationOrProductSetting() => StaTest.Run(() =>
    {
        Assert.Equal("@ink", InkTypography.FontChoice);
        foreach (var language in new[] { "en", "zh-TW", "ja" })
        {
            var family = InkTypography.Resolve(language);
            Assert.False(string.IsNullOrWhiteSpace(family));
            Assert.Equal(family, InkTypography.Resolve(language));
        }
    });
}
