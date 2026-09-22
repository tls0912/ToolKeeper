using System.Text.Json;
using System.Windows;
using CabiDock.Desktop;

namespace CabiDock;

public partial class App : System.Windows.Application
{
    private SingleInstance? _instance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var options = ParseArguments(e.Args);
            if (options.TryGetValue("--diagnose-desktop", out var reportPath))
            {
                var report = DesktopProbe.Capture();
                File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown();
                return;
            }
            var dataDirectory = options.GetValueOrDefault("--data-directory")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper", "CabiDock");
            dataDirectory = Path.GetFullPath(dataDirectory);
            _instance = new SingleInstance(dataDirectory);
            if (!_instance.IsPrimary)
            {
                _instance.Signal();
                Shutdown();
                return;
            }
            IReadOnlyList<string>? roots = options.TryGetValue("--desktop-directory", out var directory)
                ? [Path.GetFullPath(directory)] : null;
            _controller = new AppController(this, dataDirectory, roots);
            _instance.Listen(() => Dispatcher.BeginInvoke(() => _controller?.ShowSettings()));
            _controller.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"CabiDock 無法啟動。\n\n{ex.Message}", "CabiDock", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var key = args[index];
            if (key is not ("--diagnose-desktop" or "--data-directory" or "--desktop-directory")
                || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException($"不支援的啟動參數：{key}");
            if (!result.TryAdd(key, args[index + 1])) throw new ArgumentException($"重複的啟動參數：{key}");
        }
        return result;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _controller?.PrepareExit();
        e.Cancel = false;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
