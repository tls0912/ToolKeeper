using System.IO;
using System.Windows;
using ToolKeeper.UI;
using TransLamp.Core;

namespace TransLamp;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            var options = LaunchOptions.Parse(e.Args);
            var window = new MainWindow(
                new LanguagePackService(options.DataDirectory is null ? null : Path.Combine(options.DataDirectory, "LanguagePacks")),
                new TranslationEngine(options.RuntimeDirectory));
            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            var language = UiLanguage.Resolve("System");
            var message = error is ArgumentException
                ? UiLanguage.Text(language,
                    "Invalid launch arguments. Use translamp://open, --data-dir <path>, or --runtime-dir <path>.",
                    "啟動參數無效。請使用 translamp://open、--data-dir <路徑> 或 --runtime-dir <路徑>。",
                    "起動引数が無効です。translamp://open、--data-dir <パス>、--runtime-dir <パス> を使用してください。")
                : UiLanguage.Text(language,
                    "TransLamp could not start. Check access to its data folder and try again with a complete Offline Kit.",
                    "TransLamp 無法啟動，請檢查資料目錄的存取權限，並使用完整 Offline Kit 重試。",
                    "TransLamp を起動できません。データフォルダーのアクセス権を確認し、完全な Offline Kit で再試行してください。");
            MessageBox.Show(message, "TransLamp", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}

/// <summary>Paths can only be supplied as explicit local command-line options, never via activation URIs.</summary>
public sealed record LaunchOptions(string? DataDirectory, string? RuntimeDirectory)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? data = null, runtime = null;
        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            if (argument is "--data-dir" or "--runtime-dir")
            {
                if (++i >= args.Count || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing path for {argument}.");
                var path = Path.GetFullPath(args[i]);
                if (argument == "--data-dir") data = path;
                else runtime = path;
                continue;
            }

            if (Uri.TryCreate(argument, UriKind.Absolute, out var uri)
                && uri.Scheme.Equals("translamp", StringComparison.OrdinalIgnoreCase)
                && uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase)
                && uri.AbsolutePath is "" or "/"
                && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort
                && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
                continue;

            throw new ArgumentException("Unsupported launch argument. Use translamp://open, --data-dir <path>, or --runtime-dir <path>.");
        }
        return new LaunchOptions(data, runtime);
    }
}
