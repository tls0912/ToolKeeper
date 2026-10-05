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
    public Task SharedFrameKeepsPreferencesRightOfTheDescription() => StaTest.Run(() =>
    {
        var badge = new TextBlock { Text = "Local · Offline", FontSize = 12 };
        var workspace = new TextBox { Text = "Keep this unsaved text" };
        var window = new AppWindow
        {
            MainName = "ToolKeeper", SubName = "Tools", Description = "Utilities that keep your files local.",
            HeaderActions = badge, Workspace = workspace
        };
        try
        {
            Assert.Equal("Ink", window.SelectedTheme);
            var frame = Assert.IsType<WindowFrame>(window.Content);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.True(frame.IsCaptionVisible);
            Assert.Null(frame.CaptionContent);
            Assert.Null(frame.CaptionActions);
            Assert.DoesNotContain(Descendants<TextBlock>(frame), text => text.Name == "ProductHeading");
            var title = Descendants<TextBlock>(frame).Single(text => text.Name == "WindowTitleText");
            Assert.Equal("ToolKeeper - Tools", title.Text);
            window.SubName = "Tools and Utilities";
            Assert.Equal("ToolKeeper - Tools and Utilities", title.Text);
            var caption = Descendants<Border>(frame).Single(border => border.Name == "TitleBar");
            var minimize = Descendants<Button>(frame).Single(button => button.Name == "MinimizeButton");
            var maximize = Descendants<Button>(frame).Single(button => button.Name == "MaximizeButton");
            var close = Descendants<Button>(frame).Single(button => button.Name == "WindowCloseButton");
            var description = Descendants<TextBlock>(frame).Single(text => text.Name == "ProductDescription");
            var buttons = Descendants<Button>(frame).Where(button => button.Name.StartsWith("Shared")).ToArray();
            Assert.Equal(new[] { "SharedThemeButton", "SharedLanguageButton", "SharedAboutButton" }, buttons.Select(button => button.Name));

            window.Content = null;
            var host = new Border { Child = frame, Resources = window.Resources };
            TextElement.SetFontFamily(host, window.FontFamily);
            TextElement.SetFontSize(host, window.FontSize);
            foreach (var language in new[] { "zh-TW", "en", "ja" })
            foreach (var theme in new[] { "Light", "Dark", "Ink", "InkDark", "System", "Light" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                Assert.Same(window.Resources["ChromeBackgroundBrush"], window.Background);
                Assert.Same(window.Resources["ChromeTitleBrush"], caption.Background);
                if (UiTheme.IsInk(theme))
                {
                    Assert.IsType<DrawingBrush>(window.Background);
                    Assert.IsType<DrawingBrush>(caption.Background);
                }
                else
                {
                    Assert.IsType<SolidColorBrush>(window.Background);
                    Assert.IsType<SolidColorBrush>(caption.Background);
                }
                Assert.Equal(UiLanguage.Text(language, "Style", "風格", "スタイル"), buttons[0].Content);
                Assert.Equal(UiLanguage.Text(language, "Language", "語言", "言語"), buttons[1].Content);
                Assert.Equal(UiLanguage.Text(language, "About", "關於", "情報"), buttons[2].Content);
                foreach (var width in new[] { 844d, 724d, 450d, 320d })
                {
                    host.Measure(new Size(width, 700));
                    host.Arrange(new Rect(0, 0, width, 700));
                    host.UpdateLayout();
                    var titleBounds = Bounds(title, host);
                    var captionBounds = Bounds(caption, host);
                    Assert.True(titleBounds.Right <= Bounds(minimize, host).Left + 0.5);
                    Assert.True(Bounds(minimize, host).Right <= Bounds(maximize, host).Left + 0.5);
                    Assert.True(Bounds(maximize, host).Right <= Bounds(close, host).Left + 0.5);
                    Assert.True(Bounds(close, host).Right <= width + 0.5);
                    var descriptionBounds = Bounds(description, host);
                    Assert.True(descriptionBounds.Top >= captionBounds.Bottom - 0.5);
                    var preferenceBounds = buttons.Select(button => Bounds(button, host)).ToArray();
                    foreach (var bounds in preferenceBounds)
                    {
                        Assert.True(bounds.Left >= descriptionBounds.Right - 0.5);
                        Assert.InRange(Math.Abs(bounds.Top + bounds.Height / 2 - descriptionBounds.Top - descriptionBounds.Height / 2), 0, 1);
                    }
                    Assert.InRange(width - preferenceBounds[^1].Right, 28, 30);
                    var badgeBounds = Bounds(badge, host);
                    Assert.True(badgeBounds.Top >= Math.Max(descriptionBounds.Bottom, preferenceBounds.Max(bounds => bounds.Bottom)) - 0.5);
                    var commandBounds = preferenceBounds.Append(badgeBounds).ToArray();
                    for (var index = 0; index < commandBounds.Length; index++)
                    {
                        var bounds = commandBounds[index];
                        Assert.InRange(bounds.Left, 0, width);
                        Assert.True(bounds.Right <= width + 0.5);
                        for (var other = index + 1; other < commandBounds.Length; other++)
                            Assert.False(bounds.IntersectsWith(commandBounds[other]));
                    }
                    Assert.True(Bounds(workspace, host).Top >= commandBounds.Max(bounds => bounds.Bottom) - 0.5);
                    foreach (var button in buttons)
                    {
                        Assert.Equal(((SolidColorBrush)window.Resources["TextBrush"]).Color, ((SolidColorBrush)button.Foreground).Color);
                        Assert.Equal(Colors.Transparent, ((SolidColorBrush)button.Background).Color);
                    }
                }
            }
            Assert.Same(workspace, window.Workspace);
            Assert.Equal("Keep this unsaved text", workspace.Text);
            Assert.Null(window.PreferencesPath);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ThemeRoundTripPreservesWorkspaceAndOwnerPreferences() => StaTest.Run(() =>
    {
        var dataContext = new object();
        var editor = new TextBox { Text = "Draft", SelectionStart = 1, SelectionLength = 3 };
        var first = new AppWindow { SelectedTheme = "Light", SelectedLanguage = "en", Workspace = editor, DataContext = dataContext };
        var second = new AppWindow { SelectedTheme = "Light", SelectedLanguage = "ja" };
        try
        {
            var frame = Assert.IsType<WindowFrame>(first.Content);
            var body = frame.Workspace;
            var secondBackground = second.Background;
            var notifications = 0;
            first.UiPreferencesChanged += (_, _) => notifications++;
            foreach (var theme in new[] { "Ink", "InkDark", "Dark", "Light" })
            {
                first.SelectedTheme = theme;
                Assert.Same(frame, first.Content);
                Assert.Same(body, frame.Workspace);
                Assert.Same(editor, first.Workspace);
                Assert.Same(dataContext, first.DataContext);
                Assert.Equal("Draft", editor.Text);
                Assert.Equal(1, editor.SelectionStart);
                Assert.Equal(3, editor.SelectionLength);
                if (UiTheme.IsInk(theme)) Assert.IsType<DrawingBrush>(first.Background);
                else Assert.IsType<SolidColorBrush>(first.Background);
                Assert.Equal(UiTheme.Palette(UiTheme.IsDark(theme), UiTheme.IsInk(theme)).Text,
                    ((SolidColorBrush)first.Resources["TextBrush"]).Color);
            }
            first.SelectedLanguage = "zh-TW";
            Assert.Equal(5, notifications);
            Assert.Same(editor, first.Workspace);
            Assert.Equal(1, editor.SelectionStart);
            Assert.Equal(3, editor.SelectionLength);
            Assert.Equal("Light", second.SelectedTheme);
            Assert.Equal("ja", second.ResolvedLanguage);
            Assert.IsType<SolidColorBrush>(second.Background);
            Assert.Same(secondBackground, second.Background);
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
            Assert.Equal(new AppWindowPreferences("Ink", "System"), AppWindowPreferences.Load(first));
            File.WriteAllText(productSettings, "keep-product-data");
            new AppWindowPreferences("InkDark", "System").Save(first);
            new AppWindowPreferences("Light", "ja").Save(second);
            Assert.Equal(new AppWindowPreferences("InkDark", "System"), AppWindowPreferences.Load(first));
            Assert.Equal(new AppWindowPreferences("Light", "ja"), AppWindowPreferences.Load(second));
            Assert.Equal("keep-product-data", File.ReadAllText(productSettings));
            File.WriteAllText(first, "{\"Theme\":\"unknown\",\"Language\":\"unknown\"}");
            Assert.Equal(new AppWindowPreferences("Ink", "System"), AppWindowPreferences.Load(first));
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
