using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CabiDock.Models;
using CabiDock.Views;

[assembly: InternalsVisibleTo("CabiDock.Tests")]

namespace CabiDock.Desktop;

internal interface IDesktopGroupHost : IDisposable
{
    bool IsAlive { get; }
    double DpiScale { get; }
    bool TrySetBounds(Rect physicalScreenPixels, out string reason);
    bool TryShow(Rect physicalScreenPixels, out string reason);
    bool TrySetOpacity(double opacity, out string reason);
    void Hide();
}

internal sealed record DesktopGroupAttachment(IDesktopGroupHost? Host, string Reason);

/// <summary>
/// Owns Explorer child windows. GroupWindow is an unshown interaction/layout model so WPF's
/// child-window coordinates cannot feed back into the saved, screen-relative group layout.
/// </summary>
public sealed class DesktopGroupPresenter : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<string, GroupEntry> _groups = new(StringComparer.Ordinal);
    private readonly Func<Window, DesktopProbeResult, DesktopGroupAttachment> _attach;
    private readonly Func<Rect> _getPrimaryWorkArea;
    private DesktopProbeResult? _desktop;
    private Rect _workArea;
    private IReadOnlyList<Rect> _reservedAreas = [];
    private bool _active, _updating, _positioning, _disposed;
    private string? _failure;

    public event Action<DesktopItem>? ItemOpenRequested;
    public event Action<DesktopItem, string>? ManualAssignmentRequested;
    public event Action<string, GroupLayout>? LayoutChanged;
    public event Action<string>? Failed;
    public event Action? RefreshRequested;

    public DesktopGroupPresenter() : this(Attach, GetPrimaryWorkArea) { }

    internal DesktopGroupPresenter(Func<Window, DesktopProbeResult, DesktopGroupAttachment> attach,
        Func<Rect> getPrimaryWorkArea)
    {
        _attach = attach;
        _getPrimaryWorkArea = getPrimaryWorkArea;
    }

    public bool IsAlive => !_disposed && _active && _groups.Values.All(group => group.Host.IsAlive);

    internal IEnumerable<string> RepresentedFilePaths => _groups.Values.SelectMany(group => group.Items)
        .Select(item => item.FullPath).Distinct(StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<string> PlannedFilePaths(CabiDockConfiguration config, CabiDockState state,
        IReadOnlyList<DesktopItem> items)
    {
        // Native menus pump messages. A file change must not replace their owner or claim a
        // newly added item is visible before that group's pending body can actually render.
        var busy = _groups.Where(pair => pair.Value.Model.IsInteractionActive).Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var categories = config.Categories.Select(category => category.Id).ToHashSet(StringComparer.Ordinal);
        var paths = items.Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return state.Items.Where(item => categories.Contains(item.CategoryId) && !busy.Contains(item.CategoryId)
                && paths.Contains(item.FullPath)).Select(item => item.FullPath)
            .Concat(_groups.Where(pair => busy.Contains(pair.Key)).SelectMany(pair => pair.Value.Items)
                .Select(item => item.FullPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Physical screen rectangles occupied by native items that must remain accessible.</summary>
    public void SetReservedAreas(IReadOnlyList<Rect> physicalScreenBounds)
    {
        _dispatcher.VerifyAccess();
        _reservedAreas = physicalScreenBounds.ToArray();
    }

    /// <summary>Preserves the HWNDs and interaction state while native icon geometry is unknown.</summary>
    public void SuspendVisibility()
    {
        _dispatcher.VerifyAccess();
        foreach (var entry in _groups.Values)
        {
            entry.Host.Hide();
            entry.Shown = false;
        }
    }

    public bool TryUpdate(CabiDockConfiguration config, CabiDockState state,
        IReadOnlyList<DesktopItem> items, DesktopProbeResult probe, out string reason, bool rebuild = false,
        bool show = true)
    {
        _dispatcher.VerifyAccess();
        reason = "";
        if (_disposed) { reason = "桌面群組已關閉。"; return false; }
        if (!probe.Available)
        {
            CloseGroups();
            reason = probe.Message;
            return false;
        }

        _updating = true;
        try
        {
            if (rebuild || _failure is not null || _groups.Values.Any(group => !group.Host.IsAlive)
                || _desktop is not null && (_desktop.ShellWindow != probe.ShellWindow
                    || _desktop.ViewWindow != probe.ViewWindow || _desktop.ShellProcessId != probe.ShellProcessId))
                CloseGroups();
            _failure = null;
            _desktop = probe;
            _workArea = _getPrimaryWorkArea();
            if (_workArea.IsEmpty || _workArea.Width <= 0 || _workArea.Height <= 0)
                throw new InvalidOperationException("無法取得主螢幕工作區域。");

            var byPath = items.GroupBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var membership = state.Items.Where(item => byPath.ContainsKey(item.FullPath))
                .GroupBy(item => item.CategoryId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(item => byPath[item.FullPath])
                    .DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.Ordinal);
            var populated = config.Categories.Where(category => membership.ContainsKey(category.Id)).ToList();
            var keep = populated.Select(category => category.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in _groups.Keys.Where(id => !keep.Contains(id) && !_groups[id].Model.IsInteractionActive).ToList())
                RemoveGroup(id);

            foreach (var category in populated)
            {
                if (_groups.TryGetValue(category.Id, out var busy) && busy.Model.IsInteractionActive) continue;
                if (_groups.TryGetValue(category.Id, out var old) && old.Name != category.Name) RemoveGroup(category.Id);
                if (!_groups.TryGetValue(category.Id, out var entry))
                {
                    var layout = state.Groups.GetValueOrDefault(category.Id) ?? new GroupLayout
                    {
                        X = 24 + _groups.Count % 6 * 154,
                        Y = 24 + _groups.Count / 6 * 176
                    };
                    entry = CreateGroup(category, layout, probe);
                    _groups.Add(category.Id, entry);
                    var bounds = LogicalWorkArea(entry.Host.DpiScale);
                    var normalized = NormalizeLayout(layout, bounds);
                    entry.Model.Left = normalized.X;
                    entry.Model.Top = normalized.Y;
                    entry.Layout = normalized;
                    state.Groups[category.Id] = normalized;
                    LayoutChanged?.Invoke(category.Id, normalized);
                }
                entry.Model.SetPrimaryBounds(LogicalWorkArea(entry.Host.DpiScale));
                var categoryChoices = config.Categories.Select(choice => (choice.Id, choice.Name)).ToArray();
                if (!SameItems(entry.Items, membership[category.Id]) || !entry.CategoryChoices.SequenceEqual(categoryChoices))
                {
                    entry.Items = membership[category.Id];
                    entry.CategoryChoices = categoryChoices;
                    entry.Model.UpdateItems(entry.Items, config.Categories);
                }
                if (!entry.Host.TrySetOpacity(config.GroupOpacity, out reason)) throw new InvalidOperationException(reason);
                if (!Sync(entry, out reason)) throw new InvalidOperationException(reason);
                state.Groups[category.Id] = entry.Layout;
            }

            // No new window becomes visible until every attachment and layout has succeeded.
            if (show && !TryShow(out reason)) throw new InvalidOperationException(reason);
            _active = true;
            reason = $"已在桌面顯示 {_groups.Count} 個分類群組。";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            CloseGroups();
            reason = $"桌面群組未啟用：{error.Message}";
            return false;
        }
        finally { _updating = false; }
    }

    public bool TryShow(out string reason)
    {
        _dispatcher.VerifyAccess();
        reason = "";
        if (_disposed) { reason = "桌面群組已關閉。"; return false; }
        foreach (var entry in _groups.Values.Where(entry => !entry.Shown))
        {
            if (!entry.Host.TryShow(BoundsFor(entry), out reason)) return false;
            entry.Shown = true;
        }
        return true;
    }

    private GroupEntry CreateGroup(CategoryDefinition category, GroupLayout saved, DesktopProbeResult probe)
    {
        var model = new GroupWindow(category, saved);
        var window = new Window
        {
            Title = $"CabiDock｜{category.Name}", WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Topmost = false, AllowsTransparency = false, Width = model.Width, Height = model.Height,
            Left = 0, Top = 0, Background = model.Background, FontFamily = model.FontFamily,
            FontSize = model.FontSize, Foreground = model.Foreground, Resources = model.Resources
        };
        model.Content = null;
        window.Content = model.CardContent;
        IDesktopGroupHost? host = null;
        try
        {
            var attachment = _attach(window, probe);
            host = attachment.Host;
            if (host is null) throw new InvalidOperationException(attachment.Reason);
            var entry = new GroupEntry(category.Name, model, window, host);
            void GeometryChanged(object? sender, EventArgs e)
            {
                if (_updating || _positioning || _disposed || _failure is not null) return;
                if (!Sync(entry, out var failure)) FailClosed(failure);
            }
            foreach (var property in new[] { Window.LeftProperty, Window.TopProperty, FrameworkElement.WidthProperty, FrameworkElement.HeightProperty })
            {
                var descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(Window));
                descriptor.AddValueChanged(model, GeometryChanged);
                entry.Unsubscribe.Add(() => descriptor.RemoveValueChanged(model, GeometryChanged));
            }
            model.ItemOpenRequested += item => ItemOpenRequested?.Invoke(item);
            model.InteractionEnded += () => _dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                if (!_disposed) RefreshRequested?.Invoke();
            });
            model.ManualAssignmentRequested += (item, target) => _dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                // Reclassification can remove the originating group. Wait until the drop/menu
                // handler has returned before allowing its native host to be destroyed.
                if (!_disposed) ManualAssignmentRequested?.Invoke(item, target);
            });
            model.LayoutChanged += layout =>
            {
                entry.Layout = layout;
                LayoutChanged?.Invoke(category.Id, layout);
            };
            model.Expanded += (_, _) =>
            {
                foreach (var other in _groups.Values.Where(other => other != entry)) other.Model.Collapse();
                if (!_updating && !Sync(entry, out var failure, force: true)) FailClosed(failure);
            };
            return entry;
        }
        catch
        {
            host?.Dispose();
            window.Close();
            model.Close();
            throw;
        }
    }

    private bool Sync(GroupEntry entry, out string reason, bool force = false)
    {
        reason = "";
        entry.Model.CardContent.Width = entry.Model.Width;
        entry.Model.CardContent.Height = entry.Model.Height;
        var requested = BoundsFor(entry);
        if (!TryPlace(requested, entry.Host.DpiScale, out var bounds))
        {
            reason = "主螢幕沒有足夠空間顯示群組並保留原生桌面項目。";
            return false;
        }
        var scale = entry.Host.DpiScale;
        if (Math.Abs(entry.Model.Left * scale - bounds.Left) > .01
            || Math.Abs(entry.Model.Top * scale - bounds.Top) > .01)
        {
            _positioning = true;
            try
            {
                entry.Model.Left = bounds.Left / scale;
                entry.Model.Top = bounds.Top / scale;
            }
            finally { _positioning = false; }
            entry.Layout = new GroupLayout
            {
                X = entry.Model.Left, Y = entry.Model.Top,
                Width = entry.Layout.Width, Height = entry.Layout.Height
            };
            LayoutChanged?.Invoke(entry.Model.CategoryId, entry.Layout);
        }
        if (!force && entry.LastBounds == bounds && entry.Host.IsAlive) return true;
        if (!entry.Host.TrySetBounds(bounds, out reason)) return false;
        entry.LastBounds = bounds;
        return true;
    }

    private bool TryPlace(Rect requested, double scale, out Rect placement)
    {
        placement = requested;
        var obstacles = _reservedAreas.Where(area => !area.IsEmpty && area.Width > 0 && area.Height > 0)
            .Select(area => { area.Inflate(8 * scale, 8 * scale); return area; })
            .Where(area => Overlaps(area, _workArea)).ToList();
        if (obstacles.All(area => !Overlaps(area, requested))) return true;

        var minX = _workArea.Left;
        var maxX = _workArea.Right - requested.Width;
        var minY = _workArea.Top;
        var maxY = _workArea.Bottom - requested.Height;
        var candidates = new[] { requested.Left, minX, maxX }
            .Concat(obstacles.SelectMany(area => new[] { area.Left - requested.Width, area.Right }))
            .Where(x => x >= minX && x <= maxX).Distinct();
        var bestDistance = double.PositiveInfinity;
        foreach (var x in candidates)
        {
            var horizontalDistance = (x - requested.Left) * (x - requested.Left);
            if (horizontalDistance >= bestDistance) continue;
            // For a fixed X, obstacle tops/bottoms form forbidden intervals for the group's Y.
            // Merging them finds the nearest free Y without a quadratic grid of candidate pairs.
            var intervals = obstacles.Where(area => x < area.Right && x + requested.Width > area.Left)
                .Select(area => (Start: area.Top - requested.Height, End: area.Bottom))
                .OrderBy(interval => interval.Start).ToList();
            var merged = new List<(double Start, double End)>();
            foreach (var interval in intervals)
            {
                if (merged.Count > 0 && interval.Start < merged[^1].End)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
                else merged.Add(interval);
            }
            var blocked = merged.FindIndex(interval => requested.Top > interval.Start && requested.Top < interval.End);
            var ys = blocked < 0 ? new[] { requested.Top } : new[] { merged[blocked].Start, merged[blocked].End };
            foreach (var y in ys.Where(y => y >= minY && y <= maxY))
            {
                var distance = horizontalDistance + (y - requested.Top) * (y - requested.Top);
                if (distance >= bestDistance) continue;
                placement = new Rect(x, y, requested.Width, requested.Height);
                bestDistance = distance;
            }
        }
        return double.IsFinite(bestDistance);
    }

    private static bool Overlaps(Rect first, Rect second) => first.Left < second.Right
        && first.Right > second.Left && first.Top < second.Bottom && first.Bottom > second.Top;

    private Rect BoundsFor(GroupEntry entry)
    {
        var scale = entry.Host.DpiScale;
        var width = Math.Min(_workArea.Width, entry.Model.Width * scale);
        var height = Math.Min(_workArea.Height, entry.Model.Height * scale);
        return new Rect(Math.Clamp(entry.Model.Left * scale, _workArea.Left, _workArea.Right - width),
            Math.Clamp(entry.Model.Top * scale, _workArea.Top, _workArea.Bottom - height), width, height);
    }

    private Rect LogicalWorkArea(double scale) => new(_workArea.X / scale, _workArea.Y / scale,
        _workArea.Width / scale, _workArea.Height / scale);

    private static GroupLayout NormalizeLayout(GroupLayout layout, Rect bounds) => new()
    {
        X = Math.Clamp(double.IsFinite(layout.X) ? layout.X : bounds.Left + 24, bounds.Left, Math.Max(bounds.Left, bounds.Right - 138)),
        Y = Math.Clamp(double.IsFinite(layout.Y) ? layout.Y : bounds.Top + 24, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - 160)),
        Width = Math.Clamp(double.IsFinite(layout.Width) ? layout.Width : 360, Math.Min(260, bounds.Width), bounds.Width),
        Height = Math.Clamp(double.IsFinite(layout.Height) ? layout.Height : 320, Math.Min(210, bounds.Height), bounds.Height)
    };

    private static bool SameItems(IReadOnlyList<DesktopItem> first, IReadOnlyList<DesktopItem> second) =>
        first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First.FullPath == pair.Second.FullPath && pair.First.IsDirectory == pair.Second.IsDirectory
            && pair.First.Identity == pair.Second.Identity);

    private void FailClosed(string reason)
    {
        if (_disposed || _failure is not null) return;
        _failure = reason;
        _active = false;
        foreach (var entry in _groups.Values) entry.Host.Hide();
        Failed?.Invoke(reason);
    }

    private void RemoveGroup(string id)
    {
        if (_groups.Remove(id, out var entry)) entry.Dispose();
    }

    private void CloseGroups()
    {
        _active = false;
        foreach (var entry in _groups.Values) entry.Host.Hide();
        foreach (var entry in _groups.Values) entry.Dispose();
        _groups.Clear();
        _desktop = null;
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        CloseGroups();
    }

    private static DesktopGroupAttachment Attach(Window window, DesktopProbeResult probe) =>
        DesktopWindowHost.TryAttach(window, probe, out var host, out var reason) ? new(host, reason) : new(null, reason);

    private static Rect GetPrimaryWorkArea()
    {
        var monitor = MonitorFromPoint(new NativeDesktop.Point(), 1); // MONITOR_DEFAULTTOPRIMARY
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("無法讀取主螢幕工作區域。");
        return new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeDesktop.Rectangle Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativeDesktop.Point point, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    private sealed class GroupEntry(string name, GroupWindow model, Window window, IDesktopGroupHost host) : IDisposable
    {
        public string Name { get; } = name;
        public GroupWindow Model { get; } = model;
        public IDesktopGroupHost Host { get; } = host;
        public List<DesktopItem> Items { get; set; } = [];
        public (string Id, string Name)[] CategoryChoices { get; set; } = [];
        public GroupLayout Layout { get; set; } = new();
        public List<Action> Unsubscribe { get; } = [];
        public Rect? LastBounds { get; set; }
        public bool Shown { get; set; }

        public void Dispose()
        {
            foreach (var unsubscribe in Unsubscribe) unsubscribe();
            Unsubscribe.Clear();
            Host.Dispose();
            window.Content = null;
            window.Close();
            Model.Close();
        }
    }
}
