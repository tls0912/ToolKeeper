using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using CabiDock.Models;

namespace CabiDock.Views;

internal sealed class ShellIconProvider
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? Get(DesktopItem item)
    {
        if (_cache.TryGetValue(item.FullPath, out var cached)) return cached;
        ImageSource? image = null;
        var info = new ShellFileInfo();
        try
        {
            // SHGFI_ICON returns an owned HICON; release it after WPF copies its pixels.
            if (SHGetFileInfo(item.FullPath, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100) != 0 && info.Icon != 0)
            {
                image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
            }
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or System.IO.IOException)
        {
            // A disappearing desktop item or a failing shell handler uses the fallback glyph.
        }
        finally { if (info.Icon != 0) DestroyIcon(info.Icon); }
        _cache[item.FullPath] = image;
        return image;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
