using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CabiDock.Desktop;
using CabiDock.Models;
using CabiDock.Services;
using Xunit;

namespace CabiDock.Tests;

// WPF property descriptors share a process-wide cache with the preview's layout bindings.
[Collection("WPF layout bindings")]
public sealed class DesktopGroupPresenterTests
{
    private static readonly DesktopProbeResult Desktop = new()
    {
        Available = true, ShellWindow = 10, ViewWindow = 20, ShellProcessId = 30
    };

    [Fact]
    public Task KeepsWindowsAcrossRefreshAndConvertsDraggingToPhysicalScreenBounds() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 60, 1200, 840), 1.5);
        var (config, state, items) = Data();
        var layouts = new Dictionary<string, GroupLayout>();
        presenter.LayoutChanged += (category, layout) => layouts[category] = layout;
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out var reason), reason);
        Assert.True(presenter.IsAlive);
        Assert.Equal(2, hosts.Count);
        Assert.All(hosts, host =>
        {
            Assert.False(host.Window.IsVisible); // Fake host never shows an HWND or touches Explorer.
            Assert.Equal(207, host.Bounds.Width);
            Assert.Equal(240, host.Bounds.Height);
            Assert.InRange(host.Bounds.Top, 60, 660);
        });
        Assert.Equal(2, state.Groups.Count);
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out reason), reason);
        Assert.Equal(2, hosts.Count);
        Assert.All(hosts, host => Assert.Equal(2, host.ShowCount)); // Reconfirm presentation after each verified refresh.

        var first = hosts[0];
        var header = Descendants<Thumb>((DependencyObject)first.Window.Content).First();
        var oldX = first.Bounds.X;
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragDeltaEventArgs(20, 10) { RoutedEvent = Thumb.DragDeltaEvent });
        header.RaiseEvent(new DragCompletedEventArgs(20, 10, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(oldX + 30, first.Bounds.X);
        Assert.Equal(first.Bounds.X / 1.5, layouts["documents"].X);

        header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        // A new click resets the header movement flag and expands the card.
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(540, first.Bounds.Width);
        Assert.Equal(480, first.Bounds.Height);

        var secondHeader = Descendants<Thumb>((DependencyObject)hosts[1].Window.Content).First();
        secondHeader.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        secondHeader.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(207, first.Bounds.Width);
        Assert.Equal(540, hosts[1].Bounds.Width);
    });

    [Fact]
    public Task WatcherRenameBeforeRescanCannotEmptyOrRecreateTheAttachedGroup() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        var snapshot = DesktopTakeoverService.SnapshotClassification(state);
        Assert.True(presenter.TryUpdate(config, snapshot, items, Desktop, out _));
        var oldPath = items[0].FullPath;
        var renamed = new DesktopItem(@"C:\CabiDock-tests\renamed.md", false);
        new ClassificationService().Rename(oldPath, renamed, state);
        Assert.Contains(snapshot.Items, item => item.FullPath == oldPath);
        Assert.DoesNotContain(state.Items, item => item.FullPath == oldPath);
        Assert.True(presenter.TryUpdate(config, snapshot, items, Desktop, out var reason), reason);
        Assert.Equal(2, hosts.Count);
        Assert.All(hosts, host => Assert.False(host.Disposed));
        Assert.Same(state.Groups, snapshot.Groups); // Layout changes still persist immediately.

        items[0] = renamed;
        new ClassificationService().Reconcile(items, state, config);
        Assert.True(presenter.TryUpdate(config, DesktopTakeoverService.SnapshotClassification(state), items,
            Desktop, out reason), reason);
        Assert.Equal(2, hosts.Count);
        Assert.Contains(renamed.FullPath, presenter.RepresentedFilePaths);
    });

    [Fact]
    public Task FileChangesAndTemporaryGeometrySuspensionPreserveExpandedGroupHandles() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out _));
        var first = hosts[0];
        var header = Descendants<Thumb>((DependencyObject)first.Window.Content).First();
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(360, first.Bounds.Width);

        items[0] = new DesktopItem(@"C:\CabiDock-tests\renamed.md", false);
        items.Add(new DesktopItem(@"C:\CabiDock-tests\new.md", false));
        new ClassificationService().Reconcile(items, state, config);
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out var reason), reason);
        Assert.Equal(2, hosts.Count);
        Assert.False(first.Disposed);
        Assert.Equal(360, first.Bounds.Width);
        Assert.Equal(2, first.ShowCount);
        Assert.Contains(items[0].FullPath, presenter.RepresentedFilePaths);

        presenter.SuspendVisibility();
        Assert.True(presenter.IsAlive);
        Assert.All(hosts, host => Assert.True(host.Hidden));
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out reason, show: false), reason);
        Assert.Equal(2, first.ShowCount);
        Assert.True(presenter.TryShow(out reason), reason);
        Assert.Equal(3, first.ShowCount);
        Assert.False(first.Hidden);
        Assert.Equal(360, first.Bounds.Width);
        Assert.Equal(2, hosts.Count);
    });

    [Fact]
    public Task VerifiedRefreshRestoresExternallyHiddenGroupsWithoutRecreatingThem() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out var reason), reason);
        var first = hosts[0];
        var header = Descendants<Thumb>((DependencyObject)first.Window.Content).First();
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.Equal(360, first.Bounds.Width);

        // Explorer can change native presentation while the presenter still considers it shown.
        foreach (var host in hosts) host.Hide();
        Assert.True(presenter.IsAlive);
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out reason, show: false), reason);
        Assert.All(hosts, host => { Assert.True(host.Hidden); Assert.Equal(1, host.ShowCount); });

        // The service calls TryShow only after it has verified and applied current icon geometry.
        Assert.True(presenter.TryShow(out reason), reason);
        Assert.Equal(2, hosts.Count);
        Assert.All(hosts, host =>
        {
            Assert.False(host.Hidden);
            Assert.False(host.Disposed);
            Assert.Equal(2, host.ShowCount);
        });
        Assert.Equal(360, first.Bounds.Width);
    });

    [Fact]
    public Task OpenMenuRetainsItsOwnerAndDoesNotClaimUnrenderedRenamedItems() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out _));
        var first = hosts[0];
        var card = (FrameworkElement)first.Window.Content;
        var header = Descendants<Thumb>(card).First();
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        card.Measure(new Size(360, 320));
        card.Arrange(new Rect(0, 0, 360, 320));
        card.UpdateLayout();
        var menu = Descendants<FrameworkElement>(card).First(element => element.ContextMenu is not null).ContextMenu;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        var oldPath = items[0].FullPath;
        items[0] = new DesktopItem(@"C:\CabiDock-tests\renamed.md", false);
        new ClassificationService().Reconcile(items, state, config);
        var planned = presenter.PlannedFilePaths(config, state, items).ToList();
        Assert.Contains(oldPath, planned);
        Assert.DoesNotContain(items[0].FullPath, planned);
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out var reason), reason);
        Assert.Contains(oldPath, presenter.RepresentedFilePaths);
        Assert.DoesNotContain(items[0].FullPath, presenter.RepresentedFilePaths);

        // Even deleting the group's last item must leave the active native menu owner alive.
        Assert.True(presenter.TryUpdate(config, new CabiDockState(), [], Desktop, out reason), reason);
        Assert.False(first.Disposed);
        var refreshes = 0;
        presenter.RefreshRequested += () => refreshes++;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(refreshes > 0);
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out reason), reason);
        Assert.False(first.Disposed);
        Assert.Equal(360, first.Bounds.Width);
        Assert.DoesNotContain(oldPath, presenter.RepresentedFilePaths);
        Assert.Contains(items[0].FullPath, presenter.RepresentedFilePaths);
    });

    [Fact]
    public Task FailedAttachmentClosesAllBeforeAnyNewGroupIsShown() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = new DesktopGroupPresenter((window, _) =>
        {
            if (hosts.Count == 1) return new(null, "injected attach failure");
            var host = new FakeHost(window, 1);
            hosts.Add(host);
            return new(host, "");
        }, () => new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        Assert.False(presenter.TryUpdate(config, state, items, Desktop, out var reason));
        Assert.Contains("injected attach failure", reason);
        Assert.False(presenter.IsAlive);
        Assert.True(hosts[0].Disposed);
        Assert.Equal(0, hosts[0].ShowCount);
    });

    [Fact]
    public Task DeadExplorerChildrenAreRecreatedAndEmptyDesktopRemainsHealthy() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out _));
        hosts[0].Alive = false;
        hosts[0].Window.Close(); // Explorer can destroy a child HWND before the next poll.
        Assert.False(presenter.IsAlive);
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out var reason), reason);
        Assert.Equal(4, hosts.Count);
        Assert.All(hosts.Take(2), host => Assert.True(host.Disposed));
        Assert.True(presenter.IsAlive);
        Assert.True(presenter.TryUpdate(config, new CabiDockState(), [], Desktop, out reason), reason);
        Assert.All(hosts, host => Assert.True(host.Disposed));
        Assert.True(presenter.IsAlive);
        Assert.False(presenter.TryUpdate(config, state, items, new DesktopProbeResult(), out _));
        Assert.False(presenter.IsAlive);
    });

    [Fact]
    public Task BoundsFailureDuringInteractionHidesEveryGroup() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out _));
        hosts[0].FailBounds = true;
        var failures = new List<string>();
        presenter.Failed += reason =>
        {
            Assert.All(hosts, host => Assert.True(host.Hidden));
            failures.Add(reason);
        };
        var header = Descendants<Thumb>((DependencyObject)hosts[0].Window.Content).First();
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragDeltaEventArgs(10, 10) { RoutedEvent = Thumb.DragDeltaEvent });
        Assert.False(presenter.IsAlive);
        Assert.All(hosts, host => Assert.True(host.Hidden));
        Assert.Single(failures);
        header.RaiseEvent(new DragCompletedEventArgs(10, 10, false) { RoutedEvent = Thumb.DragCompletedEvent });
    });

    [Fact]
    public Task DefaultGroupsAvoidRecycleBinAndPublishRelocatedLayout() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1200, 840), 1.5);
        var recycleBin = new Rect(0, 0, 120, 150);
        presenter.SetReservedAreas([recycleBin]);
        var (config, state, items) = Data();
        var layouts = new Dictionary<string, GroupLayout>();
        presenter.LayoutChanged += (category, layout) => layouts[category] = layout;
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out var reason), reason);
        Assert.All(hosts, host => Assert.False(Overlaps(host.Bounds, recycleBin)));
        Assert.Equal(132, hosts[0].Bounds.Left); // The nearest edge includes the 8-DIP margin.
        Assert.Equal(88, layouts["documents"].X);
        Assert.Equal(88, state.Groups["documents"].X);

        var header = Descendants<Thumb>((DependencyObject)hosts[0].Window.Content).First();
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragDeltaEventArgs(-88, -24) { RoutedEvent = Thumb.DragDeltaEvent });
        header.RaiseEvent(new DragCompletedEventArgs(-88, -24, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.False(Overlaps(hosts[0].Bounds, recycleBin));
    });

    [Fact]
    public Task SavedGroupRelocatesOnExpansionToKeepNativeItemAccessible() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 800, 600));
        var nativeItem = new Rect(400, 300, 100, 100);
        presenter.SetReservedAreas([nativeItem]);
        var (config, state, items) = Data();
        state.Groups["documents"] = new GroupLayout { X = 650, Y = 430, Width = 360, Height = 320 };
        var lastLayout = new GroupLayout();
        presenter.LayoutChanged += (category, layout) => { if (category == "documents") lastLayout = layout; };
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out var reason), reason);
        Assert.Equal(650, hosts[0].Bounds.X);
        var header = Descendants<Thumb>((DependencyObject)hosts[0].Window.Content).First();
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        Assert.True(presenter.IsAlive);
        Assert.Equal(360, hosts[0].Bounds.Width);
        Assert.False(Overlaps(hosts[0].Bounds, nativeItem));
        Assert.Equal(32, hosts[0].Bounds.Left);
        Assert.Equal(32, lastLayout.X);
        Assert.Equal(360, lastLayout.Width);
        Assert.Equal(320, lastLayout.Height);
    });

    [Fact]
    public Task NoPlacementSpaceFailsClosedBeforeShowingAnyGroup() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 800, 600));
        presenter.SetReservedAreas([new Rect(0, 0, 800, 600)]);
        var (config, state, items) = Data();
        Assert.False(presenter.TryUpdate(config, state, items, Desktop, out var reason));
        Assert.Contains("沒有足夠空間", reason);
        Assert.False(presenter.IsAlive);
        Assert.All(hosts, host => { Assert.True(host.Disposed); Assert.Equal(0, host.ShowCount); });
    });

    [Fact]
    public Task ManualAssignmentIsDeferredUntilTheOriginatingMenuHandlerReturns() => OnSta(() =>
    {
        var hosts = new List<FakeHost>();
        using var presenter = CreatePresenter(hosts, new Rect(0, 0, 1920, 1040));
        var (config, state, items) = Data();
        Assert.True(presenter.TryUpdate(config, state, items, Desktop, out _));
        var card = (FrameworkElement)hosts[0].Window.Content;
        var header = Descendants<Thumb>(card).First();
        header.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        header.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        card.Measure(new Size(360, 320));
        card.Arrange(new Rect(0, 0, 360, 320));
        card.UpdateLayout();
        var menu = Descendants<FrameworkElement>(card).First(element => element.ContextMenu is not null).ContextMenu;
        var assignment = ((MenuItem)menu.Items[1]).Items.Cast<MenuItem>().First(choice => !choice.IsChecked);
        var assignments = 0;
        presenter.ManualAssignmentRequested += (_, _) => assignments++;

        assignment.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(0, assignments);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, assignments);

        assignment.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        presenter.Dispose();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, assignments); // A closed presenter cannot dispatch a queued assignment.
    });

    [Fact]
    public Task NativeHostShowsOnlyAsChildAndSurvivesItsHiddenTestParentBeingDestroyed() => OnSta(() =>
    {
        // The parent is our own hidden HWND. The real Explorer HWND is only read for identity;
        // this test never reparents into Explorer or changes a desktop window.
        var parent = new Window { Width = 800, Height = 600, ShowInTaskbar = false };
        var child = new Window
        {
            Width = 138, Height = 160, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            ShowActivated = false, ResizeMode = ResizeMode.NoResize
        };
        DesktopWindowHost? host = null;
        try
        {
            var parentHandle = new WindowInteropHelper(parent).EnsureHandle();
            NativeDesktop.GetWindowThreadProcessId(parentHandle, out var process);
            var probe = new DesktopProbeResult
            {
                Available = true, ShellWindow = NativeDesktop.GetShellWindow(),
                ViewWindow = parentHandle, ShellProcessId = process
            };
            Assert.True(DesktopWindowHost.TryAttach(child, probe, out host, out var reason), reason);
            Assert.NotNull(host);
            var childHandle = new WindowInteropHelper(child).Handle;
            Assert.False(child.IsVisible);
            Assert.Equal(parentHandle, NativeDesktop.GetParent(childHandle));
            Assert.True(host.TryShow(new Rect(50, 70, 138, 160), out reason), reason);
            Assert.False(parent.IsVisible);
            Assert.Equal(parentHandle, NativeDesktop.GetParent(childHandle));
            var style = NativeDesktop.ReadStyle(childHandle, -16).ToInt64();
            Assert.NotEqual(0, style & 0x40000000L); // WS_CHILD remains set after WPF Show.
            Assert.Equal(0, style & 0x80000000L); // WS_POPUP was not restored by WPF.
            Assert.True(NativeDesktop.GetWindowRect(childHandle, out var bounds));
            Assert.Equal(50, bounds.Left);
            Assert.Equal(70, bounds.Top);
            Assert.Equal(138, bounds.Right - bounds.Left);
            host.Hide();
            Assert.Equal(0, NativeDesktop.ReadStyle(childHandle, -16).ToInt64() & 0x10000000L); // WS_VISIBLE
            Assert.True(host.IsAlive);
            Assert.True(host.TryShow(new Rect(50, 70, 138, 160), out reason), reason);
            Assert.NotEqual(0, NativeDesktop.ReadStyle(childHandle, -16).ToInt64() & 0x10000000L);
            Assert.False(parent.IsVisible); // Native visibility recovery must not show the test parent.
            parent.Close();
            Assert.False(host.IsAlive);
            Assert.False(host.TrySetBounds(new Rect(0, 0, 138, 160), out _));
        }
        finally { host?.Dispose(); child.Close(); parent.Close(); }
    });

    [Fact]
    public Task NativeHostReturnsAboveSiblingAfterExplorerStyleReordering() => OnSta(() =>
    {
        // Both children and the hidden parent belong to this test; no Explorer HWND is changed.
        var parent = new Window { Width = 800, Height = 600, ShowInTaskbar = false };
        var child = new Window
        {
            Width = 138, Height = 160, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            ShowActivated = false, ResizeMode = ResizeMode.NoResize
        };
        var sibling = new Window
        {
            Width = 138, Height = 160, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            ShowActivated = false, ResizeMode = ResizeMode.NoResize
        };
        DesktopWindowHost? childHost = null;
        DesktopWindowHost? siblingHost = null;
        try
        {
            var parentHandle = new WindowInteropHelper(parent).EnsureHandle();
            NativeDesktop.GetWindowThreadProcessId(parentHandle, out var process);
            var probe = new DesktopProbeResult
            {
                Available = true, ShellWindow = NativeDesktop.GetShellWindow(),
                ViewWindow = parentHandle, ShellProcessId = process
            };
            Assert.True(DesktopWindowHost.TryAttach(child, probe, out childHost, out var reason), reason);
            Assert.True(DesktopWindowHost.TryAttach(sibling, probe, out siblingHost, out reason), reason);
            var bounds = new Rect(50, 70, 138, 160);
            Assert.True(childHost!.TryShow(bounds, out reason), reason);
            Assert.True(siblingHost!.TryShow(bounds, out reason), reason);
            var childHandle = new WindowInteropHelper(child).Handle;
            var siblingHandle = new WindowInteropHelper(sibling).Handle;
            Assert.Equal(siblingHandle, GetWindow(childHandle, 3)); // GW_HWNDPREV: sibling is above our group.

            Assert.True(childHost.TryShow(bounds, out reason), reason);
            Assert.Equal(nint.Zero, GetWindow(childHandle, 3));
            Assert.Equal(childHandle, GetWindow(siblingHandle, 3));
            Assert.Equal(parentHandle, NativeDesktop.GetParent(childHandle));
            Assert.False(parent.IsVisible);
        }
        finally
        {
            siblingHost?.Dispose();
            childHost?.Dispose();
            sibling.Close();
            child.Close();
            parent.Close();
        }
    });

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    private static DesktopGroupPresenter CreatePresenter(List<FakeHost> hosts, Rect bounds, double scale = 1) =>
        new((window, _) =>
        {
            var host = new FakeHost(window, scale);
            hosts.Add(host);
            return new(host, "");
        }, () => bounds);

    private static (CabiDockConfiguration Config, CabiDockState State, List<DesktopItem> Items) Data()
    {
        var config = ConfigurationService.LoadDefaults();
        // Category order is explicit so the first fake host always represents documents.
        config.Categories = config.Categories.OrderBy(category => category.Id == "documents" ? 0 : 1).ToList();
        var state = new CabiDockState();
        var items = new List<DesktopItem> { new(@"C:\CabiDock-tests\notes.md", false), new(@"C:\CabiDock-tests\photo.png", false) };
        new ClassificationService().Reconcile(items, state, config);
        return (config, state, items);
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

    private static bool Overlaps(Rect first, Rect second) => first.Left < second.Right
        && first.Right > second.Left && first.Top < second.Bottom && first.Bottom > second.Top;

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

    private sealed class FakeHost(Window window, double scale) : IDesktopGroupHost
    {
        public Window Window { get; } = window;
        public double DpiScale => scale;
        public bool Alive { get; set; } = true;
        public bool Disposed { get; private set; }
        public bool Hidden { get; private set; }
        public bool FailBounds { get; set; }
        public bool IsAlive => Alive && !Disposed;
        public int ShowCount { get; private set; }
        public Rect Bounds { get; private set; }
        public bool TrySetBounds(Rect bounds, out string reason)
        {
            reason = FailBounds ? "injected bounds failure" : "";
            if (FailBounds || !IsAlive) return false;
            Bounds = bounds;
            return true;
        }
        public bool TryShow(Rect bounds, out string reason)
        {
            ShowCount++;
            Hidden = false;
            return TrySetBounds(bounds, out reason);
        }
        public bool TrySetOpacity(double opacity, out string reason) { reason = ""; return IsAlive; }
        public void Hide() => Hidden = true;
        public void Dispose() { Hidden = true; Disposed = true; }
    }
}
