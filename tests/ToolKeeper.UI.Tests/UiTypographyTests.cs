using System.Windows;
using System.Windows.Media;
using Xunit;

namespace ToolKeeper.UI.Tests;

public sealed class UiTypographyTests
{
    [Theory]
    [InlineData("zh-TW", "Microsoft JhengHei UI", "Microsoft JhengHei")]
    [InlineData("ja", "Yu Gothic UI", "Yu Gothic")]
    [InlineData("en", "Segoe UI", "Segoe UI")]
    public void AutomaticPlainFontsKeepInterfaceAndReadingDefaults(string language, string ui, string reading)
    {
        Assert.Equal(ui, UiTypography.ResolveInterfaceFont("", language, false));
        Assert.Equal(reading, UiTypography.ResolveReadingFont(null, language, false));
    }

    [Fact]
    public Task AutomaticInkAndExplicitInkChoicesUseTheSharedInstalledFontResolver() => StaTest.Run(() =>
    {
        foreach (var language in new[] { "en", "zh-TW", "ja" })
        {
            var expected = InkTypography.Resolve(language);
            Assert.Equal(expected, UiTypography.ResolveInterfaceFont("", language, true));
            Assert.Equal(expected, UiTypography.ResolveReadingFont(" ", language, true));
            Assert.Equal(expected, UiTypography.ResolveInterfaceFont(InkTypography.FontChoice, language, false));
            Assert.Equal(expected, UiTypography.ResolveReadingFont(InkTypography.FontChoice, language, false));
        }
    });

    [Fact]
    public void ExplicitCustomFontsArePreservedAcrossLanguageAndInkChoices()
    {
        foreach (var language in new[] { "en", "zh-TW", "ja" })
            foreach (var ink in new[] { false, true })
            {
                Assert.Equal("Custom UI Family", UiTypography.ResolveInterfaceFont("Custom UI Family", language, ink));
                Assert.Equal("Custom Reading Family", UiTypography.ResolveReadingFont("Custom Reading Family", language, ink));
            }
    }

    [Fact]
    public Task TypographyResourcesStayInTheirOwnerAndPreserveOtherResources() => StaTest.Run(() =>
    {
        var first = new ResourceDictionary { ["ProductValue"] = 42 }; var second = new ResourceDictionary();
        var firstFamily = new FontFamily("Segoe UI"); var secondFamily = new FontFamily("Consolas");
        UiTypography.ApplyResources(first, firstFamily, 20); UiTypography.ApplyResources(second, secondFamily, 12);
        Assert.Same(firstFamily, first["UiFontFamily"]); Assert.Same(secondFamily, second["UiFontFamily"]);
        Assert.Equal(20d, first["UiFontSize"]); Assert.Equal(12d, second["UiFontSize"]); Assert.Equal(42, first["ProductValue"]);
        UiTypography.ApplyResources(first, firstFamily, double.NaN);
        Assert.Equal(16d, first["UiFontSize"]); Assert.Equal(12d, second["UiFontSize"]);
        Assert.True((double)first["UiMenuMaxWidth"] >= 320);
        UiTypography.ApplyResources(first, firstFamily, 200); Assert.Equal(20d, first["UiFontSize"]);
        UiTypography.ApplyResources(first, firstFamily, 1); Assert.Equal(10d, first["UiFontSize"]);
    });
}
