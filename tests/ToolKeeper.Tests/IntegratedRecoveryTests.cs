using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using CabiDock.Desktop;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class IntegratedRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolKeeperHelperRestoresSyntheticWindowWithoutStartingThePlatform(bool disconnect)
    {
        using var window = new TestListView();
        SetRegion(window.Handle, CreateRectRgn(4, 7, 123, 145));
        var original = ReadRegion(window.Handle);
        var host = Path.Combine(AppContext.BaseDirectory, "ToolKeeper.exe");
        Assert.True(File.Exists(host), "The test must run the actual ToolKeeper host.");
        Assert.True(DesktopRecoveryGuard.TryStartForOwnedWindow(window.Handle, host, out var guard, out var reason), reason);
        using (guard)
        {
            Assert.True(guard!.IsAlive);
            var process = (Process)typeof(DesktopRecoveryGuard).GetField("_guardian", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(guard)!;
            process.Refresh();
            Assert.Equal(0, process.MainWindowHandle);
            SetRegion(window.Handle, CreateRectRgn(10, 10, 20, 20));
            Assert.NotEqual(original, ReadRegion(window.Handle));
            if (disconnect)
            {
                var pipe = (NamedPipeServerStream)typeof(DesktopRecoveryGuard).GetField("_pipe", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(guard)!;
                pipe.Dispose();
                var elapsed = Stopwatch.StartNew();
                while (!ReadRegion(window.Handle)!.SequenceEqual(original!) && elapsed.Elapsed < TimeSpan.FromSeconds(10))
                    await Task.Delay(25);
                Assert.Equal(original, ReadRegion(window.Handle));
            }
        }
        Assert.Equal(original, ReadRegion(window.Handle));
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
