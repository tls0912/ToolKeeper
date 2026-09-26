using System.Runtime.InteropServices;
using System.Windows;
using CabiDock.Desktop;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopVisibilityAuditTests
{
    [Fact]
    public void NoExplicitRegionShowsAllPlannedIcons()
    {
        using var window = new TestWindow();
        var audit = DesktopVisibilityAudit.Capture(window.Handle, window.Handle, window.MakePlan());
        Assert.True(audit.Available, audit.Message);
        Assert.False(audit.HasExplicitIconRegion);
        Assert.Equal(2, audit.ManagedIconCount);
        Assert.Equal(0, audit.FullyClippedManagedIconCount);
        Assert.Equal(2, audit.PreservedIconCount);
        Assert.Equal(2, audit.FullyVisiblePreservedIconCount);
        Assert.Equal(0, audit.VisibleAttachedGroupCount);
    }

    [Fact]
    public void AuditDistinguishesFullSuppressionAndVisibilityFromPartialClipping()
    {
        using var window = new TestWindow();
        window.SetClipping(
            new Rect(10, 10, 20, 20), // First managed icon is completely suppressed.
            new Rect(60, 10, 10, 20), // Second managed icon is only partly suppressed.
            new Rect(180, 10, 10, 20)); // Second preserved icon is only partly visible.
        var audit = DesktopVisibilityAudit.Capture(window.Handle, window.Handle, window.MakePlan());
        Assert.True(audit.Available, audit.Message);
        Assert.True(audit.HasExplicitIconRegion);
        Assert.Equal(2, audit.ManagedIconCount);
        Assert.Equal(1, audit.FullyClippedManagedIconCount);
        Assert.Equal(2, audit.PreservedIconCount);
        Assert.Equal(1, audit.FullyVisiblePreservedIconCount);
    }

    [Fact]
    public void EmptyNativeRegionSuppressesEveryIcon()
    {
        using var window = new TestWindow();
        var empty = CreateRectRgn(0, 0, 0, 0);
        Assert.NotEqual(0, empty);
        Assert.NotEqual(0, SetWindowRgn(window.Handle, empty, false));
        var audit = DesktopVisibilityAudit.Capture(window.Handle, window.Handle, window.MakePlan());
        Assert.True(audit.Available, audit.Message);
        Assert.True(audit.HasExplicitIconRegion);
        Assert.Equal(2, audit.FullyClippedManagedIconCount);
        Assert.Equal(0, audit.FullyVisiblePreservedIconCount);
    }

    [Fact]
    public void OffWindowBoundsCannotOverflowOrCountAsFullyVisible()
    {
        using var window = new TestWindow();
        const string managedPath = @"C:\CabiDock-test\distant.txt";
        DesktopIconSnapshot[] shell =
        [
            new("distant", managedPath, managedPath, true, null, null),
            new("system", "::{test-system}", null, false, null, null)
        ];
        DesktopAccessibleIcon[] icons =
        [
            new("distant", new Rect(int.MaxValue - 500, 0, 400, 20)),
            new("system", new Rect(window.Left - 10, window.Top + 100, 20, 20))
        ];
        Assert.True(DesktopClipPlan.TryCreate(shell, [managedPath], icons, out var plan, out var reason), reason);
        var audit = DesktopVisibilityAudit.Capture(window.Handle, window.Handle, plan!);
        Assert.True(audit.Available, audit.Message);
        Assert.Equal(1, audit.FullyClippedManagedIconCount);
        Assert.Equal(0, audit.FullyVisiblePreservedIconCount);
    }

    [Fact]
    public void InvalidWindowReturnsUnavailableInsteadOfSuccessfulZeroCounts()
    {
        Assert.True(DesktopClipPlan.TryCreate([], [], [], out var plan, out _));
        var audit = DesktopVisibilityAudit.Capture(0, 0, plan!);
        Assert.False(audit.Available);
        Assert.NotEmpty(audit.Message);
    }

    // All clipping mutations below apply only to this hidden test-owned HWND.
    private sealed class TestWindow : IDisposable
    {
        public nint Handle { get; }
        public int Left { get; }
        public int Top { get; }

        public TestWindow()
        {
            Handle = CreateWindowEx(0, "STATIC", "CabiDock visibility audit test", 0x80000000,
                -400, -300, 320, 200, 0, 0, 0, 0);
            Assert.NotEqual(0, Handle);
            Assert.True(NativeDesktop.GetWindowRect(Handle, out var bounds));
            Left = bounds.Left;
            Top = bounds.Top;
        }

        public DesktopClipPlan MakePlan()
        {
            const string first = @"C:\CabiDock-test\first.txt";
            const string second = @"C:\CabiDock-test\second.txt";
            const string other = @"C:\CabiDock-test\other.txt";
            DesktopIconSnapshot[] shell =
            [
                new("first", first, first, true, null, null),
                new("second", second, second, true, null, null),
                new("system", "::{test-system}", null, false, null, null),
                new("other", other, other, true, null, null)
            ];
            DesktopAccessibleIcon[] icons =
            [
                new("first", new Rect(Left + 10, Top + 10, 20, 20)),
                new("second", new Rect(Left + 60, Top + 10, 20, 20)),
                new("system", new Rect(Left + 150, Top + 10, 20, 20)),
                new("other", new Rect(Left + 180, Top + 10, 20, 20))
            ];
            Assert.True(DesktopClipPlan.TryCreate(shell, [first, second], icons, out var plan, out var reason), reason);
            return plan!;
        }

        public void SetClipping(params Rect[] holes)
        {
            var region = CreateRectRgn(0, 0, 320, 200);
            Assert.NotEqual(0, region);
            try
            {
                foreach (var rectangle in holes)
                {
                    var hole = CreateRectRgn((int)rectangle.Left, (int)rectangle.Top,
                        (int)rectangle.Right, (int)rectangle.Bottom);
                    Assert.NotEqual(0, hole);
                    try { Assert.NotEqual(0, CombineRgn(region, region, hole, 4)); }
                    finally { DeleteObject(hole); }
                }
                Assert.NotEqual(0, SetWindowRgn(Handle, region, false));
                region = 0;
            }
            finally { if (region != 0) DeleteObject(region); }
        }

        public void Dispose() => DestroyWindow(Handle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
}
