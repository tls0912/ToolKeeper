using System.Runtime.InteropServices;

namespace CabiDock.Desktop;

internal static class NativeDesktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint window);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint window, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetWindowDpiAwarenessContext(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rectangle rectangle);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(nint window, ref Point point);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    internal static nint ReadStyle(nint window, int index) => nint.Size == 8
        ? GetWindowLongPtr(window, index) : GetWindowLong(window, index);
    internal static bool WriteStyle(nint window, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = nint.Size == 8 ? SetWindowLongPtr(window, index, value) : SetWindowLong(window, index, value.ToInt32());
        return previous != 0 || Marshal.GetLastPInvokeError() == 0;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);
}
