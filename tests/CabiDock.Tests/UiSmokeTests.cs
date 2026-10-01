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
using ToolKeeper.UI;
using Xunit;

namespace CabiDock.Tests;

[Collection("WPF layout bindings")]
public sealed class UiSmokeTests
{
    [Fact]
    public Task SettingsRendersAtDefaultAndCompactSizesAndRejectsConflictingExtensions() => OnSta(() =>
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories.Add(new CategoryDefinition { Id = "technical", Name = "技術文件", IsCustom = true, Extensions = ["md"] });
        configuration.KeywordRules.Add(new KeywordRule { Keyword = "發票", CategoryId = "documents" });
        var window = new SettingsWindow(configuration) { AllowClose = true, SelectedLanguage = "zh-TW" };
        try
        {
            Assert.IsAssignableFrom<AppWindow>(window);
            Assert.Equal("CabiDock - Desktop Organizer", window.Title);
            Assert.True(window.Height <= SystemParameters.WorkArea.Height);
            var content = HostWindowContent(window);
            Render(content, 940, 790, "settings-default.png");
            AssertSettingsHeaderLayout(window, content);
            Render(content, 740, 660, "settings-compact.png");
            AssertSettingsHeaderLayout(window, content);
            var previews = 0;
            var desktopToggles = 0;
            window.PreviewRequested += (_, _) => previews++;
            window.DesktopToggleRequested += (_, _) => desktopToggles++;
            Descendants<Button>(content).Single(button => Equals(button.Content, "開啟群組操作預覽"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Descendants<Button>(content).Single(button => Equals(button.Content, "暫停桌面接管"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, previews);
            Assert.Equal(1, desktopToggles);
            var save = Descendants<Button>(content).Single(button => Equals(button.Content, "儲存並套用"));
            var point = save.TransformToAncestor(content).Transform(new Point());
            Assert.InRange(point.Y + save.ActualHeight, 1, 661);
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
    public Task PreferencesTranslateAndThemeTheExistingEditorsWithoutLosingUnsavedData() => OnSta(() =>
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories.Insert(0, new CategoryDefinition { Id = "notes", Name = "我的筆記", IsCustom = true, Extensions = ["md"] });
        configuration.KeywordRules.Add(new KeywordRule { Keyword = "原始規則", CategoryId = "notes" });
        var window = new SettingsWindow(configuration) { AllowClose = true, SelectedLanguage = "zh-TW" };
        try
        {
            var content = HostWindowContent(window);
            Layout(content, 740, 660);
            var grids = Descendants<DataGrid>(content).ToArray();
            var categories = grids[0];
            var rules = grids[1];
            foreach (var header in Descendants<DataGridColumnHeader>(categories).Where(value => value.Column is not null))
            {
                Assert.IsType<Thumb>(header.Template.FindName("PART_LeftHeaderGripper", header));
                Assert.IsType<Thumb>(header.Template.FindName("PART_RightHeaderGripper", header));
            }
            var category = window.Categories.Single(value => value.Id == "notes");
            category.Name = "尚未儲存的名稱";
            categories.SelectedItem = category;
            categories.ScrollIntoView(category);
            Layout(content, 740, 660);
            var editor = Descendants<TextBox>(categories).Single(box => ReferenceEquals(box.DataContext, category)
                && System.Windows.Data.BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == "ExtensionText");
            editor.Text = "md, test";
            editor.Select(4, 3);
            var rule = rules.Items[0];
            rule.GetType().GetProperty("Keyword")!.SetValue(rule, "未儲存的規則");
            rules.SelectedItem = rule;
            var source = categories.ItemsSource;
            var ruleSource = rules.ItemsSource;
            window.SetLocalizedStatus("Pending changes", "尚有修改", "未保存の変更");

            foreach (var (language, theme, saveLabel, kindLabel) in new[]
                     {
                         ("en", "Dark", "Save and apply", "Custom"),
                         ("ja", "InkDark", "保存して適用", "カスタム"),
                         ("zh-TW", "Ink", "儲存並套用", "自訂")
                     })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                Layout(content, 740, 660);
                Assert.Same(source, categories.ItemsSource);
                Assert.Same(ruleSource, rules.ItemsSource);
                Assert.Same(category, categories.SelectedItem);
                Assert.Same(rule, rules.SelectedItem);
                Assert.Equal("尚未儲存的名稱", category.Name);
                Assert.Equal("md, test", category.ExtensionText);
                Assert.Equal("未儲存的規則", rule.GetType().GetProperty("Keyword")!.GetValue(rule));
                Assert.Equal(kindLabel, category.KindLabel);
                // WPF may regenerate cell visuals when the inherited font changes;
                // inspect the visible editor, not a detached former cell instance.
                editor = Descendants<TextBox>(categories).Single(box => ReferenceEquals(box.DataContext, category)
                    && System.Windows.Data.BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == "ExtensionText");
                Assert.Equal("md, test", editor.Text);
                Assert.True(editor.IsEnabled && !editor.IsReadOnly);
                Assert.Contains(Descendants<Button>(content), button => Equals(button.Content, saveLabel));
                Assert.Equal(((SolidColorBrush)window.Resources["SurfaceBrush"]).Color,
                    Assert.IsType<SolidColorBrush>(categories.Background).Color);
                Assert.Equal(((SolidColorBrush)window.Resources["TextBrush"]).Color,
                    Assert.IsType<SolidColorBrush>(editor.Foreground).Color);
                var save = Descendants<Button>(content).Single(button => Equals(button.Content, saveLabel));
                Assert.InRange(Bounds(save, content).Bottom, 1, 661);
                Render(content, 740, 660, $"settings-{language}-{theme}.png");
            }
            var saved = 0;
            window.SaveRequested += (_, args) =>
            {
                saved++;
                Assert.Contains(args.Configuration.Categories, value => value.Id == "notes" && value.Name == "尚未儲存的名稱");
            };
            Descendants<Button>(content).Single(button => Equals(button.Content, "儲存並套用"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, saved);
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
    public Task PreviewKeepsToolsExpandedWhenOtherGroupsOpenAndCatalogChanges() => OnSta(() =>
    {
        var configuration = ConfigurationService.LoadDefaults();
        DesktopItem[] items = [new(@"C:\CabiDock-preview\notes.md", false), new(@"C:\CabiDock-preview\photo.png", false)];
        var state = new CabiDockState();
        new ClassificationService().Reconcile(items, state, configuration);
        state.Groups[DesktopToolsGroup.Id] = new GroupLayout { X = 24, Y = 24, Width = 460, Height = 400 };
        var window = new GroupPreviewWindow { AllowClose = true };
        try
        {
            var tools = Enumerable.Range(1, 5).Select(index => new DesktopTool($"{index:000}", $"工具 {index:000}", "由工具番開啟", "開啟",
                ActivationUri: $"toolkeeper://run/{index:000}")).ToArray();
            var activated = new List<string>();
            window.SetTools(tools, activated.Add);
            window.SetItems(configuration, state, items);
            var content = HostWindowContent(window);
            Layout(content, 1080, 650);
            var canvas = Descendants<Canvas>(content).Single();
            var cards = canvas.Children.OfType<FrameworkElement>().Where(card => card.Visibility == Visibility.Visible).ToArray();
            var toolCard = cards.Single(card => Descendants<TextBlock>(card).Any(text => text.Text == "工具番"));
            Assert.Equal(460, toolCard.Width);
            Assert.Equal(400, toolCard.Height);
            Assert.Equal(5, Descendants<Button>(toolCard).Count());
            var normalCards = cards.Where(card => card != toolCard).ToArray();
            foreach (var card in new[] { normalCards[0], toolCard, normalCards[1] })
            {
                var header = Descendants<Thumb>(card).First();
                header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            }
            Assert.Equal(138, normalCards[0].Width);
            Assert.True(normalCards[1].Width > 138);
            Assert.Equal(460, toolCard.Width);
            window.SetTools(tools.Select(tool => tool with { ActionLabel = "Open" }).ToArray(), activated.Add);
            window.SetItems(configuration, state, items);
            Layout(content, 1080, 650);
            Assert.Equal(460, toolCard.Width);
            Assert.Equal(400, toolCard.Height);
            Descendants<Button>(toolCard).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { "toolkeeper://run/001" }, activated);
            var toolBounds = new Rect(Canvas.GetLeft(toolCard), Canvas.GetTop(toolCard), toolCard.Width, toolCard.Height);
            foreach (var card in normalCards)
            {
                var cardBounds = new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.Width, card.Height);
                Assert.False(toolBounds.IntersectsWith(cardBounds), "The rendered preview must leave every tool entry accessible.");
            }
            Render(content, 1080, 650, "tools-group-preview.png");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PreviewHeaderSnapsToVisibleToolsAndIgnoresHiddenCategories() => OnSta(() =>
    {
        var configuration = ConfigurationService.LoadDefaults();
        DesktopItem[] items = [new(@"C:\CabiDock-preview\notes.md", false)];
        var state = new CabiDockState();
        new ClassificationService().Reconcile(items, state, configuration);
        state.Groups[DesktopToolsGroup.Id] = new GroupLayout { X = 100, Y = 80, Width = 320, Height = 240 };
        state.Groups["documents"] = new GroupLayout { X = 520, Y = 100, Width = 420, Height = 350 };
        state.Groups["images"] = new GroupLayout { X = 700, Y = 100, Width = 360, Height = 320 };
        var window = new GroupPreviewWindow { AllowClose = true };
        try
        {
            window.SetTools([new DesktopTool("001", "汗青", "Ready", "Open", ActivationUri: "toolkeeper://run/001")], _ => { });
            window.SetItems(configuration, state, items);
            var content = HostWindowContent(window);
            Layout(content, 1200, 800);
            var canvas = Descendants<Canvas>(content).Single();
            var cards = canvas.Children.OfType<FrameworkElement>().ToArray();
            var toolCard = cards.Single(card => card.Visibility == Visibility.Visible
                && Descendants<TextBlock>(card).Any(text => text.Text == "工具番"));
            var documentCard = cards.Single(card => card.Visibility == Visibility.Visible
                && Descendants<TextBlock>(card).Any(text => text.Text == "文件類"));
            var hiddenImageCard = cards.Single(card => Canvas.GetLeft(card) == 700 && Canvas.GetTop(card) == 100);
            Assert.Equal(Visibility.Collapsed, hiddenImageCard.Visibility);
            Assert.Equal(2, cards.Count(card => card.Visibility == Visibility.Visible));
            var saved = new List<(string Id, GroupLayout Layout)>();
            window.LayoutChanged += (id, layout) => saved.Add((id, layout));
            var header = Descendants<Thumb>(documentCard).First();

            header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            header.RaiseEvent(new DragDeltaEventArgs(-84, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            Assert.Equal(Canvas.GetLeft(toolCard) + toolCard.Width + 8, Canvas.GetLeft(documentCard));
            Assert.Equal(100, Canvas.GetTop(documentCard));
            Assert.Empty(saved);
            header.RaiseEvent(new DragCompletedEventArgs(-84, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            var firstSave = Assert.Single(saved);
            Assert.Equal("documents", firstSave.Id);
            Assert.Equal(428, firstSave.Layout.X);
            Assert.Equal(420, firstSave.Layout.Width);
            Assert.Equal(350, firstSave.Layout.Height);
            Render(content, 1200, 800, "groups-preview-snapped-to-tools.png");

            header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            header.RaiseEvent(new DragDeltaEventArgs(122, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            // A visible image group at x=700 would attract this card to x=554.
            Assert.Equal(550, Canvas.GetLeft(documentCard));
            header.RaiseEvent(new DragCompletedEventArgs(122, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Assert.Equal(2, saved.Count);
            Assert.Equal(550, saved[1].Layout.X);
            Assert.Equal(100, saved[1].Layout.Y);
            Assert.Equal(138, documentCard.Width);
            Assert.Equal(160, documentCard.Height);
            Assert.Equal(320, toolCard.Width);
            Assert.Equal(240, toolCard.Height);
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

    [Fact]
    public Task ReparentedGroupChangesThemeWithoutRebuildingItemsOrChangingItsLayout() => OnSta(() =>
    {
        var category = ConfigurationService.LoadDefaults().Categories.Single(value => value.Id == "documents");
        var group = new GroupWindow(category, new GroupLayout { X = 20, Y = 30, Width = 420, Height = 350 });
        try
        {
            group.UpdateItems([new DesktopItem(@"C:\CabiDock-preview\draft.md", false)], [category]);
            group.Expand();
            var card = Assert.IsType<Border>(group.CardContent);
            group.Content = null;
            var host = new Border { Child = card };
            Layout(host, 420, 350);
            var itemLabel = Descendants<TextBlock>(card).Single(text => text.Text == "draft.md");
            var body = card.Child;
            var bounds = new Rect(group.Left, group.Top, group.Width, group.Height);
            var layoutChanges = 0;
            group.LayoutChanged += _ => layoutChanges++;
            foreach (var theme in new[] { "Ink", "InkDark", "Light", "Dark", "Ink" })
            {
                group.SetTheme(theme);
                Layout(host, 420, 350);
                if (UiTheme.IsInk(theme)) Assert.IsType<DrawingBrush>(card.Background);
                else Assert.IsType<SolidColorBrush>(card.Background);
                if (UiTheme.IsInk(theme)) Render(host, 420, 350, $"bamboo-group-{theme}.png");
                Assert.Same(card.Resources["TextBrush"], TextElement.GetForeground(card));
                Assert.Same(body, card.Child);
                Assert.Same(itemLabel, Descendants<TextBlock>(card).Single(text => text.Text == "draft.md"));
                Assert.Same(host, VisualTreeHelper.GetParent(card));
                Assert.True(group.IsExpanded);
                Assert.Equal(bounds, new Rect(group.Left, group.Top, group.Width, group.Height));
            }
            Assert.Equal(0, layoutChanges);
        }
        finally { group.Close(); }
    });
    private static void AssertSettingsHeaderLayout(SettingsWindow window, FrameworkElement content)
    {
        var title = Descendants<TextBlock>(content).Single(text => text.Name == "WindowTitleText" && text.Text == window.Title);
        var description = Descendants<TextBlock>(content)
            .Single(text => text.Text == "桌面檔案自動分組，平時收起，需要時點開。");
        var titleBounds = Bounds(title, content);
        var descriptionBounds = Bounds(description, content);
        Assert.Null(window.HeaderActions);
        var workspace = Assert.IsAssignableFrom<FrameworkElement>(window.Workspace);
        var actions = Descendants<WrapPanel>(workspace).Single(panel => panel.Name == "WorkspaceActions");
        var actionsBounds = Bounds(actions, content);
        var workspaceBounds = Bounds(workspace, content);
        Assert.True(titleBounds.Height > 0);
        Assert.InRange(titleBounds.Top, 1, 40);
        Assert.True(descriptionBounds.Height > 0);
        Assert.True(titleBounds.Bottom <= descriptionBounds.Top + 1);
        Assert.False(titleBounds.IntersectsWith(actionsBounds));
        Assert.False(descriptionBounds.IntersectsWith(actionsBounds));
        Assert.True(workspaceBounds.Top >= descriptionBounds.Bottom);
        Assert.InRange(actionsBounds.Top, workspaceBounds.Top, workspaceBounds.Bottom);
        Assert.InRange(actionsBounds.Bottom, workspaceBounds.Top, workspaceBounds.Bottom);
        Assert.InRange(actionsBounds.Right, workspaceBounds.Left, workspaceBounds.Right + 0.5);
        Assert.Equal(2, actions.Children.Count);
        var categoryGrid = Descendants<DataGrid>(workspace).First();
        Assert.True(Bounds(categoryGrid, content).Top >= actionsBounds.Bottom);
        Assert.InRange(workspaceBounds.Bottom, workspaceBounds.Top + 1, content.ActualHeight + 1);
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

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
        if (window is AppWindow)
        {
            host.SetResourceReference(Border.BackgroundProperty, "ChromeBackgroundBrush");
            host.SetResourceReference(TextElement.FontFamilyProperty, "UiFontFamily");
            host.SetResourceReference(TextElement.FontSizeProperty, "UiFontSize");
            host.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        }
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
