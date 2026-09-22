using System.IO;
using System.Windows;
using System.Windows.Input;
using MarkPad.Models;
using MarkPad.Services;

namespace MarkPad;

public partial class App : Application
{
    public static SettingsService Preferences { get; private set; } = null!;
    public static DocumentFileService Files { get; } = new();
    public static RecoveryService Recovery { get; private set; } = null!;
    private SingleInstanceService? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Preferences = new SettingsService();
            Recovery = new RecoveryService(Path.Combine(Preferences.DataDirectory, "recovery"));
            _instance = new SingleInstanceService();
            var newWindow = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var paths = ParsePaths(e.Args);
            if (!_instance.IsPrimary)
            {
                if (!await _instance.ForwardAsync(paths, newWindow))
                    MessageBox.Show("MarkPad is still starting. Please try opening the file again.", "MarkPad");
                Shutdown();
                return;
            }
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            ApplyPreferences();
            _instance.StartListening((files, separate) => Dispatcher.BeginInvoke(new Action(async () =>
            {
                var target = separate ? CreateWindow() : Windows.OfType<MainWindow>().LastOrDefault(w => w.IsActive)
                    ?? Windows.OfType<MainWindow>().FirstOrDefault() ?? CreateWindow();
                await target.OpenPathsAsync(ParsePaths(files));
                if (target.WindowState == WindowState.Minimized) target.WindowState = WindowState.Normal;
                target.Activate();
            })));
            var recoverable = await Recovery.RecoverAsync();
            if (recoverable.Count > 0)
            {
                var result = MessageBox.Show(window,
                    window.T("Recover unsaved documents from the previous session?", "要復原上次未儲存的文件嗎？", "前回の未保存の文書を復元しますか？"),
                    "MarkPad", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    foreach (var document in recoverable) window.AddDocument(document);
                else Recovery.Clear();
            }
            await window.OpenPathsAsync(paths);
        }
        catch (Exception ex)
        {
            LocalLog.Write(ex);
            MessageBox.Show(ex.Message, "MarkPad", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal static MainWindow CreateWindow()
    {
        var window = new MainWindow();
        window.Show();
        return window;
    }

    internal static IEnumerable<DocumentTab> AllDocuments =>
        Current.Windows.OfType<MainWindow>().SelectMany(w => w.Documents);

    internal static void ApplyPreferences()
    {
        try { Preferences.Save(); } catch (Exception ex) { LocalLog.Write(ex); }
        foreach (var window in Current.Windows.OfType<MainWindow>()) window.ApplyPreferences();
    }

    private static string[] ParsePaths(string[] args)
    {
        var result = new List<string>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("toolkeeper-markpad:", StringComparison.OrdinalIgnoreCase))
            {
                // Protocol launch opens the app. Files are only accepted as ordinary local arguments.
                continue;
            }
            if (!arg.StartsWith('-')) result.Add(arg);
        }
        return result.ToArray();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
