using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace CabiDock.Desktop;

public sealed record DesktopIconSnapshot(string Name, string ParsingName, string? FileSystemPath,
    bool IsFileSystem, int? X, int? Y);

public sealed record DesktopProbeResult
{
    public bool Available { get; init; }
    public string Message { get; init; } = "尚未檢查桌面整合。";
    public string? FailureStage { get; init; }
    public string? ErrorCode { get; init; }
    public bool? IconGeometryAvailable { get; init; }
    public string? IconGeometryMessage { get; init; }
    public string? DesktopDpiContext { get; init; }
    public string? GroupDpiContext { get; init; }
    public bool? DpiContextsMatch { get; init; }
    public DesktopVisibilityAuditResult? VisibilityAudit { get; init; }
    public int FileSystemItemCount => Items.Count(item => item.IsFileSystem);
    // Non-filesystem Shell items: normally Recycle Bin, This PC, etc., possibly third-party namespaces.
    public int SystemItemCount => Items.Count(item => !item.IsFileSystem);
    public bool? AutoArrange { get; init; }
    public uint ViewMode { get; init; }
    [JsonIgnore] public nint ShellWindow { get; init; }
    [JsonIgnore] public nint ViewWindow { get; init; }
    public string ShellWindowHandle => $"0x{ShellWindow:X}";
    public string ViewWindowHandle => $"0x{ViewWindow:X}";
    public uint ShellProcessId { get; init; }
    public IReadOnlyList<DesktopIconSnapshot> Items { get; init; } = [];
    // Region clipping is visual only; the Shell does not offer semantic per-item hiding here.
    public bool CanSuppressIndividualIcons => false;
}

/// <summary>
/// A read-only Shell diagnostic. It neither sets view flags nor changes files, icon positions,
/// visibility, selection, or Explorer windows. Call on an STA thread.
/// </summary>
public static class DesktopProbe
{
    public static DesktopProbeResult CaptureDiagnostics()
    {
        var report = Capture();
        if (!report.Available) return report;
        var group = new System.Windows.Window { WindowStyle = System.Windows.WindowStyle.None, ShowInTaskbar = false };
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(group).EnsureHandle();
            var desktopDpi = NativeDesktop.GetWindowDpiAwarenessContext(report.ViewWindow);
            var groupDpi = NativeDesktop.GetWindowDpiAwarenessContext(handle);
            report = report with
            {
                DesktopDpiContext = $"0x{desktopDpi:X}", GroupDpiContext = $"0x{groupDpi:X}",
                DpiContextsMatch = NativeDesktop.AreDpiAwarenessContextsEqual(desktopDpi, groupDpi)
            };
        }
        finally { group.Close(); }
        var scan = new DesktopScanner().Scan(DesktopScanner.ResolveRoots());
        if (!scan.Succeeded) return report with { IconGeometryAvailable = false, IconGeometryMessage = scan.Error };
        var available = DesktopIconClipper.TryCapture(report, scan.Items.Select(item => item.FullPath),
            out var iconWindow, out var plan, out var reason);
        return report with
        {
            IconGeometryAvailable = available,
            IconGeometryMessage = reason,
            VisibilityAudit = available ? DesktopVisibilityAudit.Capture(iconWindow, report.ViewWindow, plan!) : null
        };
    }

    public static DesktopProbeResult Capture()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            return new() { Message = "桌面檢查必須在 STA 執行緒執行。" };

        object? windows = null, desktop = null, browser = null, view = null, folder = null;
        nint folderPidl = 0;
        var stage = "GetShellWindow";
        try
        {
            var shellWindow = NativeDesktop.GetShellWindow();
            if (shellWindow == 0) return new() { Message = "找不到 Windows 桌面 Shell；保留原生桌面。" };
            NativeDesktop.GetWindowThreadProcessId(shellWindow, out var shellProcessId);

            stage = "建立 ShellWindows";
            var windowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!;
            windows = Activator.CreateInstance(windowsType)!;
            object location = 0; // CSIDL_DESKTOP
            object? root = null; // VT_EMPTY
            int dispatchWindow;
            stage = "IShellWindows.FindWindowSW";
            desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out dispatchWindow, 1);
            if (desktop is null) throw new InvalidOperationException("桌面 Shell view 尚未就緒。");

            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
            var browserId = typeof(IShellBrowser).GUID;
            stage = "IServiceProvider.QueryService(IShellBrowser)";
            Check(((IShellServiceProvider)desktop).QueryService(ref service, ref browserId, out browser));
            stage = "IShellBrowser.QueryActiveShellView";
            Check(((IShellBrowser)browser).QueryActiveShellView(out view));
            stage = "QueryInterface(IFolderView)";
            var folderView = (IFolderView)view;
            stage = "IUnknown_GetWindow(IShellView)";
            var viewWindow = GetViewWindow(view);
            NativeDesktop.GetWindowThreadProcessId(viewWindow, out var viewProcessId);
            if (!NativeDesktop.IsWindow(viewWindow) || viewProcessId != shellProcessId)
                throw new InvalidOperationException("桌面視窗在檢查期間已變更。");

            stage = "IFolderView.GetCurrentViewMode";
            Check(folderView.GetCurrentViewMode(out var mode));
            stage = "IFolderView.GetAutoArrange";
            var arrangement = folderView.GetAutoArrange();
            Check(arrangement);
            stage = "IFolderView.ItemCount";
            Check(folderView.ItemCount(2, out var count)); // SVGIO_ALLVIEW
            if (count < 0 || count > 10000) throw new InvalidOperationException("桌面項目數量超過原型檢查上限。");

            var folderId = new Guid("000214E6-0000-0000-C000-000000000046"); // IShellFolder
            stage = "IFolderView.GetFolder(IShellFolder)";
            Check(folderView.GetFolder(ref folderId, out folder));
            stage = "SHGetIDListFromObject";
            Check(SHGetIDListFromObject(folder, out folderPidl));
            var items = new List<DesktopIconSnapshot>(count);
            for (var index = 0; index < count; index++)
            {
                nint childPidl = 0, absolutePidl = 0;
                object? shellItem = null;
                try
                {
                    stage = $"IFolderView.Item({index})";
                    Check(folderView.Item(index, out childPidl));
                    stage = $"ILCombine({index})";
                    absolutePidl = ILCombine(folderPidl, childPidl);
                    if (absolutePidl == 0) throw new OutOfMemoryException();
                    var itemId = typeof(IShellItem).GUID;
                    stage = $"SHCreateItemFromIDList({index})";
                    Check(SHCreateItemFromIDList(absolutePidl, ref itemId, out shellItem));
                    var item = (IShellItem)shellItem;
                    stage = $"IShellItem.GetAttributes({index})";
                    Check(item.GetAttributes(0x40000000, out var attributes)); // SFGAO_FILESYSTEM
                    var isFileSystem = (attributes & 0x40000000) != 0;
                    var name = ReadName(item, 0) ?? ""; // SIGDN_NORMALDISPLAY
                    var parsingName = ReadName(item, 0x80028000) ?? name;
                    var path = isFileSystem ? ReadName(item, 0x80058000) : null;
                    var positionResult = folderView.GetItemPosition(childPidl, out var position);
                    items.Add(new(name, parsingName, path, isFileSystem,
                        positionResult >= 0 ? position.X : null, positionResult >= 0 ? position.Y : null));
                }
                finally
                {
                    Release(shellItem);
                    if (absolutePidl != 0) Marshal.FreeCoTaskMem(absolutePidl);
                    if (childPidl != 0) Marshal.FreeCoTaskMem(childPidl);
                }
            }

            // Explorer is asynchronous: fail the whole diagnostic if its view changed while reading it.
            stage = "IFolderView.ItemCount（核對）";
            Check(folderView.ItemCount(2, out var finalCount));
            if (finalCount != count || NativeDesktop.GetShellWindow() != shellWindow || !NativeDesktop.IsWindow(viewWindow))
                throw new InvalidOperationException("桌面內容在檢查期間已變更，請稍後重試。");

            return new()
            {
                Available = true,
                Message = "已讀取桌面資訊；此診斷不變更桌面顯示。",
                AutoArrange = arrangement == 0,
                ViewMode = mode,
                ShellWindow = shellWindow,
                ViewWindow = viewWindow,
                ShellProcessId = shellProcessId,
                Items = items.AsReadOnly()
            };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new()
            {
                FailureStage = stage,
                ErrorCode = $"0x{error.HResult:X8}",
                Message = $"桌面檢查未完成：{stage}（0x{error.HResult:X8}）；保留原生桌面。"
            };
        }
        finally
        {
            if (folderPidl != 0) Marshal.FreeCoTaskMem(folderPidl);
            Release(folder);
            Release(view);
            Release(browser);
            Release(desktop);
            Release(windows);
        }
    }

    private static string? ReadName(IShellItem item, uint kind)
    {
        nint text = 0;
        try
        {
            return item.GetDisplayName(kind, out text) >= 0 && text != 0 ? Marshal.PtrToStringUni(text) : null;
        }
        finally { if (text != 0) Marshal.FreeCoTaskMem(text); }
    }

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    internal static nint GetViewWindow(object view)
    {
        // Some Shell views expose IShellView but reject QI for its IOleWindow base.
        // This Shell helper also tries the derived interfaces instead of assuming base QI works.
        // https://learn.microsoft.com/windows/win32/api/shlwapi/nf-shlwapi-iunknown_getwindow
        Check(IUnknown_GetWindow(view, out var window));
        return window;
    }
    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object value, out nint pidl);
    [DllImport("shlwapi.dll", ExactSpelling = true)]
    private static extern int IUnknown_GetWindow([MarshalAs(UnmanagedType.IUnknown)] object value, out nint window);
    [DllImport("shell32.dll")]
    private static extern nint ILCombine(nint parent, nint child);
    [DllImport("shell32.dll")]
    private static extern int SHCreateItemFromIDList(nint pidl, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out nint window);
        [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
        [PreserveSig] int InsertMenusSB(nint menu, nint widths);
        [PreserveSig] int SetMenuSB(nint menu, nint reserved, nint activeObject);
        [PreserveSig] int RemoveMenusSB(nint menu);
        [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
        [PreserveSig] int TranslateAcceleratorSB(nint message, ushort id);
        [PreserveSig] int BrowseObject(nint pidl, uint flags);
        [PreserveSig] int GetViewStateStream(uint mode, out nint stream);
        [PreserveSig] int GetControlWindow(uint id, out nint window);
        [PreserveSig] int SendControlMsg(uint id, uint message, nint wParam, nint lParam, out nint result);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder);
        [PreserveSig] int Item(int index, out nint pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid iid, out nint items);
        [PreserveSig] int GetSelectionMarkedItem(out int index);
        [PreserveSig] int GetFocusedItem(out int index);
        [PreserveSig] int GetItemPosition(nint pidl, out NativeDesktop.Point position);
        [PreserveSig] int GetSpacing(out NativeDesktop.Point spacing);
        [PreserveSig] int GetDefaultSpacing(out NativeDesktop.Point spacing);
        [PreserveSig] int GetAutoArrange();
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        [PreserveSig] int GetParent(out nint parent);
        [PreserveSig] int GetDisplayName(uint kind, out nint name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(nint other, uint hint, out int order);
    }
}
