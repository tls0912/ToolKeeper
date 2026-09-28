using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ToolKeeper.UI;
using Xunit;

namespace ToolKeeper.UI.Tests;

public sealed class AppWindowTests
{
    [Fact]
    public Task HeaderKeepsThreeActionsBesideTitleAndBadgeBesideDescription() => StaTest.Run(() =>
    {
        var badge = new TextBlock { Text = "Local · Offline", FontSize = 12 };
        var window = new AppWindow
        {
            MainName = "ToolKeeper", SubName = "Tools", Description = "Utilities that keep your files local.",
            HeaderActions = badge, Workspace = new TextBox { Text = "Keep this unsaved text" }
        };
        try
        {
            var body = (FrameworkElement)window.Content;
            window.Content = null;
            var host = new Border { Child = body, Resources = window.Resources };
            TextElement.SetFontFamily(host, window.FontFamily);
            TextElement.SetFontSize(host, window.FontSize);
            foreach (var language in new[] { "zh-TW", "en", "ja" })
            foreach (var theme in new[] { "Light", "Dark", "Ink", "InkDark", "System" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                foreach (var width in new[] { 844d, 724d, 450d })
                {
                    host.Measure(new Size(width, 700)); host.Arrange(new Rect(0, 0, width, 700)); host.UpdateLayout();
                    var heading = Descendants<TextBlock>(body).Single(t => t.Name == "ProductHeading");
                    var description = Descendants<TextBlock>(body).Single(t => t.Name == "ProductDescription");
                    var buttons = Descendants<Button>(body).Where(b => b.Name.StartsWith("Shared")).ToArray();
                    Assert.Equal(new[] { "SharedThemeButton", "SharedLanguageButton", "SharedAboutButton" }, buttons.Select(b => b.Name));
                    var titleBounds = Bounds(heading, host);
                    Assert.InRange(titleBounds.Top, 0, 0.5);
                    foreach (var button in buttons)
                    {
                        var bounds = Bounds(button, host);
                        Assert.True(bounds.Left >= titleBounds.Right);
                        Assert.True(bounds.Right <= width);
                        Assert.InRange(bounds.Top + bounds.Height / 2, titleBounds.Top, titleBounds.Bottom);
                        Assert.Equal(((SolidColorBrush)window.Resources["TextBrush"]).Color, ((SolidColorBrush)button.Foreground).Color);
                        Assert.Equal(Colors.Transparent, ((SolidColorBrush)button.Background).Color);
                    }
                    Assert.True(Bounds(badge, host).Left >= Bounds(description, host).Right);
                    Assert.True(Bounds(description, host).Top >= titleBounds.Bottom);
                }
            }
            Assert.Equal("Keep this unsaved text", ((TextBox)window.Workspace!).Text);
            Assert.Null(window.PreferencesPath);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PreferencesStayPerOwnerAndNotifyProductWithoutReplacingContent() => StaTest.Run(() =>
    {
        var first = new AppWindow { SelectedLanguage = "en", Workspace = new TextBox { Text = "Draft", SelectionStart = 3 } };
        var second = new AppWindow { SelectedTheme = "Light", SelectedLanguage = "ja" };
        try
        {
            var workspace = first.Workspace;
            var notifications = 0;
            first.UiPreferencesChanged += (_, _) => notifications++;
            first.SelectedTheme = "InkDark";
            first.SelectedLanguage = "zh-TW";
            Assert.Equal(2, notifications);
            Assert.Same(workspace, first.Workspace);
            Assert.Equal(3, ((TextBox)first.Workspace!).SelectionStart);
            Assert.Equal("Light", second.SelectedTheme);
            Assert.Equal("ja", second.ResolvedLanguage);
            Assert.NotEqual(((SolidColorBrush)first.Background).Color, ((SolidColorBrush)second.Background).Color);
        }
        finally { first.Close(); second.Close(); }
    });

    [Fact]
    public void StoreRoundTripsRawSystemAndSeparatesProductsWithoutTouchingOtherSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ToolKeeper-header-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "First", "ui.json");
            var second = Path.Combine(directory, "Second", "ui.json");
            var productSettings = Path.Combine(directory, "settings.json");
            File.WriteAllText(productSettings, "keep-product-data");
            new AppWindowPreferences("InkDark", "System").Save(first);
            new AppWindowPreferences("Light", "ja").Save(second);
            Assert.Equal(new AppWindowPreferences("InkDark", "System"), AppWindowPreferences.Load(first));
            Assert.Equal(new AppWindowPreferences("Light", "ja"), AppWindowPreferences.Load(second));
            Assert.Equal("keep-product-data", File.ReadAllText(productSettings));
            File.WriteAllText(first, "{\"Theme\":\"unknown\",\"Language\":\"unknown\"}");
            Assert.Equal(new AppWindowPreferences(), AppWindowPreferences.Load(first));
            File.WriteAllText(first, "broken-json");
            Assert.Equal(new AppWindowPreferences(), AppWindowPreferences.Load(first));
            Assert.Equal("broken-json", File.ReadAllText(first));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Rect Bounds(FrameworkElement element, Visual host) =>
        element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
