using System.Runtime.InteropServices;

namespace CabiDock.Desktop;

/// <summary>Clips only represented icons. Does not change Shell layout, files or selection.</summary>
internal sealed class DesktopIconClipper : IDisposable
{
    private readonly nint _originalRegion;
    private readonly bool _hadRegion;
    private readonly uint _process;
    private bool _disposed;
    public nint Window { get; }

    public DesktopIconClipper(nint window, uint process)
    {
        Window = window;
        _process = process;
        _originalRegion = CreateRectRgn(0, 0, 0, 0);
        if (_originalRegion == 0) throw new InvalidOperationException("無法保存桌面顯示區域。");
        _hadRegion = GetWindowRgn(window, _originalRegion) != 0;
    }

    public bool IsAlive => !_disposed && NativeDesktop.IsWindow(Window)
        && NativeDesktop.GetWindowThreadProcessId(Window, out var process) != 0 && process == _process;

    public static bool TryCapture(DesktopProbeResult probe, IEnumerable<string> paths,
        out nint window, out DesktopClipPlan? plan, out string reason)
    {
        window = 0;
        plan = null;
        reason = "無法讀取原生桌面圖示範圍。";
        try
        {
            if (!probe.Available || !NativeDesktop.IsWindow(probe.ViewWindow)) return false;
            window = FindWindowEx(probe.ViewWindow, 0, "SysListView32", null);
            if (window == 0 || NativeDesktop.GetWindowThreadProcessId(window, out var process) == 0
                || process != probe.ShellProcessId || !IsWindowVisible(window))
            {
                reason = "原生桌面圖示視窗尚未就緒，或已由使用者隱藏。";
                return false;
            }
            // Mirrored regions use a different coordinate origin; do not guess its geometry.
            if ((NativeDesktop.ReadStyle(window, -20).ToInt64() & 0x400000) != 0)
            {
                reason = "目前桌面使用鏡像座標，暫不接管。";
                return false;
            }
            if (!DesktopAccessibilityReader.TryRead(window, out var icons, out reason)) return false;
            if (NativeDesktop.GetShellWindow() != probe.ShellWindow
                || NativeDesktop.GetParent(window) != probe.ViewWindow) return false;
            return DesktopClipPlan.TryCreate(probe.Items, paths, icons, out plan, out reason);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            reason = $"無法確認原生圖示範圍（0x{error.HResult:X8}），已保留原生桌面。";
            return false;
        }
    }

    public bool TryApply(DesktopClipPlan plan, out string reason)
    {
        reason = "無法更新原生圖示顯示區域。";
        if (!IsAlive || !NativeDesktop.GetWindowRect(Window, out var bounds)) return false;
        var region = CreateRectRgn(0, 0, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        if (region == 0) return false;
        try
        {
            if (_hadRegion && CombineRgn(region, region, _originalRegion, 1) == 0) return false; // AND
            foreach (var rectangle in plan.ManagedScreenBounds)
            {
                // Clip in double precision before converting to window-relative integers:
                // an offscreen accessibility rectangle must never overflow into the window.
                var intersection = System.Windows.Rect.Intersect(rectangle,
                    new System.Windows.Rect(bounds.Left, bounds.Top, bounds.Right - (double)bounds.Left, bounds.Bottom - (double)bounds.Top));
                if (intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0) continue;
                var hole = CreateRectRgn((int)Math.Floor(intersection.Left - bounds.Left),
                    (int)Math.Floor(intersection.Top - bounds.Top),
                    (int)Math.Ceiling(intersection.Right - bounds.Left),
                    (int)Math.Ceiling(intersection.Bottom - bounds.Top));
                if (hole == 0) return false;
                try { if (CombineRgn(region, region, hole, 4) == 0) return false; } // DIFF
                finally { DeleteObject(hole); }
            }
            if (!IsAlive || SetWindowRgn(Window, region, true) == 0) return false;
            region = 0; // Ownership transferred to Windows.
            reason = "";
            return true;
        }
        finally { if (region != 0) DeleteObject(region); }
    }

    /// <summary>Temporarily restores visibility without disarming the independent recovery guard.</summary>
    public bool TryRestore()
    {
        if (!IsAlive) return false;
        nint region = 0;
        try
        {
            if (_hadRegion)
            {
                region = CreateRectRgn(0, 0, 0, 0);
                if (region == 0 || CombineRgn(region, _originalRegion, 0, 5) == 0) return false; // COPY
            }
            if (!IsAlive || SetWindowRgn(Window, region, true) == 0) return false;
            region = 0;
            return true;
        }
        finally { if (region != 0) DeleteObject(region); }
    }

    // RecoveryGuard owns restoration; this object only owns its private GDI snapshot.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DeleteObject(_originalRegion);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
