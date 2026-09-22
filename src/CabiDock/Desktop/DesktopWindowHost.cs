using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CabiDock.Desktop;

/// <summary>
/// Experimental child-window attachment. Deliberately unused by the normal app: attaching to
/// Explorer's view is not a documented Shell extension contract and needs target-OS validation.
/// This class changes only our own HWND, never Explorer styles, desktop files, or native icons.
/// </summary>
public sealed class DesktopWindowHost : IDisposable
{
    private const long Child = 0x40000000, Popup = 0x80000000;
    private const int StyleIndex = -16, ExtendedStyleIndex = -20;
    private readonly Window _window;
    private readonly HwndSource _source;
    private readonly nint _handle, _view, _shell;
    private readonly uint _shellProcess;
    private readonly nint _originalParent, _style, _extendedStyle;
    private readonly NativeDesktop.Rectangle _originalBounds;
    private bool _disposed;

    private DesktopWindowHost(Window window, nint handle, DesktopProbeResult probe)
    {
        _window = window;
        _handle = handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("群組 HWND 尚未就緒。");
        _view = probe.ViewWindow;
        _shell = probe.ShellWindow;
        _shellProcess = probe.ShellProcessId;
        _originalParent = NativeDesktop.GetParent(handle);
        _style = NativeDesktop.ReadStyle(handle, StyleIndex);
        _extendedStyle = NativeDesktop.ReadStyle(handle, ExtendedStyleIndex);
        if (!NativeDesktop.GetWindowRect(handle, out _originalBounds))
            throw new InvalidOperationException("無法讀取群組視窗位置。");
    }

    public bool IsAlive => !_disposed && IsOurWindow() && IsCurrentDesktop()
        && NativeDesktop.GetParent(_handle) == _view;

    /// <summary>
    /// Use a hidden, borderless, nontransparent, unowned window. The caller must keep it hidden
    /// if this returns false. No top-level fallback is created, and native icons remain visible.
    /// </summary>
    public static bool TryAttach(Window window, DesktopProbeResult probe,
        out DesktopWindowHost? host, out string reason)
    {
        window.Dispatcher.VerifyAccess();
        host = null;
        reason = "桌面附掛尚未經產品相容性驗證。";
        if (!probe.Available || !NativeDesktop.IsWindow(probe.ViewWindow))
        {
            reason = "桌面檢查未成功；不附掛群組。";
            return false;
        }
        if (window.IsVisible || window.Topmost || window.AllowsTransparency || window.ShowInTaskbar
            || window.WindowStyle != WindowStyle.None || window.Owner is not null)
        {
            reason = "桌面原型需使用隱藏、無框線、不透明且無擁有者的群組視窗。";
            return false;
        }

        DesktopWindowHost? candidate = null;
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var windowContext = NativeDesktop.GetWindowDpiAwarenessContext(handle);
            var parentContext = NativeDesktop.GetWindowDpiAwarenessContext(probe.ViewWindow);
            if (windowContext == 0 || parentContext == 0 || !NativeDesktop.AreDpiAwarenessContextsEqual(windowContext, parentContext))
            {
                reason = "群組與 Explorer 的 DPI 模式不一致；不變更程序 DPI 或附掛視窗。";
                return false;
            }
            candidate = new(window, handle, probe);
            if (!candidate.IsCurrentDesktop()) throw new InvalidOperationException("Explorer 已變更，請重新檢查。");
            var childStyle = (candidate._style.ToInt64() & ~Popup) | Child;
            if (!NativeDesktop.WriteStyle(handle, StyleIndex, ToNativeStyle(childStyle))
                || !NativeDesktop.WriteStyle(handle, ExtendedStyleIndex,
                    ToNativeStyle(candidate._extendedStyle.ToInt64() & ~0x40000L & ~0x8L))) // APPWINDOW, TOPMOST
                throw new InvalidOperationException("無法設定群組視窗樣式。");

            Marshal.SetLastPInvokeError(0);
            var previous = NativeDesktop.SetParent(handle, probe.ViewWindow);
            if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
                throw new InvalidOperationException("Windows 拒絕桌面附掛。");
            if (!candidate.IsAlive) throw new InvalidOperationException("桌面附掛結果驗證失敗。");
            var rectangle = candidate._originalBounds;
            if (!candidate.TrySetBounds(new Rect(rectangle.Left, rectangle.Top,
                    Math.Max(1, rectangle.Right - rectangle.Left), Math.Max(1, rectangle.Bottom - rectangle.Top)), out reason))
                throw new InvalidOperationException(reason);

            host = candidate;
            reason = "已附掛原型視窗；原生桌面圖示仍保留，尚未通過 V1 桌面整合驗收。";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            candidate?.Dispose();
            reason = $"桌面附掛失敗：{error.Message}";
            return false;
        }
    }

    /// <summary>Physical screen pixels, not WPF DIPs. Caller clamps to primary monitor work area.</summary>
    public bool TrySetBounds(Rect physicalScreenPixels, out string reason)
    {
        _window.Dispatcher.VerifyAccess();
        reason = "";
        if (!IsAlive)
        {
            HideOwnWindow();
            reason = "Explorer 或群組視窗已變更；已停止顯示這個原型群組。";
            return false;
        }
        if (!double.IsFinite(physicalScreenPixels.X) || !double.IsFinite(physicalScreenPixels.Y)
            || !double.IsFinite(physicalScreenPixels.Width) || !double.IsFinite(physicalScreenPixels.Height)
            || Math.Abs(physicalScreenPixels.X) > 100000 || Math.Abs(physicalScreenPixels.Y) > 100000
            || physicalScreenPixels.Width is < 1 or > 100000 || physicalScreenPixels.Height is < 1 or > 100000)
        {
            reason = "群組尺寸或位置無效。";
            return false;
        }
        var point = new NativeDesktop.Point { X = (int)Math.Round(physicalScreenPixels.X), Y = (int)Math.Round(physicalScreenPixels.Y) };
        if (!NativeDesktop.ScreenToClient(_view, ref point)
            || !NativeDesktop.SetWindowPos(_handle, 0, point.X, point.Y,
                (int)Math.Round(physicalScreenPixels.Width), (int)Math.Round(physicalScreenPixels.Height),
                0x10 | 0x20)) // NOACTIVATE | FRAMECHANGED; HWND_TOP only within this parent
        {
            HideOwnWindow();
            reason = "無法更新桌面群組位置；已隱藏原型群組。";
            return false;
        }
        return true;
    }

    /// <summary>Detaches our window and leaves it hidden. Never recreates Explorer or edits its view.</summary>
    public void Dispose()
    {
        _window.Dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        if (!IsOurWindow()) return;
        HideOwnWindow();
        var parent = _originalParent != 0 && NativeDesktop.IsWindow(_originalParent) ? _originalParent : 0;
        NativeDesktop.SetParent(_handle, parent);
        NativeDesktop.WriteStyle(_handle, StyleIndex, _style);
        NativeDesktop.WriteStyle(_handle, ExtendedStyleIndex, _extendedStyle);
        var point = new NativeDesktop.Point { X = _originalBounds.Left, Y = _originalBounds.Top };
        if (parent != 0) NativeDesktop.ScreenToClient(parent, ref point);
        NativeDesktop.SetWindowPos(_handle, 0, point.X, point.Y,
            _originalBounds.Right - _originalBounds.Left, _originalBounds.Bottom - _originalBounds.Top,
            0x10 | 0x20 | 0x4); // NOACTIVATE | FRAMECHANGED | NOZORDER, remain hidden
    }

    private bool IsCurrentDesktop()
    {
        NativeDesktop.GetWindowThreadProcessId(_view, out var process);
        return NativeDesktop.GetShellWindow() == _shell && NativeDesktop.IsWindow(_view) && process == _shellProcess;
    }

    private bool IsOurWindow() => NativeDesktop.IsWindow(_handle)
        && ReferenceEquals(HwndSource.FromHwnd(_handle), _source);

    private void HideOwnWindow()
    {
        if (IsOurWindow()) NativeDesktop.ShowWindow(_handle, 0);
    }

    private static nint ToNativeStyle(long value) => nint.Size == 8 ? (nint)value : unchecked((nint)(int)value);
}
