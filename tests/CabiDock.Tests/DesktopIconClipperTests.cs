using System.Runtime.InteropServices;
using System.Windows;
using CabiDock.Desktop;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopIconClipperTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeometryRetryRestoresExactOriginalRegionAndCanApplyAgain(bool customRegion)
    {
        // A hidden HWND owned by the test process; no Explorer window is ever mutated.
        // WPF tests initialize process DPI concurrently; pin this thread's coordinate context.
        var previousDpi = SetThreadDpiAwarenessContext(-4);
        var window = CreateWindowEx(0, "STATIC", "CabiDock clip retry test", 0x80000000,
            -400, -300, 320, 200, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        var original = CreateRectRgn(0, 0, 0, 0);
        var current = CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (customRegion)
            {
                var region = CreateRectRgn(0, 0, 200, 180);
                if (SetWindowRgn(window, region, false) == 0) { DeleteObject(region); Assert.Fail("Cannot set test region."); }
                Assert.NotEqual(0, GetWindowRgn(window, original));
            }
            Assert.True(NativeDesktop.GetWindowRect(window, out var bounds));
            NativeDesktop.GetWindowThreadProcessId(window, out var process);
            const string path = @"C:\CabiDock-tests\managed.txt";
            DesktopIconSnapshot[] shell =
            [
                new("managed", path, path, true, null, null),
                new("system", "::{test-system}", null, false, null, null)
            ];
            DesktopAccessibleIcon[] accessible =
            [
                new("managed", new Rect(bounds.Left + 10, bounds.Top + 10, 20, 20)),
                new("system", new Rect(bounds.Left + 70, bounds.Top + 10, 20, 20))
            ];
            Assert.True(DesktopClipPlan.TryCreate(shell, [path], accessible, out var plan, out var reason), reason);
            using var clipper = new DesktopIconClipper(window, process);
            for (var attempt = 0; attempt < 2; ++attempt)
            {
                Assert.True(clipper.TryApply(plan!, out reason), reason);
                var clipped = DesktopVisibilityAudit.Capture(window, window, plan!);
                Assert.True(clipped.Available, clipped.Message);
                Assert.Equal(1, clipped.FullyClippedManagedIconCount);
                Assert.Equal(1, clipped.FullyVisiblePreservedIconCount);
                Assert.True(clipper.TryRestore());
                var result = GetWindowRgn(window, current);
                if (customRegion) { Assert.NotEqual(0, result); Assert.True(EqualRgn(original, current)); }
                else Assert.Equal(0, result);
                var restored = DesktopVisibilityAudit.Capture(window, window, plan!);
                Assert.Equal(0, restored.FullyClippedManagedIconCount);
                Assert.Equal(1, restored.FullyVisiblePreservedIconCount);
            }
        }
        finally
        {
            DeleteObject(original); DeleteObject(current); DestroyWindow(window);
            SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EqualRgn(nint first, nint second);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
}
