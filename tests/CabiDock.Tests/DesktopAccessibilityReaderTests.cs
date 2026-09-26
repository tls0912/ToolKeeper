using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Accessibility;
using CabiDock.Desktop;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopAccessibilityReaderTests
{
    [Fact]
    public void HiddenHeaderIsExcludedWhileFileAndSystemLabelsRemain()
    {
        RunSta(() =>
        {
            using var list = new TestListView();
            list.Add("managed.txt");
            list.Add("資源回收筒");
            list.Add("other.txt");
            list.UseIconView();
            Assert.NotEqual(0, list.Header);
            Assert.Equal(0, NativeDesktop.ReadStyle(list.Header, -16).ToInt64() & 0x10000000);

            // Reproduce Explorer's extra structural accessible child, independently of
            // Shell enumeration. A count comparison before filtering would reject this.
            Assert.Equal(4, ReadRawChildCount(list.Handle));
            Assert.True(DesktopAccessibilityReader.TryRead(list.Handle, out var icons, out var reason), reason);
            Assert.Equal(new[] { "managed.txt", "資源回收筒", "other.txt" }, icons.Select(icon => icon.Name));
            Assert.All(icons, icon => Assert.True(icon.ScreenBounds.Width > 0 && icon.ScreenBounds.Height > 0));

            const string managedPath = @"C:\CabiDock-test\managed.txt";
            DesktopIconSnapshot[] shell =
            [
                new("managed.txt", managedPath, managedPath, true, null, null),
                new("資源回收筒", "::{test-recycle-bin}", null, false, null, null),
                new("other.txt", @"C:\CabiDock-test\other.txt", @"C:\CabiDock-test\other.txt", true, null, null)
            ];
            Assert.True(DesktopClipPlan.TryCreate(shell, [managedPath], icons, out var plan, out reason), reason);
            Assert.Single(plan!.ManagedScreenBounds);
            Assert.Equal(2, plan.PreservedScreenBounds.Count);
        });
    }

    [Fact]
    public void HeaderWithVisibleStyleIsNotDiscardedEvenWhenItsParentIsHidden()
    {
        RunSta(() =>
        {
            using var list = new TestListView();
            list.Add("managed.txt");
            var style = NativeDesktop.ReadStyle(list.Header, -16).ToInt64();
            Assert.True(NativeDesktop.WriteStyle(list.Header, -16, (nint)(style | 0x10000000)));
            Assert.NotEqual(0, NativeDesktop.ReadStyle(list.Header, -16).ToInt64() & 0x10000000);
            Assert.False(DesktopAccessibilityReader.TryRead(list.Handle, out var icons, out var reason));
            Assert.Empty(icons);
            Assert.NotEmpty(reason);
        });
    }

    [Fact]
    public void EmptyIconViewMayStillContainHiddenHeader()
    {
        RunSta(() =>
        {
            using var list = new TestListView();
            list.UseIconView();
            Assert.Equal(1, ReadRawChildCount(list.Handle));
            Assert.True(DesktopAccessibilityReader.TryRead(list.Handle, out var icons, out var reason), reason);
            Assert.Empty(icons);
        });
    }

    [Fact]
    public void InvalidWindowDoesNotProduceAUsableSnapshot()
    {
        Assert.False(DesktopAccessibilityReader.TryRead(0, out var icons, out var reason));
        Assert.Empty(icons);
        Assert.NotEmpty(reason);
    }

    private static int ReadRawChildCount(nint window)
    {
        var iid = typeof(IAccessible).GUID;
        Marshal.ThrowExceptionForHR(AccessibleObjectFromWindow(window, 0xFFFFFFFC, ref iid, out var accessible));
        try { return accessible.accChildCount; }
        finally { Marshal.ReleaseComObject(accessible); }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Synthetic ListView accessibility test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    // This hidden, test-owned native control never accesses Explorer or desktop files.
    private sealed class TestListView : IDisposable
    {
        private int _count;
        public nint Handle { get; }
        public nint Header => SendMessage(Handle, 0x101F, 0, 0); // LVM_GETHEADER

        public TestListView()
        {
            var controls = new CommonControls { Size = 8, Classes = 1 };
            Assert.True(InitCommonControlsEx(ref controls));
            Handle = CreateWindowEx(0, "SysListView32", "CabiDock accessibility test", 0x80000101,
                0, 0, 600, 400, 0, 0, 0, 0); // WS_POPUP | LVS_AUTOARRANGE | LVS_REPORT
            Assert.NotEqual(0, Handle);
            var column = new ListColumn { Mask = 2, Width = 300 };
            Assert.Equal(0, SendColumn(Handle, 0x1061, 0, ref column)); // LVM_INSERTCOLUMNW
            Assert.NotEqual(0, Header);
        }

        public void Add(string name)
        {
            var text = Marshal.StringToHGlobalUni(name);
            try
            {
                var item = new ListItem { Mask = 1, Index = _count, Text = text };
                Assert.Equal((nint)_count, SendItem(Handle, 0x104D, 0, ref item)); // LVM_INSERTITEMW
                _count++;
            }
            finally { Marshal.FreeHGlobal(text); }
        }

        public void UseIconView()
        {
            var style = NativeDesktop.ReadStyle(Handle, -16).ToInt64();
            Assert.True(NativeDesktop.WriteStyle(Handle, -16, (nint)(style & ~3L))); // LVS_ICON
            Assert.True(NativeDesktop.SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x0037));
            SendMessage(Handle, 0x1016, 0, 0); // LVM_ARRANGE
        }

        public void Dispose() => DestroyWindow(Handle);
    }

    [StructLayout(LayoutKind.Sequential)] private struct CommonControls { public uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] private struct ListColumn
    {
        public uint Mask;
        public int Format, Width;
        public nint Text;
        public int TextCapacity, SubItem, Image, Order, MinimumWidth, DefaultWidth, IdealWidth;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ListItem
    {
        public uint Mask;
        public int Index, SubItem;
        public uint State, StateMask;
        public nint Text;
        public int TextCapacity, Image;
        public nint Parameter;
        public int Indent, GroupId;
        public uint Columns;
        public nint ColumnIndices, ColumnFormats;
        public int Group;
    }
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitCommonControlsEx(ref CommonControls controls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendItem(nint window, uint message, nint wParam, ref ListItem item);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendColumn(nint window, uint message, nint wParam, ref ListColumn column);
}
