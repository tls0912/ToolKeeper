using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CabiDock.Models;
using CabiDock.Services;
using CabiDock.Views;
using Xunit;

namespace CabiDock.Tests;

[Collection("WPF layout bindings")]
public sealed class GroupItemRefreshTests
{
    [Fact]
    public Task PreviewCatalogRefreshPreservesFileScrollAndContent() => OnSta(() =>
    {
        var configuration = ConfigurationService.LoadDefaults();
        var items = Enumerable.Range(0, 30).Select(index =>
            new DesktopItem($@"C:\CabiDock-item-refresh\notes-{index:00}.txt", false, $"item-{index}")).ToArray();
        var state = new CabiDockState();
        new ClassificationService().Reconcile(items, state, configuration);
        DesktopTool[] tools = [new("001", "汗青", "Ready", "Open", ActivationUri: "toolkeeper://run/001")];
        var preview = new GroupPreviewWindow { AllowClose = true };
        try
        {
            preview.SetTools(tools, _ => { });
            preview.SetItems(configuration, state, items);
            var content = DetachContent(preview);
            Layout(content, 1080, 650);
            var canvas = Descendants<Canvas>(content).Single();
            var card = canvas.Children.OfType<FrameworkElement>().Single(element =>
                element.Visibility == Visibility.Visible && Descendants<TextBlock>(element).Any(text => text.Text == "文件類"));
            var header = Descendants<Thumb>(card).First();
            header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Layout(content, 1080, 650);
            var scroll = Descendants<ScrollViewer>(card).Single();
            scroll.ScrollToVerticalOffset(350);
            Layout(content, 1080, 650);
            var offset = scroll.VerticalOffset;
            Assert.True(offset > 0);
            var label = Descendants<TextBlock>(card).Single(text => text.Text == "notes-00.txt");

            // Platform polling supplies new objects even when the catalog and files are unchanged.
            preview.SetTools(tools.Select(tool => tool with { }).ToArray(), _ => { });
            preview.SetItems(ConfigurationService.Normalize(configuration), state,
                items.Reverse().Select(item => new DesktopItem(item.FullPath, item.IsDirectory, item.Identity)).ToArray());
            Layout(content, 1080, 650);

            Assert.Same(scroll, Descendants<ScrollViewer>(card).Single());
            Assert.Equal(offset, scroll.VerticalOffset);
            Assert.Same(label, Descendants<TextBlock>(card).Single(text => text.Text == "notes-00.txt"));
            Assert.True(card.Width > 138);
        }
        finally { preview.Close(); }
    });

    [Theory]
    [InlineData("case")]
    [InlineData("identity")]
    [InlineData("type")]
    public Task ChangedItemSnapshotRefreshesTheDisplayedItemAndAssignment(string change) => OnSta(() =>
    {
        var category = new CategoryDefinition { Id = "documents", Name = "文件類" };
        var item = new DesktopItem(@"C:\CabiDock-item-refresh\notes.txt", false, "original-id");
        var group = new GroupWindow(category, new GroupLayout());
        try
        {
            group.UpdateItems([item], [category]);
            group.Expand();
            var content = DetachContent(group);
            Layout(content, 360, 320);
            var oldScroll = Descendants<ScrollViewer>(content).Single();
            if (change == "case") item.FullPath = @"C:\CabiDock-item-refresh\NOTES.txt";
            else if (change == "identity") item.Identity = "replacement-id";
            else item.IsDirectory = true;

            group.UpdateItems([item], [category]);
            Layout(content, 360, 320);

            Assert.NotSame(oldScroll, Descendants<ScrollViewer>(content).Single());
            Assert.Contains(Descendants<TextBlock>(content), text => text.Text == item.Name);
            DesktopItem? assigned = null;
            group.ManualAssignmentRequested += (selected, _) => assigned = selected;
            AssignmentChoices(content).Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.NotNull(assigned);
            Assert.Equal(item.FullPath, assigned.FullPath);
            Assert.Equal(item.Identity, assigned.Identity);
            Assert.Equal(item.IsDirectory, assigned.IsDirectory);
        }
        finally { group.Close(); }
    });

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("order")]
    [InlineData("add")]
    [InlineData("remove")]
    public Task ChangedCategoryChoicesRefreshMenuLabelsAndTargets(string change) => OnSta(() =>
    {
        CategoryDefinition[] categories =
        [
            new() { Id = "documents", Name = "文件類" },
            new() { Id = "archives", Name = "壓縮檔" }
        ];
        var item = new DesktopItem(@"C:\CabiDock-item-refresh\notes.txt", false);
        var group = new GroupWindow(categories[0], new GroupLayout());
        try
        {
            group.UpdateItems([item], categories);
            group.Expand();
            var content = DetachContent(group);
            Layout(content, 360, 320);
            if (change == "id") categories[1].Id = "custom-archive";
            else if (change == "name") categories[1].Name = "封存";
            else if (change == "order") Array.Reverse(categories);
            else if (change == "add") categories = [.. categories, new() { Id = "images", Name = "圖像類" }];
            else categories = [categories[0]];

            group.UpdateItems([item], categories);
            Layout(content, 360, 320);

            var choices = AssignmentChoices(content);
            Assert.Equal(categories.Select(category => category.Name), choices.Select(choice => (string)choice.Header));
            string? assignedCategory = null;
            group.ManualAssignmentRequested += (_, target) => assignedCategory = target;
            choices.Last().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(categories.Last().Id, assignedCategory);
        }
        finally { group.Close(); }
    });

    [Fact]
    public Task ChangedMembershipUpdatesVisibleItemsAndCount() => OnSta(() =>
    {
        var category = new CategoryDefinition { Id = "documents", Name = "文件類" };
        var first = new DesktopItem(@"C:\CabiDock-item-refresh\first.txt", false);
        var second = new DesktopItem(@"C:\CabiDock-item-refresh\second.txt", false);
        var group = new GroupWindow(category, new GroupLayout());
        try
        {
            group.UpdateItems([first], [category]);
            group.Expand();
            var content = DetachContent(group);
            Layout(content, 360, 320);

            group.UpdateItems([second, first], [category]);
            Layout(content, 360, 320);
            Assert.Contains(Descendants<TextBlock>(content), text => text.Text == first.Name);
            Assert.Contains(Descendants<TextBlock>(content), text => text.Text == second.Name);
            Assert.Contains(Descendants<TextBlock>(content), text => text.Text == "2 個項目");

            group.UpdateItems([second], [category]);
            Layout(content, 360, 320);
            Assert.DoesNotContain(Descendants<TextBlock>(content), text => text.Text == first.Name);
            Assert.Contains(Descendants<TextBlock>(content), text => text.Text == second.Name);
            Assert.Contains(Descendants<TextBlock>(content), text => text.Text == "1 個項目");
        }
        finally { group.Close(); }
    });

    private static MenuItem[] AssignmentChoices(DependencyObject content) =>
        Descendants<FrameworkElement>(content).Single(element => element.ContextMenu is not null)
            .ContextMenu.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, "手動指定分類"))
            .Items.OfType<MenuItem>().ToArray();

    private static FrameworkElement DetachContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        return new Border { Child = content, Resources = window.Resources };
    }

    private static void Layout(FrameworkElement content, int width, int height)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        content.UpdateLayout();
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
            catch (Exception error) { completion.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true, Name = "CabiDock item refresh" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
