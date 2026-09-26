using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CabiDock.Models;
using CabiDock.Views;
using Xunit;

namespace CabiDock.Tests;

public sealed class GroupCollapseDelayTests
{
    [Fact]
    public Task LeavingWaitsThreeSecondsAndReenteringCancelsCollapse() => OnSta(() =>
    {
        var group = CreateGroup();
        try
        {
            var card = group.CardContent;
            Leave(card);
            Pump(400);
            Assert.True(group.IsExpanded);
            Pump(2800);
            Assert.False(group.IsExpanded);

            group.Expand();
            Leave(card);
            Pump(300);
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            Pump(3100);
            Assert.True(group.IsExpanded);
        }
        finally { group.Close(); }
    });

    [Fact]
    public Task MenuInteractionHoldsExpansionAndDismissalStartsAFullDelay() => OnSta(() =>
    {
        var group = CreateGroup();
        try
        {
            var card = group.CardContent;
            card.Measure(new Size(360, 320));
            card.Arrange(new Rect(0, 0, 360, 320));
            card.UpdateLayout();
            var menu = Descendants(card).OfType<FrameworkElement>().First(element => element.ContextMenu is not null).ContextMenu;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.True(group.IsInteractionActive);
            Leave(card);
            group.Collapse(); // Another category expansion must not close an active menu.
            Pump(3100);
            Assert.True(group.IsExpanded);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
            Assert.False(group.IsInteractionActive);
            Pump(400);
            Assert.True(group.IsExpanded);
            Pump(2800);
            Assert.False(group.IsExpanded);
        }
        finally { group.Close(); }
    });

    private static GroupWindow CreateGroup()
    {
        var category = new CategoryDefinition { Id = "test", Name = "Test" };
        var group = new GroupWindow(category, new GroupLayout());
        group.UpdateItems([new DesktopItem(@"C:\CabiDock-tests\test.txt", false)], [category]);
        group.Expand();
        return group;
    }

    private static void Leave(FrameworkElement card) =>
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
