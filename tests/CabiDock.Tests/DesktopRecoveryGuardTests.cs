using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using CabiDock.Desktop;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopRecoveryGuardTests
{
    [Theory]
    [InlineData(@"C:\apps\ToolKeeper.exe")]
    public void AppHostRunsItsOwnExecutableForRecovery(string executable)
    {
        var start = DesktopRecoveryGuard.CreateHostStartInfo(executable, @"C:\apps\ToolKeeper.dll");
        Assert.Equal(executable, start.FileName);
        Assert.Empty(start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }

    [Fact]
    public void DotnetHostRunsEntryAssemblyAndRejectsDesktopLibrary()
    {
        var start = DesktopRecoveryGuard.CreateHostStartInfo(@"C:\dotnet\dotnet.exe", @"C:\apps\ToolKeeper.dll");
        Assert.Equal(@"C:\dotnet\dotnet.exe", start.FileName);
        Assert.Equal(new[] { @"C:\apps\ToolKeeper.dll" }, start.ArgumentList);
        Assert.Throws<InvalidOperationException>(() => DesktopRecoveryGuard.CreateHostStartInfo(
            @"C:\dotnet\dotnet.exe", typeof(DesktopRecoveryGuard).Assembly.Location));
        Assert.Throws<InvalidOperationException>(() => DesktopRecoveryGuard.CreateHostStartInfo(null, null));
    }

    [Fact]
    public void DisposeRestoresOriginalComplexRegion()
    {
        using var window = new TestListView();
        var first = CreateRectRgn(2, 3, 52, 61);
        var second = CreateRectRgn(80, 90, 150, 160);
        Assert.NotEqual(0, CombineRgn(first, first, second, 2));
        DeleteObject(second);
        SetRegion(window.Handle, first);
        var original = ReadRegion(window.Handle);
        Assert.NotNull(original);

        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(window.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var guard, out var reason), reason);
        using (guard)
        {
            Assert.True(guard!.IsAlive);
            SetRegion(window.Handle, CreateRectRgn(10, 10, 20, 20));
            Assert.NotEqual(original, ReadRegion(window.Handle));
        }
        Assert.Equal(original, ReadRegion(window.Handle));
    }

    [Fact]
    public void DisposeRemovesRegionWhenOriginalWindowHadNone()
    {
        using var window = new TestListView();
        Assert.Null(ReadRegion(window.Handle));
        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(window.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var guard, out var reason), reason);
        using (guard) SetRegion(window.Handle, CreateRectRgn(10, 10, 20, 20));
        Assert.Null(ReadRegion(window.Handle));
    }

    [Fact]
    public void IndependentSyntheticWindowsCanArmGuardsConcurrently()
    {
        using var first = new TestListView();
        using var second = new TestListView();
        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(first.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var firstGuard, out var reason), reason);
        using (firstGuard)
        {
            Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(second.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var secondGuard, out reason), reason);
            using (secondGuard)
            {
                Assert.True(firstGuard!.IsAlive);
                Assert.True(secondGuard!.IsAlive);
                SetRegion(first.Handle, CreateRectRgn(1, 2, 30, 40));
                SetRegion(second.Handle, CreateRectRgn(5, 6, 50, 60));
            }
            Assert.Null(ReadRegion(second.Handle));
            Assert.NotNull(ReadRegion(first.Handle));
        }
        Assert.Null(ReadRegion(first.Handle));
    }

    [Fact]
    public async Task GuardianRestoresOriginalRegionAfterConnectionIsLost()
    {
        using var window = new TestListView();
        SetRegion(window.Handle, CreateRectRgn(4, 7, 123, 145));
        var original = ReadRegion(window.Handle);
        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(window.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var guard, out var reason), reason);
        using (guard)
        {
            SetRegion(window.Handle, CreateRectRgn(10, 10, 20, 20));
            // Simulate a broken parent connection without invoking the parent's restoration path.
            var pipe = (NamedPipeServerStream)typeof(DesktopRecoveryGuard)
                .GetField("_pipe", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(guard)!;
            pipe.Dispose();
            var deadline = Stopwatch.StartNew();
            while (!ReadRegion(window.Handle)!.SequenceEqual(original!) && deadline.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(25);
            Assert.Equal(original, ReadRegion(window.Handle));
            Assert.False(guard!.IsAlive);
        }
    }

    [Fact]
    public async Task ParentStillRestoresWhenGuardianHasExited()
    {
        using var window = new TestListView();
        SetRegion(window.Handle, CreateRectRgn(5, 8, 127, 149));
        var original = ReadRegion(window.Handle);
        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(window.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var guard, out var reason), reason);
        using (guard)
        {
            SetRegion(window.Handle, CreateRectRgn(10, 10, 20, 20));
            var guardian = (Process)typeof(DesktopRecoveryGuard)
                .GetField("_guardian", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(guard)!;
            guardian.Kill();
            await guardian.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(guard!.IsAlive);
        }
        Assert.Equal(original, ReadRegion(window.Handle));
    }

    [Fact]
    public void ClipperPreservesOtherIconsAndRebuildsFromOriginalRegionOnUpdate()
    {
        using var window = new TestListView();
        var originalRegion = CreateRectRgn(0, 0, 180, 180);
        var originalHole = CreateRectRgn(150, 0, 180, 30);
        Assert.NotEqual(0, CombineRgn(originalRegion, originalRegion, originalHole, 4));
        DeleteObject(originalHole);
        SetRegion(window.Handle, originalRegion);
        var original = ReadRegion(window.Handle);
        Assert.True(NativeDesktop.GetWindowRect(window.Handle, out var bounds));

        const string managedPath = @"C:\CabiDock-test\managed.txt";
        DesktopIconSnapshot[] shell =
        [
            new("managed", managedPath, managedPath, true, null, null),
            new("Recycle Bin", "::{test-recycle-bin}", null, false, null, null)
        ];
        DesktopClipPlan PlanAt(int x)
        {
            DesktopAccessibleIcon[] accessible =
            [
                new("managed", new System.Windows.Rect(bounds.Left + x, bounds.Top + 30, 30, 30)),
                new("Recycle Bin", new System.Windows.Rect(bounds.Left + 90, bounds.Top + 90, 30, 30))
            ];
            Assert.True(DesktopClipPlan.TryCreate(shell, [managedPath], accessible, out var plan, out var reason), reason);
            return plan!;
        }

        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(window.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var guard, out var reason), reason);
        using (guard)
        using (var clipper = new DesktopIconClipper(window.Handle, checked((uint)Environment.ProcessId)))
        {
            Assert.True(clipper.TryApply(PlanAt(20), out reason), reason);
            AssertPointVisibility(window.Handle,
                (30, 40, false), // Only the represented item is suppressed.
                (100, 100, true), (5, 170, true), // System icon and background remain accessible.
                (165, 15, false), (190, 190, false)); // Preexisting region is respected.

            Assert.True(clipper.TryApply(PlanAt(60), out reason), reason);
            AssertPointVisibility(window.Handle,
                (30, 40, true), (70, 40, false), // Moving the managed icon closes the previous hole.
                (100, 100, true), (5, 170, true),
                (165, 15, false), (190, 190, false));
        }
        Assert.Equal(original, ReadRegion(window.Handle));
    }

    [Fact]
    public void ClipperHandlesNegativeWindowOriginAndSkipsFarOffscreenBoundsWithoutOverflow()
    {
        using var window = new TestListView();
        Assert.True(NativeDesktop.SetWindowPos(window.Handle, 0, -400, -300, 0, 0, 0x0015));
        Assert.True(NativeDesktop.GetWindowRect(window.Handle, out var bounds));
        Assert.Equal(-400, bounds.Left);
        Assert.Equal(-300, bounds.Top);
        const string localPath = @"C:\CabiDock-test\local.txt";
        const string distantPath = @"C:\CabiDock-test\distant.txt";
        DesktopIconSnapshot[] shell =
        [
            new("local", localPath, localPath, true, null, null),
            new("distant", distantPath, distantPath, true, null, null),
            new("Recycle Bin", "::{test-recycle-bin}", null, false, null, null)
        ];
        DesktopAccessibleIcon[] accessible =
        [
            new("local", new System.Windows.Rect(bounds.Left + 20, bounds.Top + 30, 30, 30)),
            // Both edges are valid screen integers. Subtracting the negative window origin
            // before intersection would overflow the right edge and could corrupt the region.
            new("distant", new System.Windows.Rect(int.MaxValue - 500, bounds.Top + 100, 400, 20)),
            new("Recycle Bin", new System.Windows.Rect(bounds.Left + 90, bounds.Top + 90, 30, 30))
        ];
        Assert.True(DesktopClipPlan.TryCreate(shell, [localPath, distantPath], accessible, out var plan, out var reason), reason);
        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(window.Handle, System.IO.Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe"), out var guard, out reason), reason);
        using (guard)
        using (var clipper = new DesktopIconClipper(window.Handle, checked((uint)Environment.ProcessId)))
        {
            Assert.True(clipper.TryApply(plan!, out reason), reason);
            AssertPointVisibility(window.Handle,
                (30, 40, false), (19, 40, true), (50, 40, true), // Correct local conversion.
                (100, 100, true), (5, 170, true),
                (0, 110, true), (100, 110, true), (199, 110, true)); // No wrapped offscreen hole.
        }
        Assert.Null(ReadRegion(window.Handle));
    }

    [Fact]
    public void InvalidWindowCannotArmGuard()
    {
        Assert.False(DesktopRecoveryGuard.TryStart(0, out var guard, out var reason));
        Assert.Null(guard);
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void PrivateHelperArgumentsNeverFallThroughToNormalStartup()
    {
        Assert.False(DesktopRecoveryGuard.TryRun([]));
        Assert.False(DesktopRecoveryGuard.TryRun(["--data-directory", "example"]));
        Assert.True(DesktopRecoveryGuard.TryRun(["--desktop-recovery"]));
        Assert.True(DesktopRecoveryGuard.TryRun(["--desktop-recovery", "invalid request"]));
    }

    private static void SetRegion(nint window, nint region)
    {
        Assert.NotEqual(0, region);
        if (SetWindowRgn(window, region, false) == 0)
        {
            DeleteObject(region);
            Assert.Fail("Could not set the synthetic window region.");
        }
    }

    private static byte[]? ReadRegion(nint window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            if (GetWindowRgn(window, region) == 0) return null;
            var size = GetRegionData(region, 0, null);
            Assert.NotEqual(0u, size);
            var data = new byte[size];
            Assert.Equal(size, GetRegionData(region, size, data));
            return data;
        }
        finally { DeleteObject(region); }
    }

    private static void AssertPointVisibility(nint window, params (int X, int Y, bool Visible)[] points)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, GetWindowRgn(window, region));
            foreach (var point in points)
                Assert.True(PtInRegion(region, point.X, point.Y) == point.Visible,
                    $"Expected ({point.X}, {point.Y}) visible={point.Visible}.");
        }
        finally { DeleteObject(region); }
    }

    // The target has its own message pump so the helper can change its region cross-process.
    // It is never shown and does not use Explorer or any real desktop data.
    private sealed class TestListView : IDisposable
    {
        private readonly Thread _thread;
        private readonly uint _threadId;
        public nint Handle { get; }

        public TestListView()
        {
            var ready = new TaskCompletionSource<(nint Window, uint ThreadId)>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                nint window = 0;
                try
                {
                    var controls = new CommonControls { Size = 8, Classes = 1 };
                    if (!InitCommonControlsEx(ref controls)) throw new InvalidOperationException("Common controls initialization failed.");
                    window = CreateWindowEx(0, "SysListView32", "CabiDock recovery test", 0x80000000,
                        0, 0, 200, 200, 0, 0, 0, 0);
                    if (window == 0) throw new InvalidOperationException("Test window creation failed.");
                    ready.SetResult((window, GetCurrentThreadId()));
                    while (GetMessage(out var message, 0, 0, 0) > 0)
                    {
                        TranslateMessage(ref message);
                        DispatchMessage(ref message);
                    }
                }
                catch (Exception exception) { ready.TrySetException(exception); }
                finally { if (window != 0) DestroyWindow(window); }
            }) { IsBackground = true, Name = "CabiDock recovery test window" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            (Handle, _threadId) = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            PostThreadMessage(_threadId, 0x0012, 0, 0);
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)), "Test window message pump did not stop.");
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CommonControls { public uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitCommonControlsEx(ref CommonControls controls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern uint GetRegionData(nint region, uint size, [Out] byte[]? data);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
}
