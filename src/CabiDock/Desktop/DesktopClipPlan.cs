using System.Windows;

namespace CabiDock.Desktop;

/// <summary>A native accessibility item's name and physical screen-pixel bounds.</summary>
public sealed record DesktopAccessibleIcon(string Name, Rect ScreenBounds);

/// <summary>
/// Matches two complete desktop snapshots before planning visual suppression. This does not
/// remove items from Explorer's selection model or change any file or native icon position.
/// </summary>
public sealed class DesktopClipPlan
{
    private enum ItemKind { ManagedFile, PreservedFile, System }

    private DesktopClipPlan(IReadOnlyList<Rect> managed, IReadOnlyList<Rect> preserved)
    {
        ManagedScreenBounds = managed;
        PreservedScreenBounds = preserved;
    }

    /// <summary>Physical screen pixels, rounded outwards for an integer Win32 region.</summary>
    public IReadOnlyList<Rect> ManagedScreenBounds { get; }
    /// <summary>Native icons that group windows must keep accessible, in physical screen pixels.</summary>
    public IReadOnlyList<Rect> PreservedScreenBounds { get; }
    public int ManagedItemCount => ManagedScreenBounds.Count;

    public static bool TryCreate(IReadOnlyList<DesktopIconSnapshot> shellItems,
        IEnumerable<string> representedFilePaths, IReadOnlyList<DesktopAccessibleIcon> accessibleItems,
        out DesktopClipPlan? plan, out string reason)
    {
        plan = null;
        reason = "";
        if (shellItems.Count != accessibleItems.Count)
            return Fail("Shell 與原生圖示的項目數不一致；保留原生桌面。", out reason);

        var represented = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in representedFilePaths)
        {
            if (!TryNormalizePath(path, out var normalized))
                return Fail("群組含有無法確認的項目路徑；保留原生桌面。", out reason);
            represented.Add(normalized);
        }

        var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shellGroups = new Dictionary<string, (ItemKind Kind, int Count)>(StringComparer.Ordinal);
        foreach (var item in shellItems)
        {
            if (item is null || string.IsNullOrEmpty(item.Name))
                return Fail("Shell 圖示缺少名稱；保留原生桌面。", out reason);

            var kind = ItemKind.System;
            if (item.IsFileSystem)
            {
                kind = ItemKind.PreservedFile;
                if (TryNormalizePath(item.FileSystemPath, out var path) && represented.Contains(path))
                {
                    kind = ItemKind.ManagedFile;
                    matchedPaths.Add(path);
                }
            }
            if (shellGroups.TryGetValue(item.Name, out var group))
            {
                // Accessibility names do not identify paths. A duplicate is safe only when
                // every possible pairing has exactly the same treatment.
                if (group.Kind != kind)
                    return Fail("同名圖示無法唯一區分管理與保留項目；保留原生桌面。", out reason);
                shellGroups[item.Name] = (kind, group.Count + 1);
            }
            else shellGroups.Add(item.Name, (kind, 1));
        }

        if (!represented.SetEquals(matchedPaths))
            return Fail("群組與 Shell 的檔案清單尚未一致；保留原生桌面。", out reason);

        var accessibleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var managed = new List<Rect>();
        var preserved = new List<Rect>();
        foreach (var item in accessibleItems)
        {
            if (item is null || string.IsNullOrEmpty(item.Name)
                || !shellGroups.TryGetValue(item.Name, out var group))
                return Fail("無法對應原生圖示名稱；保留原生桌面。", out reason);
            if (!TryRoundBounds(item.ScreenBounds, out var bounds))
                return Fail("原生圖示範圍無效；保留原生桌面。", out reason);

            accessibleCounts.TryGetValue(item.Name, out var count);
            accessibleCounts[item.Name] = count + 1;
            if (group.Kind == ItemKind.ManagedFile) managed.Add(bounds);
            else preserved.Add(bounds);
        }

        foreach (var (name, group) in shellGroups)
            if (!accessibleCounts.TryGetValue(name, out var count) || count != group.Count)
                return Fail("同名原生圖示數量不一致；保留原生桌面。", out reason);

        foreach (var suppressed in managed)
            foreach (var visible in preserved)
                if (suppressed.Left < visible.Right && suppressed.Right > visible.Left
                    && suppressed.Top < visible.Bottom && suppressed.Bottom > visible.Top)
                    return Fail("受管理圖示與需保留的圖示重疊；保留原生桌面。", out reason);

        plan = new(managed.AsReadOnly(), preserved.AsReadOnly());
        return true;
    }

    private static bool TryNormalizePath(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryRoundBounds(Rect source, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (source.IsEmpty || !double.IsFinite(source.Left) || !double.IsFinite(source.Top)
            || !double.IsFinite(source.Right) || !double.IsFinite(source.Bottom)
            || source.Width <= 0 || source.Height <= 0
            || source.Left < int.MinValue || source.Top < int.MinValue
            || source.Right > int.MaxValue || source.Bottom > int.MaxValue)
            return false;
        var left = Math.Floor(source.Left);
        var top = Math.Floor(source.Top);
        bounds = new Rect(left, top, Math.Ceiling(source.Right) - left, Math.Ceiling(source.Bottom) - top);
        return true;
    }

    private static bool Fail(string message, out string reason)
    {
        reason = message;
        return false;
    }
}
