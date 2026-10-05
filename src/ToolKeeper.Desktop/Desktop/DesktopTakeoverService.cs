using System.Runtime.InteropServices;
using System.Windows.Threading;
using CabiDock.Models;

namespace CabiDock.Desktop;

/// <summary>Keeps desktop groups attached while reconciling verified native icon snapshots.</summary>
internal sealed class DesktopTakeoverService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _geometryTimer;
    private DesktopGroupPresenter _groups = new();
    private readonly WinEventCallback _eventCallback;
    private readonly Mutex _lease = new(false, @"Local\CabiDock.DesktopTakeover");
    private CabiDockConfiguration? _configuration;
    private CabiDockState? _state;
    private IReadOnlyList<DesktopItem> _items = [];
    private IReadOnlyList<DesktopTool> _tools = [];
    private Action<string>? _activateTool;
    private string _theme = "Light";
    private DesktopRecoveryGuard? _guard;
    private DesktopIconClipper? _clipper;
    private DesktopProbeResult? _desktop;
    private nint _eventHook;
    private bool _ownsLease, _disposed, _refreshing, _rebuild, _hasSnapshot, _refreshQueued, _geometrySuspended;
    private DateTime _retryAfter;
    private DateTime? _geometryUnstableSince;
    public bool Enabled { get; private set; }
    public bool IsActive { get; private set; }
    public string Status { get; private set; } = "正在準備桌面接管。";
    public event Action? StatusChanged;
    public event Action<DesktopItem>? ItemOpenRequested;
    public event Action<DesktopItem, string>? ManualAssignmentRequested;
    public event Action<string, GroupLayout>? LayoutChanged;

    public DesktopTakeoverService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), dispatcher);
        _timer.Stop();
        _geometryTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background,
            (_, _) => Refresh(), dispatcher);
        _geometryTimer.Stop();
        BindGroups();
        _eventCallback = (_, eventId, window, objectId, childId, _, _) =>
        {
            if (_disposed || _clipper is null || window != _clipper.Window || objectId != -4) return;
            var session = _clipper;
            // A burst of Explorer notifications needs one fresh mapping, not a new session.
            // Window-level region changes are ignored because our own clipping raises those.
            if (eventId is 0x8000 or 0x8001 or 0x8004 || (childId > 0 && eventId is 0x800B or 0x800C))
                dispatcher.BeginInvoke(() =>
                {
                    if (_disposed || !ReferenceEquals(_clipper, session)) return;
                    QueueRefresh();
                }, DispatcherPriority.Send);
        };
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || _disposed) return;
        _refreshQueued = true;
        _timer.Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, DispatcherPriority.Send);
    }

    private void BindGroups()
    {
        _groups.SetTheme(_theme);
        _groups.SetTools(_tools, id => _activateTool?.Invoke(id));
        var presenter = _groups;
        _groups.ItemOpenRequested += item => ItemOpenRequested?.Invoke(item);
        _groups.ManualAssignmentRequested += (item, category) => ManualAssignmentRequested?.Invoke(item, category);
        _groups.LayoutChanged += (category, layout) => LayoutChanged?.Invoke(category, layout);
        _groups.RefreshRequested += QueueRefresh;
        _groups.Failed += reason => _timer.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || !ReferenceEquals(_groups, presenter)) return;
            StopSession();
            SetStatus(false, reason);
        }, DispatcherPriority.Send);
    }

    public void SetTheme(string theme)
    {
        if (_disposed) return;
        _theme = theme;
        _groups.SetTheme(theme);
    }

    public void SetTools(IReadOnlyList<DesktopTool> tools, Action<string> activateTool)
    {
        _tools = tools;
        _activateTool = activateTool;
        _groups.SetTools(tools, activateTool);
        if (Enabled) Refresh();
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed) return;
        Enabled = enabled;
        _retryAfter = DateTime.MinValue;
        if (!enabled)
        {
            _timer.Stop();
            StopSession();
            SetStatus(false, "桌面接管已暫停，原生圖示已恢復。");
        }
        else { _timer.Start(); Refresh(); }
    }

    public void Update(CabiDockConfiguration configuration, CabiDockState state, IReadOnlyList<DesktopItem> items, bool rebuild = false)
    {
        _configuration = configuration;
        _state = SnapshotClassification(state);
        _items = items;
        _hasSnapshot = true;
        _rebuild |= rebuild;
        if (!Enabled || _disposed) return;
        _retryAfter = DateTime.MinValue;
        _timer.Start();
        Refresh();
    }

    internal static CabiDockState SnapshotClassification(CabiDockState state) => new()
    {
        // Watcher rename/delete records change before the asynchronous scan finishes. Keep
        // membership paired with _items until Update publishes the next complete snapshot.
        Items = state.Items.Select(item => new ClassifiedItem
        {
            FullPath = item.FullPath, Identity = item.Identity, CategoryId = item.CategoryId, Source = item.Source
        }).ToList(),
        Groups = state.Groups,
        ConfigurationFingerprint = state.ConfigurationFingerprint
    };

    public void Suspend(string reason)
    {
        if (_disposed) return;
        _hasSnapshot = false;
        if (!Enabled) return;
        StopSession();
        SetStatus(false, reason);
    }

    private void Refresh()
    {
        if (_disposed || !Enabled || !_hasSnapshot || _configuration is null || _state is null || _refreshing) return;
        if (_clipper is null && DateTime.UtcNow < _retryAfter) return;
        _refreshing = true;
        try
        {
            if (!_ownsLease)
            {
                try { _ownsLease = _lease.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsLease = true; }
                if (!_ownsLease) { SetStatus(false, "另一個 CabiDock 正在接管桌面。"); return; }
            }
            var probe = DesktopProbe.Capture();
            if (_clipper is not null && (!IsCurrentSession() || probe.Available && !SameDesktop(probe)))
                StopSession(releaseLease: false);
            if (!DesktopIconClipper.TryCapture(probe,
                RepresentedPaths(probe, _groups.PlannedFilePaths(_configuration, _state, _items)),
                out var window, out var plan, out var reason))
            {
                WaitForGeometry(probe.Available ? reason : probe.Message);
                return;
            }
            if (_clipper is not null && window != _clipper.Window)
                StopSession(releaseLease: false);
            if (_clipper is null)
            {
                if (!DesktopRecoveryGuard.TryStart(window, out _guard, out reason))
                {
                    StopSession();
                    SetStatus(false, reason);
                    return;
                }
                _clipper = new DesktopIconClipper(window, probe.ShellProcessId);
                _desktop = probe;
                _eventHook = SetWinEventHook(0x8000, 0x800C, 0, _eventCallback, probe.ShellProcessId, 0, 0);
                if (_eventHook == 0) throw new InvalidOperationException("無法監看原生圖示配置。");
            }
            var reservedAreas = plan!.PreservedScreenBounds;
            _groups.SetReservedAreas(reservedAreas);
            if (!_groups.TryUpdate(_configuration, _state, _items, probe, out reason, _rebuild, show: false))
            {
                StopSession();
                SetStatus(false, reason);
                return;
            }
            // Guard startup and WPF attachment may take time. Never apply the geometry read
            // before those operations: Explorer can have rearranged its icons meanwhile.
            var current = DesktopProbe.Capture();
            if (!IsCurrentSession() || current.Available && !SameDesktop(current))
            {
                StopSession();
                SetStatus(false, "桌面或恢復程序已變更，已恢復原生圖示。");
                return;
            }
            if (!DesktopIconClipper.TryCapture(current, RepresentedPaths(current, _groups.RepresentedFilePaths),
                out var currentWindow, out plan, out reason)
                || currentWindow != window || !reservedAreas.SequenceEqual(plan!.PreservedScreenBounds))
            {
                WaitForGeometry(string.IsNullOrEmpty(reason) ? "原生圖示正在重新排列。" : reason);
                return;
            }
            if (!_clipper.TryApply(plan!, out reason) || !_groups.TryShow(out reason))
            {
                StopSession();
                SetStatus(false, reason);
                return;
            }
            _geometryTimer.Stop();
            _geometryUnstableSince = null;
            _geometrySuspended = false;
            _rebuild = false;
            SetStatus(true, $"桌面接管（實驗）· {plan!.ManagedItemCount} 個檔案圖示已收進群組 · 系統圖示保留");
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StopSession();
            SetStatus(false, "桌面接管暫時無法使用，已恢復原生桌面。\n" + error.Message);
        }
        finally { _refreshing = false; }
    }

    private bool SameDesktop(DesktopProbeResult probe) => probe.Available && _desktop is not null
        && probe.ShellWindow == _desktop.ShellWindow && probe.ViewWindow == _desktop.ViewWindow
        && probe.ShellProcessId == _desktop.ShellProcessId;

    private bool IsCurrentSession() => _desktop is not null && _clipper?.IsAlive == true
        && NativeDesktop.GetShellWindow() == _desktop.ShellWindow
        && NativeDesktop.GetParent(_clipper.Window) == _desktop.ViewWindow
        && NativeDesktop.IsWindow(_desktop.ViewWindow) && _guard?.IsAlive == true && _groups.IsAlive;

    private static IEnumerable<string> RepresentedPaths(DesktopProbeResult probe, IEnumerable<string> representedPaths)
    {
        // FileSystemWatcher and Explorer do not publish atomically. A removed/renamed file may
        // still be in the current group; a newly created file stays native until our rescan adds it.
        // Only suppress paths present in BOTH snapshots, never infer identity from an icon index.
        var nativePaths = probe.Items.Where(item => item.IsFileSystem && item.FileSystemPath is not null)
            .Select(item => item.FileSystemPath!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return representedPaths.Where(nativePaths.Contains);
    }

    private void WaitForGeometry(string reason)
    {
        var now = DateTime.UtcNow;
        _geometryUnstableSince ??= now;
        if (_clipper is null || !_clipper.IsAlive || _guard?.IsAlive != true || !_groups.IsAlive
            || now - _geometryUnstableSince.Value >= TimeSpan.FromSeconds(2))
        {
            StopSession();
            SetStatus(false, reason);
            return;
        }
        // Unknown geometry must never leave stale holes over a newly moved system icon.
        // Retain the same hidden HWNDs (including expansion state), but restore the native
        // region until a complete mapping can place them safely again.
        _groups.SuspendVisibility();
        if (!_geometrySuspended)
        {
            if (!_clipper.TryRestore())
            {
                StopSession();
                SetStatus(false, "無法恢復原生圖示，已停止桌面接管。");
                return;
            }
            _geometrySuspended = true;
        }
        SetStatus(false, "正在同步桌面項目，分類視窗配置保留。");
        _geometryTimer.Start();
    }

    private void StopSession(bool releaseLease = true)
    {
        _geometryTimer.Stop();
        _geometryUnstableSince = null;
        _geometrySuspended = false;
        _desktop = null;
        if (_eventHook != 0) { UnhookWinEvent(_eventHook); _eventHook = 0; }
        try { _groups.Dispose(); }
        finally
        {
            // Native restoration must run even if a destroyed WPF child fails to close.
            try { _guard?.Dispose(); }
            finally
            {
                _guard = null;
                _clipper?.Dispose();
                _clipper = null;
                IsActive = false;
                if (releaseLease && _ownsLease) { _lease.ReleaseMutex(); _ownsLease = false; }
                if (!_disposed)
                {
                    _groups = new DesktopGroupPresenter();
                    BindGroups();
                }
            }
        }
    }

    private void SetStatus(bool active, string status)
    {
        if (!active) _retryAfter = DateTime.UtcNow.AddSeconds(5);
        if (IsActive == active && Status == status) return;
        IsActive = active;
        Status = status;
        StatusChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        StopSession();
        _lease.Dispose();
    }

    private delegate void WinEventCallback(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint minimum, uint maximum, nint module,
        WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
}
