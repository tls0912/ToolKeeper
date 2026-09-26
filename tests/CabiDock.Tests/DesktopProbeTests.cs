using System.Runtime.InteropServices;
using CabiDock.Desktop;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopProbeTests
{
    private const int NoInterface = unchecked((int)0x80004002);

    [Fact]
    public void ReadsWindowWhenShellViewRejectsOleWindowQuery()
    {
        // Reproduce Explorer's compatibility behavior without opening or changing any window.
        // The native helper must obtain GetWindow from IShellView after IOleWindow QI fails.
        var expectedWindow = (nint)0x123456;
        var view = new ShellViewWithoutOleWindow(expectedWindow);
        var unknown = Marshal.GetIUnknownForObject(view);
        nint oleWindow = 0;
        try
        {
            var oleWindowId = new Guid("00000114-0000-0000-C000-000000000046");
            Assert.Equal(NoInterface, Marshal.QueryInterface(unknown, in oleWindowId, out oleWindow));
            Assert.Equal(nint.Zero, oleWindow);

            Assert.Equal(expectedWindow, DesktopProbe.GetViewWindow(view));
            Assert.Equal(1, view.GetWindowCallCount);
        }
        finally
        {
            if (oleWindow != 0) Marshal.Release(oleWindow);
            Marshal.Release(unknown);
        }
    }

    [Fact]
    public void UnsupportedObjectPreservesNoInterfaceFailure()
    {
        // Marshal.ThrowExceptionForHR maps E_NOINTERFACE to InvalidCastException.
        var error = Assert.Throws<InvalidCastException>(() =>
            DesktopProbe.GetViewWindow(new ObjectWithoutWindow()));

        Assert.Equal(NoInterface, error.HResult);
    }

    [ComVisible(true)]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellViewWithoutOleWindow
    {
        [PreserveSig] int GetWindow(out nint window);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ShellViewWithoutOleWindow(nint window) : IShellViewWithoutOleWindow
    {
        public int GetWindowCallCount { get; private set; }

        public int GetWindow(out nint result)
        {
            GetWindowCallCount++;
            result = window;
            return 0;
        }

        public int ContextSensitiveHelp(bool enter) => 0;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ObjectWithoutWindow;
}
