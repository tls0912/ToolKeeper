using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CabiDock.Desktop;

internal sealed record NativeWindowOpacitySample(
    double Opacity, bool Layered, byte? Alpha, uint Flags, bool ChildParentRetained, bool ClickThrough);

internal sealed record NativeWindowOpacityReport(
    bool Succeeded, string Message, IReadOnlyList<NativeWindowOpacitySample> Samples);

/// <summary>Exercises the manifested apphost using hidden windows owned only by this process.</summary>
internal static class NativeWindowOpacityDiagnostics
{
    internal static NativeWindowOpacityReport Capture()
    {
        var samples = new List<NativeWindowOpacitySample>();
        var parent = new Window { ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        var child = new Window { ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        DesktopWindowHost? host = null;
        try
        {
            var parentHandle = new WindowInteropHelper(parent).EnsureHandle();
            NativeDesktop.GetWindowThreadProcessId(parentHandle, out var process);
            if (!DesktopWindowHost.TryAttach(child, new DesktopProbeResult
            {
                Available = true, ShellWindow = NativeDesktop.GetShellWindow(),
                ViewWindow = parentHandle, ShellProcessId = process
            }, out host, out var reason)) return new(false, reason, samples);

            var handle = new WindowInteropHelper(child).Handle;
            foreach (var opacity in new[] { 0.5, 0.3, 1.0 })
            {
                if (!host!.TrySetOpacity(opacity, out reason)) return new(false, reason, samples);
                if (opacity < 1)
                {
                    // Simulate WPF rewriting extended styles during its normal layout lifecycle.
                    // Preserve the alpha bit without discarding unrelated requested style changes.
                    var requestedStyle = (NativeDesktop.ReadStyle(handle, -20).ToInt64() & ~0x80000L) | 0x08000000L;
                    if (!NativeDesktop.WriteStyle(handle, -20, (nint)requestedStyle)
                        || (NativeDesktop.ReadStyle(handle, -20).ToInt64() & 0x08000000L) == 0
                        || !host.TrySetBounds(new Rect(10, 10, 120, 80), out reason))
                        return new(false, $"無法驗證版面更新後的不透明度：{reason}", samples);
                }
                var attributesAvailable = GetLayeredWindowAttributes(handle, out _, out var alpha, out var flags);
                var style = NativeDesktop.ReadStyle(handle, -20).ToInt64();
                var sample = new NativeWindowOpacitySample(opacity, (style & 0x80000) != 0,
                    attributesAvailable ? alpha : null, attributesAvailable ? flags : 0,
                    NativeDesktop.GetParent(handle) == parentHandle
                        && (NativeDesktop.ReadStyle(handle, -16).ToInt64() & 0x40000000) != 0,
                    (style & 0x20) != 0);
                samples.Add(sample);
                if (!sample.ChildParentRetained || sample.ClickThrough
                    || (opacity == 1 ? sample.Layered : !sample.Layered || sample.Flags != 2
                        || sample.Alpha != checked((byte)Math.Round(opacity * 255))))
                    return new(false, "分類區視窗的原生不透明度驗證失敗。", samples);
            }
            return new(true, "原生子視窗不透明度與還原驗證成功。", samples);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(false, error.Message, samples);
        }
        finally { host?.Dispose(); child.Close(); parent.Close(); }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);
}
