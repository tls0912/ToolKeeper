using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;

namespace ToolKeeper.UI.Tests;

public sealed class SharedControlsTests
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants<T>(child)) yield return nested;
    }

    private static void SharedResources(Window owner, string theme)
    {
        owner.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/ToolKeeper.UI;component/Resources/UiStyles.xaml", UriKind.Relative) });
        owner.Resources["UiFontFamily"] = new FontFamily("Segoe UI"); owner.Resources["UiFontSize"] = 16d;
        UiTheme.ApplyResources(owner.Resources, theme);
    }

    [Fact]
    public Task SharedResourcesAreOptInAndWorkInAnOrdinaryWindow() => StaTest.Run(() =>
    {
        var optIn = new Window(); var untouched = new Window(); SharedResources(optIn, "InkDark");
        Assert.IsType<Style>(optIn.FindResource(typeof(Button))); Assert.IsType<Style>(optIn.FindResource(typeof(TextBox)));
        Assert.IsType<Style>(optIn.FindResource("SidebarScrollViewerStyle"));
        Assert.IsType<Style>(optIn.FindResource(typeof(MenuItem)));
        Assert.Empty(untouched.Resources.MergedDictionaries);
        Assert.False(untouched.Resources.Contains("SurfaceBrush"));
        Assert.Equal("#FF1F2421", ((SolidColorBrush)optIn.FindResource("SurfaceBrush")).Color.ToString());
        optIn.Close(); untouched.Close();
    });

    [Fact]
    public Task TwoOwnerWindowsKeepIndependentLanguageAndThemeMenus() => StaTest.Run(() =>
    {
        var first = new Window(); var second = new Window(); SharedResources(first, "Ink"); SharedResources(second, "Dark");
        var firstLanguage = "zh-TW"; var secondLanguage = "ja";
        var firstMenu = new ContextMenu { Resources = first.Resources }; var secondMenu = new ContextMenu { Resources = second.Resources };
        PreferenceMenus.AddLanguageChoices(firstMenu.Items, firstLanguage, firstLanguage, value => firstLanguage = value);
        PreferenceMenus.AddLanguageChoices(secondMenu.Items, secondLanguage, secondLanguage, value => secondLanguage = value);
        Assert.Equal(1, firstMenu.Items.OfType<MenuItem>().Count(item => item.IsChecked));
        Assert.Equal(1, secondMenu.Items.OfType<MenuItem>().Count(item => item.IsChecked));
        Assert.All(firstMenu.Items.OfType<MenuItem>(), item => Assert.True(item.IsCheckable));
        ((MenuItem)firstMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("en", firstLanguage); Assert.Equal("ja", secondLanguage);
        Assert.NotEqual(((MenuItem)firstMenu.Items[0]).Header, ((MenuItem)secondMenu.Items[0]).Header);
        firstMenu.Items.Clear(); secondMenu.Items.Clear();
        PreferenceMenus.AddThemeChoices(firstMenu.Items, "Ink", firstLanguage, value => UiTheme.ApplyResources(first.Resources, value));
        PreferenceMenus.AddThemeChoices(secondMenu.Items, "Dark", secondLanguage, value => UiTheme.ApplyResources(second.Resources, value));
        var untouched = second.Resources["SurfaceBrush"];
        ((MenuItem)firstMenu.Items[3]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("#FF1F2421", ((SolidColorBrush)first.Resources["SurfaceBrush"]).Color.ToString());
        Assert.Same(untouched, second.Resources["SurfaceBrush"]);
        first.Close(); second.Close();
    });

    [Fact]
    public Task MenuErrorsReachTheHostErrorHandlerWithoutLosingTheOriginalException() => StaTest.Run(() =>
    {
        var menu = new ContextMenu(); var expected = new InvalidOperationException("test callback"); Exception? reported = null;
        PreferenceMenus.AddLanguageChoices(menu.Items, "en", "en", _ => throw expected, exception => reported = exception);
        var args = new RoutedEventArgs(MenuItem.ClickEvent); ((MenuItem)menu.Items[0]).RaiseEvent(args);
        Assert.Same(expected, reported); Assert.True(args.Handled);
    });

    [Theory]
    [InlineData("en", "Close")]
    [InlineData("zh-TW", "關閉")]
    [InlineData("ja", "閉じる")]
    public Task AboutContentUsesCallerMetadataAndLocalizedCloseAction(string language, string closeLabel) => StaTest.Run(() =>
    {
        var closes = 0; var product = new AboutInfo("Harbor Test", "9.8.7", "A host supplied description.", "Example Author", "Example Suite");
        var body = AboutContent.Create(product, language, () => closes++, 20);
        var text = string.Join("\n", Descendants<TextBlock>(body).Select(block => block.Text));
        Assert.Contains("Harbor Test", text); Assert.Contains("9.8.7", text);
        Assert.Contains(product.Description, text); Assert.Contains(product.Author, text); Assert.Contains(product.Brand, text);
        Assert.DoesNotContain("汗青", text); Assert.DoesNotContain("MarkPad", text);
        var button = Assert.IsType<Button>(body.FindName("AboutCloseButton")); Assert.Equal(closeLabel, button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, closes);
        Assert.True(body.Width >= 300);
    });
}
