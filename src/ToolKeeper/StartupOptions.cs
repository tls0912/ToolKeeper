using System.IO;
using ToolKeeper.Services;

namespace ToolKeeper;

internal sealed record StartupOptions(string DesktopDataDirectory, string PlatformDataDirectory,
    IReadOnlyList<string>? DesktopRoots, string? DiagnosticPath, bool DiagnoseOpacity,
    string? ActivationUri = null, bool RegisterProtocol = true)
{
    internal static StartupOptions Parse(string[] args)
    {
        // A Shell URI may contain raw quotes. Its registered command must never allow
        // expanded argv to smuggle data-directory, desktop or diagnostic options.
        if (args.Contains("--protocol-activate", StringComparer.Ordinal))
        {
            if (args.Length != 2 || args[0] != "--protocol-activate" || !ToolActivationUri.TryParse(args[1], out _))
                throw new ArgumentException("外部工具啟動連結只能指定一個已登錄的模組。");
            args = ["--activate", args[1]];
        }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? activationUri = null;
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (ToolActivationUri.TryParse(key, out var directId))
            {
                if (activationUri is not null) throw new ArgumentException("一次只能指定一個模組啟動入口。");
                activationUri = ToolActivationUri.ForProduct(directId);
                continue;
            }
            if (key == "--activate")
            {
                if (activationUri is not null || ++index >= args.Length || !ToolActivationUri.TryParse(args[index], out var id))
                    throw new ArgumentException("無效或重複的工具番啟動連結。");
                activationUri = ToolActivationUri.ForProduct(id);
                continue;
            }
            if (key is not ("--data-directory" or "--desktop-directory" or "--diagnose-desktop" or "--diagnose-group-opacity")
                || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("不支援或缺少值的啟動參數：" + key);
            if (!values.TryAdd(key, Path.GetFullPath(args[++index])))
                throw new ArgumentException("重複的啟動參數：" + key);
        }
        if (values.ContainsKey("--diagnose-desktop") && values.ContainsKey("--diagnose-group-opacity"))
            throw new ArgumentException("一次只能指定一種診斷模式。");
        if (activationUri is not null && (values.ContainsKey("--diagnose-desktop") || values.ContainsKey("--diagnose-group-opacity")))
            throw new ArgumentException("診斷模式不能同時啟動工具。");
        var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper");
        var data = values.GetValueOrDefault("--data-directory") ?? Path.Combine(localRoot, "CabiDock");
        // An explicit test data directory must also isolate all platform and UI preferences.
        var platform = values.ContainsKey("--data-directory") ? Path.Combine(data, "toolkeeper-host") : Path.Combine(localRoot, "ToolKeeper");
        return new(data, platform,
            values.TryGetValue("--desktop-directory", out var root) ? [root] : null,
            values.GetValueOrDefault("--diagnose-desktop") ?? values.GetValueOrDefault("--diagnose-group-opacity"),
            values.ContainsKey("--diagnose-group-opacity"), activationUri,
            !values.ContainsKey("--data-directory") && !values.ContainsKey("--desktop-directory"));
    }
}
