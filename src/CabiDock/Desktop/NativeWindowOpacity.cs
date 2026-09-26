using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CabiDock.Desktop;

/// <summary>Constant alpha for our non-per-pixel WPF child windows, without click-through.</summary>
internal static class NativeWindowOpacity
{
    private const long Layered = 0x80000;
    private static readonly ConditionalWeakTable<HwndSource, LayeredStyleGuard> StyleGuards = new();

    internal static bool TrySet(nint window, double opacity, out string reason)
    {
        reason = "無法更新分類區不透明度。";
        if (!double.IsFinite(opacity) || opacity is < 0.3 or > 1
            || !NativeDesktop.IsWindow(window)
            || NativeDesktop.GetWindowThreadProcessId(window, out var process) == 0
            || process != Environment.ProcessId) return false;

        var original = NativeDesktop.ReadStyle(window, -20);
        var style = original.ToInt64();
        var source = HwndSource.FromHwnd(window);
        source?.Dispatcher.VerifyAccess();
        if (opacity == 1)
        {
            ReleaseStyleGuard(source);
            if ((style & Layered) != 0)
            {
                if (!NativeDesktop.WriteStyle(window, -20, (nint)(style & ~Layered)))
                {
                    reason = Failure("SetWindowLongPtr（移除 WS_EX_LAYERED）", Marshal.GetLastPInvokeError());
                    return false;
                }
                RedrawWindow(window, 0, 0, 0x1 | 0x4 | 0x80 | 0x400); // invalidate, erase, all children, frame
            }
            reason = "";
            return true;
        }

        // HwndTarget strips WS_EX_LAYERED for non-per-pixel WPF windows, including later
        // layout/DPI changes. Preserve only this style bit; all other messages keep flowing.
        if (source is not null) StyleGuards.GetValue(source, item => new LayeredStyleGuard(item));
        var alpha = checked((byte)Math.Round(opacity * 255));
        if ((style & Layered) != 0 && GetLayeredWindowAttributes(window, out _, out var currentAlpha, out var flags)
            && currentAlpha == alpha && flags == 2)
        {
            reason = "";
            return true;
        }
        if ((style & Layered) == 0 && !NativeDesktop.WriteStyle(window, -20, (nint)(style | Layered)))
        {
            reason = Failure("SetWindowLongPtr（設定 WS_EX_LAYERED）", Marshal.GetLastPInvokeError());
            ReleaseStyleGuard(source);
            return false;
        }
        // WS_EX_LAYERED supports child windows with the Windows 8+ compatibility manifest.
        // Do not use WPF AllowsTransparency/UpdateLayeredWindow with this constant-alpha path.
        if (!SetLayeredWindowAttributes(window, 0, alpha, 2))
        {
            var error = Marshal.GetLastPInvokeError();
            ReleaseStyleGuard(source);
            NativeDesktop.WriteStyle(window, -20, original);
            reason = Failure("SetLayeredWindowAttributes", error);
            return false;
        }
        reason = "";
        return true;
    }

    private static string Failure(string operation, int error) =>
        $"無法更新分類區不透明度：{operation}，Win32 錯誤 {error}（{new System.ComponentModel.Win32Exception(error).Message}）。";

    private static void ReleaseStyleGuard(HwndSource? source)
    {
        if (source is not null && StyleGuards.TryGetValue(source, out var guard))
        {
            guard.Dispose();
            StyleGuards.Remove(source);
        }
    }

    private sealed class LayeredStyleGuard : IDisposable
    {
        private readonly HwndSource _source;

        internal LayeredStyleGuard(HwndSource source)
        {
            _source = source;
            source.AddHook(Hook);
        }

        private nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x007C && wParam.ToInt64() == -20 && lParam != 0) // WM_STYLECHANGING, GWL_EXSTYLE
            {
                var styles = Marshal.PtrToStructure<StyleChange>(lParam);
                styles.New |= (uint)Layered;
                Marshal.StructureToPtr(styles, lParam, false);
                handled = true;
            }
            return 0;
        }

        public void Dispose()
        {
            if (!_source.IsDisposed) _source.RemoveHook(Hook);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StyleChange { public uint Old, New; }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint rectangle, nint region, uint flags);
}
