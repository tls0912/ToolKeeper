using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CabiDock.Models;
using CabiDock.Services;
using CabiDock.Views;
using Xunit;

namespace CabiDock.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public Task SettingsRendersAtDefaultAndCompactSizesAndRejectsConflictingExtensions() => OnSta(() =>
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories.Add(new CategoryDefinition { Id = "technical", Name = "技術文件", IsCustom = true, Extensions = ["md"] });
        configuration.KeywordRules.Add(new KeywordRule { Keyword = "發票", CategoryId = "documents" });
        var window = new SettingsWindow(configuration) { AllowClose = true };
        try
        {
            Assert.True(window.Height <= SystemParameters.WorkArea.Height);
            var content = HostWindowContent(window);
            Render(content, 940, 790, "settings-default.png");
            Render(content, 740, 620, "settings-compact.png");
            var save = Descendants<Button>(content).Single(button => Equals(button.Content, "儲存並套用"));
            var point = save.TransformToAncestor(content).Transform(new Point());
            Assert.InRange(point.Y + save.ActualHeight, 1, 621);
            var saves = 0;
            window.SaveRequested += (_, _) => saves++;
            window.Categories.Add(new SettingsWindow.CategoryEditor(new CategoryDefinition
            {
                Id = "duplicate", Name = "第二份技術文件", IsCustom = true, Extensions = [".MD"]
            }));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, saves);
            Assert.Contains(Descendants<TextBlock>(content), text => text.Text.Contains("已屬於自訂分類「技術文件」", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PreviewRendersOnlyPopulatedGroupsAndCanExpandHostedCard() => OnSta(() =>
    {
        var configuration = ConfigurationService.LoadDefaults();
        var items = new List<DesktopItem>
        {
            new(@"C:\CabiDock-preview\設計草稿.png", false),
            new(@"C:\CabiDock-preview\桌面收納筆記.md", false),
            new(@"C:\CabiDock-preview\專案資料", true),
            new(@"C:\CabiDock-preview\發票_202609.pdf", false)
        };
        var state = new CabiDockState();
        new ClassificationService().Reconcile(items, state, configuration);
        var window = new GroupPreviewWindow { AllowClose = true };
        try
        {
            window.SetItems(configuration, state, items);
            var content = HostWindowContent(window);
            Layout(content, 1080, 650);
            var canvas = Descendants<Canvas>(content).Single();
            Assert.Equal(3, canvas.Children.OfType<FrameworkElement>().Count(card => card.Visibility == Visibility.Visible));
            var documentCard = canvas.Children.OfType<FrameworkElement>().Single(card =>
                card.Visibility == Visibility.Visible && Descendants<TextBlock>(card).Any(text => text.Text == "文件類"));
            var header = Descendants<Thumb>(documentCard).First();
            header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Layout(content, 1080, 650);
            Assert.True(documentCard.Width > 138);
            Assert.Contains(Descendants<TextBlock>(documentCard), text => text.Text == "桌面收納筆記.md");
            Render(content, 1080, 650, "groups-preview.png");

            window.SetItems(configuration, new CabiDockState(), []);
            Assert.All(canvas.Children.OfType<FrameworkElement>(), card => Assert.Equal(Visibility.Collapsed, card.Visibility));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task GroupCardSurvivesReparentingAndStaysInsideBoundsWhenExpanded() => OnSta(() =>
    {
        var category = ConfigurationService.LoadDefaults().Categories.Single(category => category.Id == "documents");
        var group = new GroupWindow(category, new GroupLayout { X = 500, Y = 400, Width = 420, Height = 350 });
        try
        {
            var card = group.CardContent;
            group.Content = null;
            var host = new Canvas();
            host.Children.Add(card);
            group.SetPrimaryBounds(new Rect(0, 0, 600, 450));
            group.Expand();
            Assert.True(group.IsExpanded);
            Assert.InRange(group.Left + group.Width, 0, 600);
            Assert.InRange(group.Top + group.Height, 0, 450);
            Assert.Same(host, VisualTreeHelper.GetParent(card));
            group.Collapse();
            Assert.False(group.IsExpanded);
            Assert.Equal(138, group.Width);
            Assert.Equal(160, group.Height);
        }
        finally { group.Close(); }
    });

    private static FrameworkElement HostWindowContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Background = window.Background, Child = content };
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetFontSize(host, window.FontSize);
        TextElement.SetForeground(host, window.Foreground);
        // Reparented controls retain the window's styles in the offscreen render host.
        host.Resources = window.Resources;
        return host;
    }

    private static void Layout(FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        // DataGrid schedules star-column sizing after its initial layout pass.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
    }

    private static void Render(FrameworkElement content, int width, int height, string name)
    {
        Layout(content, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ToolKeeper.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, "artifacts", "cabidock-ui");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name));
        encoder.Save(file);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "CabiDock UI smoke" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
