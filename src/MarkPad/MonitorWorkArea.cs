using System.Windows;
using ToolKeeper.UI;

namespace MarkPad;

// The product retains its sizing commands; native monitor calculations live with the shared frame.
internal static class MonitorWorkArea
{
    public static Rect Get(Window window, bool full = false) => WindowWorkArea.Get(window, full);
}