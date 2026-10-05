using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace CabiDock.Desktop;

public sealed record DesktopGroupVisibility(string WindowHandle, string Title, double Opacity);

public sealed record DesktopVisibilityAuditResult
{
    public bool Available { get; init; }
    public string Message { get; init; } = "尚未檢查桌面顯示區域。";
    public bool HasExplicitIconRegion { get; init; }
    public int ManagedIconCount { get; init; }
    public int FullyClippedManagedIconCount { get; init; }
    public int PreservedIconCount { get; init; }
    public int FullyVisiblePreservedIconCount { get; init; }
    public int VisibleAttachedGroupCount { get; init; }
    public IReadOnlyList<DesktopGroupVisibility> VisibleGroups { get; init; } = [];
}

/// <summary>Read-only inspection of native clipping and attached CabiDock group windows.</summary>
internal static class DesktopVisibilityAudit
{
    internal static DesktopVisibilityAuditResult Capture(nint iconWindow, nint viewWindow, DesktopClipPlan plan)
    {
        nint visibleRegion = 0, nativeRegion = 0;
        try
        {
            if (!NativeDesktop.IsWindow(iconWindow) || !NativeDesktop.IsWindow(viewWindow)
                || !NativeDesktop.GetWindowRect(iconWindow, out var rectangle))
                return new() { Message = "桌面視窗已變更，無法核對顯示區域。" };

            var bounds = new Rect(rectangle.Left, rectangle.Top,
                rectangle.Right - (double)rectangle.Left, rectangle.Bottom - (double)rectangle.Top);
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > int.MaxValue || bounds.Height > int.MaxValue)
                return new() { Message = "桌面視窗範圍無效。" };

            visibleRegion = CreateRectRgn(0, 0, (int)bounds.Width, (int)bounds.Height);
            nativeRegion = CreateRectRgn(0, 0, 0, 0);
            if (visibleRegion == 0 || nativeRegion == 0) throw new InvalidOperationException("無法建立檢查區域。");
            // GetWindowRgn returns ERROR when the window has no explicit region. In that
            // case its rectangular window bounds are the effective clipping region.
            var hasExplicitRegion = GetWindowRgn(iconWindow, nativeRegion) != 0;
            if (hasExplicitRegion && CombineRgn(visibleRegion, visibleRegion, nativeRegion, 1) == 0)
                throw new InvalidOperationException("無法核對原生顯示區域。");

            var clipped = plan.ManagedScreenBounds.Count(icon => IsFullyClipped(icon, bounds, visibleRegion));
            var preserved = plan.PreservedScreenBounds.Count(icon => IsFullyVisible(icon, bounds, visibleRegion));
            var attachedGroups = ReadAttachedGroups(viewWindow);
            if (!NativeDesktop.IsWindow(iconWindow) || !NativeDesktop.IsWindow(viewWindow))
                return new() { Message = "桌面視窗在核對期間變更。" };

            return new()
            {
                Available = true,
                Message = "已核對原生圖示裁切與附掛群組；此檢查不變更桌面。",
                HasExplicitIconRegion = hasExplicitRegion,
                ManagedIconCount = plan.ManagedScreenBounds.Count,
                FullyClippedManagedIconCount = clipped,
                PreservedIconCount = plan.PreservedScreenBounds.Count,
                FullyVisiblePreservedIconCount = preserved,
                VisibleAttachedGroupCount = attachedGroups.Count,
                VisibleGroups = attachedGroups
            };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new() { Message = $"無法核對桌面顯示區域（0x{error.HResult:X8}）。" };
        }
        finally
        {
            if (nativeRegion != 0) DeleteObject(nativeRegion);
            if (visibleRegion != 0) DeleteObject(visibleRegion);
        }
    }

    private static bool IsFullyClipped(Rect icon, Rect windowBounds, nint visibleRegion)
    {
        var intersection = Rect.Intersect(icon, windowBounds);
        if (intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0) return true;
        return IsEmptyCombination(intersection, windowBounds, visibleRegion, 1); // RGN_AND
    }

    private static bool IsFullyVisible(Rect icon, Rect windowBounds, nint visibleRegion)
    {
        // A partly off-window icon cannot be fully visible, even if its in-window part is.
        return windowBounds.Contains(icon) && IsEmptyCombination(icon, windowBounds, visibleRegion, 4); // RGN_DIFF
    }

    private static bool IsEmptyCombination(Rect rectangle, Rect windowBounds, nint visibleRegion, int mode)
    {
        var region = CreateRectRgn((int)Math.Floor(rectangle.Left - windowBounds.Left),
            (int)Math.Floor(rectangle.Top - windowBounds.Top),
            (int)Math.Ceiling(rectangle.Right - windowBounds.Left),
            (int)Math.Ceiling(rectangle.Bottom - windowBounds.Top));
        if (region == 0) throw new InvalidOperationException("無法建立圖示檢查區域。");
        try
        {
            var result = CombineRgn(region, region, visibleRegion, mode);
            if (result == 0) throw new InvalidOperationException("無法核對圖示檢查區域。");
            return result == 1; // NULLREGION
        }
        finally { DeleteObject(region); }
    }

    private static IReadOnlyList<DesktopGroupVisibility> ReadAttachedGroups(nint viewWindow)
    {
        var groups = new List<DesktopGroupVisibility>();
        var knownProcesses = new Dictionary<uint, bool>();
        EnumChildWindows(viewWindow, (window, _) =>
        {
            if (NativeDesktop.GetParent(window) != viewWindow || !IsWindowVisible(window)) return true;
            var title = new StringBuilder(256);
            if (GetWindowText(window, title, title.Capacity) <= 0
                || !title.ToString().StartsWith("CabiDock｜", StringComparison.Ordinal)) return true;
            NativeDesktop.GetWindowThreadProcessId(window, out var processId);
            if (!knownProcesses.TryGetValue(processId, out var isCabiDock))
            {
                try
                {
                    using var process = Process.GetProcessById(checked((int)processId));
                    isCabiDock = string.Equals(process.ProcessName, "ToolKeeper", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(process.ProcessName, "CabiDock", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    isCabiDock = false; // The owner may exit while its window is enumerated.
                }
                knownProcesses[processId] = isCabiDock;
            }
            if (isCabiDock)
            {
                var opacity = GetLayeredWindowAttributes(window, out var colorKey, out var alpha, out var flags)
                    && (flags & 2) != 0 ? alpha / 255d : 1;
                groups.Add(new($"0x{window:X}", title.ToString(), opacity));
            }
            return true;
        }, 0);
        return groups.AsReadOnly();
    }

    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder title, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
}
