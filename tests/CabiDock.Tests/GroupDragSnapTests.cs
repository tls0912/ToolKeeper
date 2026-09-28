using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CabiDock.Models;
using CabiDock.Views;
using Xunit;

namespace CabiDock.Tests;

[Collection("WPF layout bindings")]
public sealed class GroupDragSnapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HeaderDragCanEscapeWorkspaceEdgeOnePointerStepAtATime(bool vertical) => OnSta(() =>
    {
        var group = CreateGroup(vertical ? 80 : 0, vertical ? 0 : 80);
        try
        {
            var saved = new List<GroupLayout>();
            group.LayoutChanged += saved.Add;
            var header = Thumbs(group.CardContent).First();
            Start(header);
            Assert.True(group.IsInteractionActive);
            // While the card is held at the edge, Thumb reports the growing offset
            // from its initial local anchor, rather than repeated one-DIP deltas.
            for (var offset = 1; offset <= 12; offset++)
            {
                Delta(header, vertical ? 0 : offset, vertical ? offset : 0);
                Assert.Equal(0, vertical ? group.Top : group.Left);
            }
            Delta(header, vertical ? 0 : 13, vertical ? 13 : 0);
            Assert.Equal(13, vertical ? group.Top : group.Left);
            // Once the card follows the pointer again, the next local offset is one DIP.
            Delta(header, vertical ? 0 : 1, vertical ? 1 : 0);
            Assert.Equal(14, vertical ? group.Top : group.Left);
            Assert.Equal(138, group.Width);
            Assert.Equal(160, group.Height);
            Assert.Empty(saved);
            Complete(header, vertical ? 0 : 14, vertical ? 14 : 0);

            var layout = Assert.Single(saved);
            Assert.Equal(vertical ? 80 : 14, layout.X);
            Assert.Equal(vertical ? 14 : 80, layout.Y);
            Assert.Equal(420, layout.Width);
            Assert.Equal(350, layout.Height);
            Assert.False(group.IsInteractionActive);
            Assert.False(group.IsExpanded);
        }
        finally { group.Close(); }
    });

    [Fact]
    public Task ExpandedHeaderDragSnapsBothEdgesAndSavesWithoutChangingSize() => OnSta(() =>
    {
        var group = CreateGroup(100, 100);
        try
        {
            group.Expand();
            var saved = new List<GroupLayout>();
            group.LayoutChanged += saved.Add;
            var header = Thumbs(group.CardContent).First();
            Start(header);
            Delta(header, 471, 442);
            Assert.Equal(580, group.Left);
            Assert.Equal(550, group.Top);
            Assert.Equal(420, group.Width);
            Assert.Equal(350, group.Height);
            Assert.Empty(saved);
            Complete(header, 471, 442);
            var layout = Assert.Single(saved);
            Assert.Equal(580, layout.X);
            Assert.Equal(550, layout.Y);
            Assert.Equal(420, layout.Width);
            Assert.Equal(350, layout.Height);
            Assert.True(group.IsExpanded);
            Assert.False(group.IsInteractionActive);
        }
        finally { group.Close(); }
    });

    [Fact]
    public Task SuppressSnapUsesPointerPositionAndStillClampsToWorkArea() => OnSta(() =>
    {
        var group = CreateGroup(0, 80);
        try
        {
            group.MoveHeader(6, 0, suppressSnap: false);
            Assert.Equal(0, group.Left);
            // The header maps the Shift modifier to this deterministic bypass.
            group.SnapTargets = () => throw new InvalidOperationException("Bypass must not inspect snap targets.");
            group.MoveHeader(6, 0, suppressSnap: true);
            Assert.Equal(6, group.Left);
            Assert.Equal(80, group.Top);
            group.MoveHeader(-100, -100, suppressSnap: true);
            Assert.Equal(0, group.Left);
            Assert.Equal(0, group.Top);
            Assert.Equal(138, group.Width);
            Assert.Equal(160, group.Height);
        }
        finally { group.Close(); }
    });

    [Fact]
    public Task HalfDipHeaderMotionRemainsAClickWithoutSnappingOrSaving() => OnSta(() =>
    {
        var group = CreateGroup(10, 80);
        try
        {
            var saved = new List<GroupLayout>();
            group.LayoutChanged += saved.Add;
            var header = Thumbs(group.CardContent).First();
            Start(header);
            Delta(header, 0.5, 0);
            Assert.Equal(10, group.Left);
            Assert.Equal(80, group.Top);
            Complete(header, 0.5, 0);
            Assert.True(group.IsExpanded);
            Assert.Equal(10, group.Left);
            Assert.Empty(saved);
        }
        finally { group.Close(); }
    });

    [Fact]
    public Task ResizeDoesNotInvokeMoveSnapping() => OnSta(() =>
    {
        var group = CreateGroup(24, 24);
        try
        {
            group.Expand();
            group.SnapTargets = () => throw new InvalidOperationException("Resize must not inspect snap targets.");
            var saved = new List<GroupLayout>();
            group.LayoutChanged += saved.Add;
            var resize = Thumbs(group.CardContent).Last();
            Start(resize);
            Delta(resize, 5, 7);
            Complete(resize, 5, 7);
            Assert.Equal(24, group.Left);
            Assert.Equal(24, group.Top);
            Assert.Equal(425, group.Width);
            Assert.Equal(357, group.Height);
            var layout = Assert.Single(saved);
            Assert.Equal(425, layout.Width);
            Assert.Equal(357, layout.Height);
        }
        finally { group.Close(); }
    });

    private static GroupWindow CreateGroup(double x, double y)
    {
        var group = new GroupWindow(new CategoryDefinition { Id = "snap-test", Name = "Snap test" },
            new GroupLayout { X = x, Y = y, Width = 420, Height = 350 });
        group.SetPrimaryBounds(new Rect(0, 0, 1000, 900));
        return group;
    }

    private static void Start(Thumb thumb) =>
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });

    private static void Delta(Thumb thumb, double x, double y) =>
        thumb.RaiseEvent(new DragDeltaEventArgs(x, y) { RoutedEvent = Thumb.DragDeltaEvent });

    private static void Complete(Thumb thumb, double x, double y) =>
        thumb.RaiseEvent(new DragCompletedEventArgs(x, y, false) { RoutedEvent = Thumb.DragCompletedEvent });

    private static IEnumerable<Thumb> Thumbs(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Thumb thumb) yield return thumb;
            foreach (var descendant in Thumbs(child)) yield return descendant;
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
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
