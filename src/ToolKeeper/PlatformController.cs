using System.IO;
using System.Windows;
using System.Windows.Threading;
using CabiDock;
using ToolKeeper.Services;
using ToolKeeper.UI;
using ToolKeeper.Modules;
using Forms = System.Windows.Forms;

namespace ToolKeeper;

/// <summary>Composes the platform and desktop module; desktop/Shell implementation stays in the module.</summary>
internal sealed class PlatformController : IDisposable
{
    private readonly Application _app;
    private readonly StartupOptions _options;
    private readonly ProductCatalogService _catalog = new();
    private readonly ProductLauncherService _launcher;
    private readonly ModuleWindowManager _modules = new();
    private MainWindow? _window;
    private HostWindowLifetime? _lifetime;
    private DesktopModule? _desktop;
    private DesktopPreferencesStore? _preferences;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private DispatcherTimer? _catalogTimer;
    private string? _actionError;
    private string? _protocolError;
    private bool _started, _exiting, _disposed;

    public PlatformController(Application app, StartupOptions options)
    {
        _app = app;
        _options = options;
        _launcher = new(_catalog, activateModule: ActivateBuiltIn);
    }

    public void Start()
    {
        if (_started || _exiting) return;
        try
        {
            _preferences = new(_options.PlatformDataDirectory);
            _window = CreateHomeWindow(Path.Combine(_options.PlatformDataDirectory, "ui.json"));
            _lifetime = new(_window);
            _app.MainWindow = _window;
            _desktop = new(_app.Dispatcher, _options.DesktopDataDirectory, _options.DesktopRoots);
            _desktop.SetEnabled(_preferences.Current.DesktopEnabled);
            _desktop.StateChanged += DesktopStateChanged;
            _window.ProductActivationRequested += id => ActivateUri(ToolActivationUri.ForProduct(id));
            _window.UiPreferencesChanged += (_, _) =>
            {
                UpdateTray();
                RefreshProducts();
                UpdateStatus();
            };
            _window.Activated += (_, _) => RefreshProducts();
            _trayIcon = PlatformIcon.CreateTrayIcon();
            _tray = new Forms.NotifyIcon
            {
                Text = "ToolKeeper - 工具番",
                Icon = _trayIcon,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };
            _tray.DoubleClick += (_, _) => _app.Dispatcher.Invoke(ShowHome);
            _tray.ContextMenuStrip.Opening += (_, _) => { RefreshProducts(); UpdateTray(); };
            UpdateTray();
            _catalogTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background,
                (_, _) => RefreshProducts(), _app.Dispatcher);
            _catalogTimer.Stop();
            _started = true;
            RefreshProducts();
            _desktop.Start();
            DesktopStateChanged();
            _tray.Visible = true;
            _catalogTimer.Start();
            if (_options.ActivationUri is null) ShowHome();
            else ActivateUri(_options.ActivationUri);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void ShowHome()
    {
        if (_exiting || _disposed) return;
        RefreshProducts();
        _lifetime?.Show();
    }

    internal static MainWindow CreateHomeWindow(string preferencesPath)
    {
        // URI cold-start may keep the home window hidden indefinitely. Load preferences
        // before an HWND exists so the tray and desktop group already use the saved language.
        var preferences = AppWindowPreferences.Load(preferencesPath);
        return new MainWindow
        {
            PreferencesPath = preferencesPath,
            SelectedTheme = preferences.Theme,
            SelectedLanguage = preferences.Language
        };
    }

    private void ToggleDesktop()
    {
        if (_exiting || _desktop is null) return;
        _desktop.SetEnabled(!_desktop.Enabled);
    }

    private void DesktopStateChanged()
    {
        if (_exiting || _desktop is null || _preferences is null) return;
        if (_preferences.Current.DesktopEnabled != _desktop.Enabled)
            _preferences.SetEnabled(_desktop.Enabled);
        UpdateTray();
        UpdateStatus();
    }

    private void UpdateTray()
    {
        if (_tray?.ContextMenuStrip is not { } menu || _exiting) return;
        // Build the menu once; changing language or status must not replace an open menu.
        if (menu.Items.Count == 0)
        {
            menu.Items.Add("", null, (_, _) => _app.Dispatcher.Invoke(ShowHome));
            menu.Items.Add("", null, (_, _) => _app.Dispatcher.Invoke(() => ActivateUri(ToolActivationUri.ForProduct("002"))));
            menu.Items.Add("", null, (_, _) => _app.Dispatcher.Invoke(ToggleDesktop));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("", null, (_, _) => _app.Dispatcher.Invoke(() => { PrepareExit(); _app.Shutdown(); }));
        }
        menu.Items[0].Text = T("Open ToolKeeper", "開啟工具番", "ToolKeeper を開く");
        menu.Items[1].Text = T("Desktop settings", "桌面設定", "デスクトップ設定");
        menu.Items[2].Text = _desktop?.Enabled == true
            ? T("Pause desktop organization", "暫停桌面整理", "デスクトップ整理を一時停止")
            : T("Enable desktop organization", "啟用桌面整理", "デスクトップ整理を有効化");
        menu.Items[4].Text = T("Exit", "結束程式", "終了");
    }

    private void RefreshProducts()
    {
        if (_exiting || _window is null || _desktop is null) return;
        _preferences?.RetrySave();
        UpdateStatus();
        var products = _catalog.Refresh();
        _window.SetProducts(products);
        _desktop.SetTools(products.Select(product => new DesktopTool(product.Id, product.Name,
            T(product.Product.DescriptionEnglish, product.Product.DescriptionChinese, product.Product.DescriptionJapanese),
            product.Availability switch
            {
                ProductAvailability.Available => T("Open", "開啟", "開く"),
                ProductAvailability.Get => T("Get", "取得", "入手"),
                _ => T("Unavailable", "未提供", "未提供")
            }, product.CanActivate, product.ActivationUri)).ToArray(), uri => ActivateUri(uri));
    }

    public bool ActivateUri(string? uri)
    {
        if (_exiting || !_started) return false;
        if (uri is null) { ShowHome(); return true; }
        if (!ToolActivationUri.TryParse(uri, out var productId)) return false;
        ActivateProduct(productId);
        return true; // Accepted by the owning host; any launch failure is displayed there.
    }

    public void SetProtocolError(string? error)
    {
        _protocolError = error is null ? null : T("Could not register tool links: ", "無法註冊工具啟動連結：", "ツールリンクを登録できません：") + error;
        UpdateStatus();
    }

    private void ActivateBuiltIn(string id)
    {
        switch (id)
        {
            case "002": _desktop!.ShowSettings(); break;
            case "004":
                _modules.Open(id, () => new HashCheckerWindow
                {
                    PreferencesPath = Path.Combine(_options.PlatformDataDirectory, "ui.json")
                });
                break;
            case "005":
                _modules.Open(id, () => new ImageToIcoWindow
                {
                    PreferencesPath = Path.Combine(_options.PlatformDataDirectory, "ui.json")
                });
                break;
            default: throw new ArgumentException("Unknown hosted module.", nameof(id));
        }
    }

    private void ActivateProduct(string productId)
    {
        if (_exiting) return;
        var result = _launcher.Launch(productId);
        _actionError = result.Succeeded ? null : result.Action == ProductLaunchAction.Unavailable
            ? T("This tool is not available on this computer yet.", "這項工具目前尚無可用的啟動或取得入口。", "このツールはまだ起動・入手できません。")
            : T("Could not open the tool: ", "無法開啟工具：", "ツールを開けません：") + result.Error;
        RefreshProducts();
        UpdateStatus();
        if (!result.Succeeded) ShowHome();
    }

    private string T(string english, string chinese, string japanese) =>
        UiLanguage.Text(_window?.ResolvedLanguage ?? UiLanguage.Resolve("System"), english, chinese, japanese);

    private void UpdateStatus() => _window?.SetPlatformStatus(string.Join(" · ",
        new[] { _preferences?.Warning, _protocolError, _actionError }.Where(text => !string.IsNullOrWhiteSpace(text))));

    public void PrepareExit()
    {
        if (_exiting) return;
        _exiting = true;
        _lifetime?.PrepareExit();
        _catalogTimer?.Stop();
        if (_tray is not null) _tray.Visible = false;
        _preferences?.RetrySave();
        _desktop?.PrepareExit();
        _modules.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        PrepareExit();
        _disposed = true;
        try { _desktop?.Dispose(); }
        finally
        {
            _catalogTimer?.Stop();
            var menu = _tray?.ContextMenuStrip;
            _tray?.Dispose();
            _trayIcon?.Dispose();
            menu?.Dispose();
            _lifetime?.Dispose();
            _window?.Close();
        }
    }
}
