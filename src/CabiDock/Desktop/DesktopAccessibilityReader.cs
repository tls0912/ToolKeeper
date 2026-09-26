using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace CabiDock.Desktop;

/// <summary>Reads desktop icons without confusing a ListView's hidden header with an icon.</summary>
internal static class DesktopAccessibilityReader
{
    private const int ListItemRole = 34;
    private const int WindowRole = 9;
    private const int MaximumChildren = 10032;

    internal static bool TryRead(nint listWindow, out IReadOnlyList<DesktopAccessibleIcon> icons,
        out string reason)
    {
        icons = [];
        reason = "無法讀取原生桌面圖示範圍。";
        IAccessible? accessible = null;
        object[]? children = null;
        try
        {
            if (!NativeDesktop.IsWindow(listWindow) || ReadClassName(listWindow) != "SysListView32")
                return false;

            var iid = typeof(IAccessible).GUID;
            Marshal.ThrowExceptionForHR(AccessibleObjectFromWindow(listWindow, 0xFFFFFFFC, ref iid, out accessible));
            var count = accessible.accChildCount;
            if (count < 0 || count > MaximumChildren)
            {
                reason = "原生桌面輔助使用項目數量無效；保留原生桌面。";
                return false;
            }

            children = new object[count];
            if (count > 0)
            {
                Marshal.ThrowExceptionForHR(AccessibleChildren(accessible, 0, count, children, out var obtained));
                if (obtained != count)
                {
                    reason = "原生桌面項目在讀取期間變更，稍後重試。";
                    return false;
                }
            }

            var result = new List<DesktopAccessibleIcon>(count);
            foreach (var child in children)
            {
                IAccessible owner;
                int childId;
                if (child is int id && id > 0)
                {
                    owner = accessible;
                    childId = id;
                }
                else if (child is IAccessible childObject)
                {
                    owner = childObject;
                    childId = 0; // CHILDID_SELF for full accessible child objects.
                }
                else
                {
                    reason = "原生桌面含有無法辨識的輔助使用項目；保留原生桌面。";
                    return false;
                }

                var role = owner.get_accRole(childId);
                if (role is not int roleId || roleId != ListItemRole)
                {
                    // Explorer also exposes the hidden SysHeader32 as a dispatch child in
                    // icon view. It is window structure, not a Shell item. Do not filter by
                    // its localized name or ignore other unexpected provider content.
                    if (child is IAccessible header && role is int headerRole
                        && IsHiddenListHeader(listWindow, header, headerRole)) continue;
                    reason = "原生桌面含有非圖示的未知項目；保留原生桌面。";
                    return false;
                }

                var name = owner.get_accName(childId);
                owner.accLocation(out var x, out var y, out var width, out var height, childId);
                if (string.IsNullOrEmpty(name) || width <= 0 || height <= 0)
                {
                    reason = "原生桌面圖示名稱或範圍無效；保留原生桌面。";
                    return false;
                }
                result.Add(new(name, new System.Windows.Rect(x, y, width, height)));
            }

            if (accessible.accChildCount != count || !NativeDesktop.IsWindow(listWindow))
            {
                reason = "原生桌面項目在讀取期間變更，稍後重試。";
                return false;
            }
            icons = result.AsReadOnly();
            reason = "";
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            reason = $"無法確認原生圖示範圍（0x{error.HResult:X8}），已保留原生桌面。";
            return false;
        }
        finally
        {
            // A provider may return the same RCW for more than one child. Never final-release
            // it, and never release one RCW twice during this enumeration.
            var released = new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (children is not null)
                foreach (var child in children) ReleaseOnce(child, released);
            ReleaseOnce(accessible, released);
        }
    }

    private static bool IsHiddenListHeader(nint listWindow, IAccessible child, int role)
    {
        return role == WindowRole && WindowFromAccessibleObject(child, out var headerWindow) >= 0
            && headerWindow != 0 && headerWindow != listWindow
            && NativeDesktop.IsWindow(headerWindow) && IsChild(listWindow, headerWindow)
            && ReadClassName(headerWindow) == "SysHeader32" && !IsWindowVisible(headerWindow)
            && (NativeDesktop.ReadStyle(headerWindow, -16).ToInt64() & 0x10000000) == 0;
    }

    private static string ReadClassName(nint window)
    {
        var name = new StringBuilder(64);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private static void ReleaseOnce(object? value, HashSet<object> released)
    {
        if (value is not null && Marshal.IsComObject(value) && released.Add(value)) Marshal.ReleaseComObject(value);
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(IAccessible accessible, int start, int count,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] children, out int obtained);
    [DllImport("oleacc.dll")]
    private static extern int WindowFromAccessibleObject(IAccessible accessible, out nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parent, nint child);
}
