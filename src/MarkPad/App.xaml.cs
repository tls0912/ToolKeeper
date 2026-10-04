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
    internal static DocumentSessionService Session { get; } = new();
    private SingleInstanceService? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var newWindow = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            var paths = ParsePaths(e.Args);
            Preferences = new SettingsService();
            var licensing = new StoreLicenseService();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!licensing.CanStartWithoutStore && !await new StartupLicenseWindow(Preferences.Settings).CheckAsync(licensing))
            {
                Shutdown();
                return;
            }
            Recovery = new RecoveryService(Path.Combine(Preferences.DataDirectory, "recovery"));
            _instance = new SingleInstanceService(licensing.InstanceName);
            if (!_instance.IsPrimary)
            {
                if (!await _instance.ForwardAsync(paths, newWindow))
                    MessageBox.Show(ToolKeeper.UI.UiLanguage.Text(Preferences.Settings.ResolveLanguage(System.Globalization.CultureInfo.CurrentUICulture.Name),
                        "汗青 is still starting. Please try opening the file again.", "汗青仍在啟動中，請稍後再開啟檔案。", "汗青は起動中です。しばらくしてからもう一度ファイルを開いてください。"), "汗青");
                Shutdown();
                return;
            }
            Session.Reset();
            var window = new MainWindow(deferEmptyState: true);
            MainWindow = window;
            window.Show();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
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
                    "汗青", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    foreach (var document in recoverable) window.AddDocument(document);
                else Recovery.Clear();
            }
            await window.RestoreSessionAsync();
            await window.OpenPathsAsync(paths);
            window.CompleteStartup();
        }
        catch (Exception ex)
        {
            LocalLog.Write(ex);
            MessageBox.Show(ex.Message, "汗青", MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (!Preferences.Settings.RememberOpenFiles) Session.Reset();
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
