using System.Windows.Interop;

namespace MarkPad;

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowBoundsMessage);
    }

    private nint WindowBoundsMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int getMinMaxInfo = 0x0024;
        // F11 deliberately uses the whole monitor, including the taskbar area.
        if (message == getMinMaxInfo && !_fullScreen)
        {
            MonitorWorkArea.ApplyMaximizedBounds(handle, lParam);
            // Let WPF also apply MinWidth/MinHeight and any explicit maximum constraints.
        }
        return 0;
    }
}
