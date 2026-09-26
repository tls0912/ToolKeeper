using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CabiDock.Models;

namespace CabiDock.Views;

/// <summary>Hosts the Explorer Shell menu, including extension submenus, in our actual visual host.</summary>
internal static class ShellContextMenu
{
    private const uint FirstShellCommand = 1;
    private const uint LastShellCommand = 0x6fff;
    private const uint FirstCategoryCommand = 0x7000;

    internal static void Show(FrameworkElement anchor, DesktopItem item, IReadOnlyList<CategoryDefinition> categories,
        string currentCategory, Point screenPoint, Action<string> assign, Action rename)
    {
        var source = PresentationSource.FromVisual(anchor) as HwndSource
            ?? throw new InvalidOperationException("分類區尚未連接至視窗。");
        using var shellItem = ShellMenuItem.Open(item.FullPath, source.Handle);
        using var menu = shellItem.CreateMenu(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        var categoryMenu = CreatePopupMenu();
        if (categoryMenu == 0) throw new Win32Exception();
        var appended = false;
        try
        {
            for (var index = 0; index < categories.Count && FirstCategoryCommand + index <= 0x7fff; index++)
            {
                var category = categories[index];
                Check(AppendMenu(categoryMenu, category.Id == currentCategory ? 0x8u : 0,
                    (nuint)(FirstCategoryCommand + index), category.Name.Replace("&", "&&")));
            }
            Check(AppendMenu(menu.Handle, 0x800, 0, null)); // MF_SEPARATOR
            Check(AppendMenu(menu.Handle, 0x10, (nuint)categoryMenu, "CabiDock 分類"));
            appended = true; // DestroyMenu owns and destroys attached submenus.

            nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled) =>
                shellItem.HandleMenuMessage(message, wParam, lParam, ref handled);
            source.AddHook(Hook);
            uint selected;
            try
            {
                SetForegroundWindow(source.Handle);
                selected = TrackPopupMenuEx(menu.Handle, 0x100 | 0x2, (int)Math.Round(screenPoint.X),
                    (int)Math.Round(screenPoint.Y), source.Handle, 0); // TPM_RETURNCMD | TPM_RIGHTBUTTON
            }
            finally { source.RemoveHook(Hook); PostMessage(source.Handle, 0, 0, 0); }
            if (selected >= FirstCategoryCommand && selected - FirstCategoryCommand < categories.Count)
                assign(categories[(int)(selected - FirstCategoryCommand)].Id);
            else if (selected >= FirstShellCommand && selected <= LastShellCommand)
            {
                var offset = selected - FirstShellCommand;
                // Explorer's rename verb expects an Explorer view to start inline editing.
                // We supply the editing UI and delegate the actual rename to the Shell folder.
                if (string.Equals(shellItem.GetCanonicalVerb(offset), "rename", StringComparison.OrdinalIgnoreCase)) rename();
                else shellItem.Invoke(offset, screenPoint, Keyboard.Modifiers);
            }
        }
        finally { if (!appended) DestroyMenu(categoryMenu); }
    }

    private static void Check(bool success) { if (!success) throw new Win32Exception(); }

    internal sealed class ShellMenuItem : IDisposable
    {
        private nint _absolutePidl;
        private readonly nint _childPidl;
        private readonly nint _owner;
        private readonly IShellFolder _folder;
        private IContextMenu? _context;
        private IContextMenu2? _context2;
        private IContextMenu3? _context3;

        private ShellMenuItem(nint absolutePidl, nint childPidl, nint owner, IShellFolder folder)
        { _absolutePidl = absolutePidl; _childPidl = childPidl; _owner = owner; _folder = folder; }

        internal static ShellMenuItem Open(string path, nint owner)
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, 0, out var absolute, 0, out _));
            try
            {
                var iid = typeof(IShellFolder).GUID;
                Marshal.ThrowExceptionForHR(SHBindToParent(absolute, ref iid, out var folder, out var child));
                return new(absolute, child, owner, folder);
            }
            catch { Marshal.FreeCoTaskMem(absolute); throw; }
        }

        internal NativeMenu CreateMenu(bool extended)
        {
            if (_context is null)
            {
                var iid = typeof(IContextMenu).GUID;
                Marshal.ThrowExceptionForHR(_folder.GetUIObjectOf(_owner, 1, [_childPidl], ref iid, 0, out var context));
                _context = (IContextMenu)context;
                _context2 = context as IContextMenu2;
                _context3 = context as IContextMenu3;
            }
            var menu = new NativeMenu();
            try
            {
                // CMF_CANRENAME | CMF_ITEMMENU; Shift adds the normal Shell extended commands.
                Marshal.ThrowExceptionForHR(_context.QueryContextMenu(menu.Handle, 0, FirstShellCommand,
                    LastShellCommand, 0x10u | 0x80u | (extended ? 0x100u : 0)));
                return menu;
            }
            catch { menu.Dispose(); throw; }
        }

        internal string GetCanonicalVerb(uint offset)
        {
            if (_context is null) return "";
            const int capacity = 256;
            var buffer = Marshal.AllocCoTaskMem(capacity * 2);
            try
            {
                Marshal.WriteInt16(buffer, 0);
                if (_context.GetCommandString(offset, 4, 0, buffer, capacity) >= 0) // GCS_VERBW
                    return (Marshal.PtrToStringUni(buffer, capacity) ?? "").Split('\0')[0];
                Marshal.WriteByte(buffer, 0);
                return _context.GetCommandString(offset, 0, 0, buffer, capacity) >= 0
                    ? (Marshal.PtrToStringAnsi(buffer, capacity) ?? "").Split('\0')[0] : "";
            }
            finally { Marshal.FreeCoTaskMem(buffer); }
        }

        internal nint HandleMenuMessage(int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message is not (0x117 or 0x2b or 0x2c or 0x120)) return 0;
            // WM_DRAWITEM / WM_MEASUREITEM with nonzero wParam refer to controls, not menus.
            if (message is 0x2b or 0x2c && wParam != 0) return 0;
            if (_context3 is not null && _context3.HandleMenuMsg2((uint)message, wParam, lParam, out var result) == 0)
            { handled = true; return result; }
            if (message != 0x120 && _context2 is not null && _context2.HandleMenuMsg((uint)message, wParam, lParam) == 0)
            { handled = true; return 0; }
            return 0;
        }

        internal void Invoke(uint offset, Point point, ModifierKeys modifiers)
        {
            var command = new InvokeCommandInfo
            {
                Size = (uint)Marshal.SizeOf<InvokeCommandInfo>(), Mask = 0x4000 | 0x20000000,
                Owner = _owner, Verb = (nint)offset, VerbUnicode = (nint)offset, Show = 1,
                Point = new NativePoint { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) }
            };
            if (modifiers.HasFlag(ModifierKeys.Shift)) command.Mask |= 0x10000000;
            if (modifiers.HasFlag(ModifierKeys.Control)) command.Mask |= 0x40000000;
            Marshal.ThrowExceptionForHR(_context!.InvokeCommand(ref command));
        }

        internal void Rename(string newName)
        {
            ValidateName(newName);
            uint attributes = 0x10; // SFGAO_CANRENAME
            Marshal.ThrowExceptionForHR(_folder.GetAttributesOf(1, [_childPidl], ref attributes));
            if ((attributes & 0x10) == 0) throw new IOException("Windows 不允許重新命名這個項目。");
            // Full leaf name, including the extension, relative to its existing folder.
            var result = _folder.SetNameOf(_owner, _childPidl, newName, 0x8001, out var renamedPidl);
            if (renamedPidl != 0) Marshal.FreeCoTaskMem(renamedPidl);
            Marshal.ThrowExceptionForHR(result);
        }

        internal static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name.EndsWith(' ') || name.EndsWith('.'))
                throw new ArgumentException("請輸入有效的檔名，不能含有 \\ / : * ? \" < > |，也不能以空白或句點結尾。", nameof(name));
            var stem = name.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                throw new ArgumentException("這是 Windows 保留名稱，請使用其他檔名。", nameof(name));
        }

        public void Dispose()
        {
            if (_absolutePidl == 0) return;
            // All context interfaces share one RCW. Release it exactly once.
            if (_context is not null) Marshal.ReleaseComObject(_context);
            _context = null; _context2 = null; _context3 = null;
            Marshal.ReleaseComObject(_folder);
            Marshal.FreeCoTaskMem(_absolutePidl);
            _absolutePidl = 0;
        }
    }

    internal sealed class NativeMenu : IDisposable
    {
        internal nint Handle { get; private set; } = CreatePopupMenu();
        internal NativeMenu() { if (Handle == 0) throw new Win32Exception(); }
        public void Dispose() { if (Handle != 0) DestroyMenu(Handle); Handle = 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeCommandInfo
    {
        internal uint Size, Mask;
        internal nint Owner, Verb, Parameters, Directory;
        internal int Show;
        internal uint HotKey;
        internal nint Icon, Title, VerbUnicode, ParametersUnicode, DirectoryUnicode, TitleUnicode;
        internal NativePoint Point;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint window, nint bind, [MarshalAs(UnmanagedType.LPWStr)] string name, nint eaten, out nint pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint window, uint flags, out nint result);
        [PreserveSig] int BindToObject(nint pidl, nint bind, ref Guid iid, out nint result);
        [PreserveSig] int BindToStorage(nint pidl, nint bind, ref Guid iid, out nint result);
        [PreserveSig] int CompareIDs(nint param, nint first, nint second);
        [PreserveSig] int CreateViewObject(nint window, ref Guid iid, out nint result);
        [PreserveSig] int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(nint window, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] pidls,
            ref Guid iid, nint reserved, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, nint name);
        [PreserveSig] int SetNameOf(nint window, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint result);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo command);
        [PreserveSig] int GetCommandString(nuint command, uint type, nint reserved, nint name, uint capacity);
    }
    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo command);
        [PreserveSig] int GetCommandString(nuint command, uint type, nint reserved, nint name, uint capacity);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
    }
    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint index, uint first, uint last, uint flags);
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo command);
        [PreserveSig] int GetCommandString(nuint command, uint type, nint reserved, nint name, uint capacity);
        [PreserveSig] int HandleMenuMsg(uint message, nint wParam, nint lParam);
        [PreserveSig] int HandleMenuMsg2(uint message, nint wParam, nint lParam, out nint result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bind, out nint pidl, uint attributes, out uint resultAttributes);
    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(nint pidl, ref Guid iid, out IShellFolder parent, out nint child);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
