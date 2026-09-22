using System.IO;
using CabiDock.Models;

namespace CabiDock.Services;

public static class StateService
{
    public static string? ValidationError(CabiDockState state)
    {
        if (state.Items is null || state.Groups is null)
            return "狀態缺少項目清單或群組配置。";
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in state.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.FullPath) || !Path.IsPathFullyQualified(item.FullPath)
                || !paths.Add(item.FullPath) || string.IsNullOrWhiteSpace(item.CategoryId) || !Enum.IsDefined(item.Source))
                return "狀態含有無效或重複的項目紀錄。";
        }
        foreach (var (id, layout) in state.Groups)
        {
            if (string.IsNullOrWhiteSpace(id) || layout is null || !double.IsFinite(layout.X) || !double.IsFinite(layout.Y)
                || !double.IsFinite(layout.Width) || !double.IsFinite(layout.Height) || layout.Width <= 0 || layout.Height <= 0)
                return "狀態含有無效的群組位置或大小。";
        }
        return null;
    }
}
