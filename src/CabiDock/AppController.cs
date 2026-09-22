using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CabiDock.Models;
using CabiDock.Services;
using CabiDock.Views;
using Forms = System.Windows.Forms;

namespace CabiDock;

internal sealed class AppController : IDisposable
{
    private readonly App _app;
    private readonly JsonFileStore<CabiDockConfiguration> _configurationFile;
    private readonly JsonFileStore<CabiDockState> _stateFile;
    private readonly DesktopScanner _scanner = new();
    private readonly DesktopWatcher _watcher = new();
    private readonly ClassificationService _classification = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _layoutTimer;
    private readonly SettingsWindow _settings;
    private readonly Forms.NotifyIcon _tray;
    private readonly IReadOnlyList<string>? _overrideRoots;
    private IReadOnlyList<string> _roots;
    private IReadOnlyList<DesktopItem> _items = [];
    private CabiDockConfiguration _configuration;
    private CabiDockState _state;
    private GroupPreviewWindow? _preview;
    private bool _canSave;
    private bool _pendingRules;
    private bool _stateDirty;
    private bool _scanning;
    private bool _rescanRequested;
    private bool _disposed;
    private long _version;
    private string? _loadWarning;
    private string? _saveError;
    private string? _lastStatus;

    public AppController(App app, string dataDirectory, IReadOnlyList<string>? roots)
    {
        _app = app;
        _overrideRoots = roots;
        _roots = roots ?? DesktopScanner.ResolveRoots();
        _configurationFile = new(Path.Combine(dataDirectory, "configuration.json"), ConfigurationService.ValidationError);
        _stateFile = new(Path.Combine(dataDirectory, "state.json"), StateService.ValidationError);
        var configuration = _configurationFile.Load(ConfigurationService.LoadDefaults);
        var state = _stateFile.Load(() => new CabiDockState());
        _configuration = ConfigurationService.Normalize(configuration.Value);
        _state = state.Value;
        _canSave = configuration.CanSave && state.CanSave;
        _loadWarning = string.Join("\n", new[] { configuration.Error, state.Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
        _pendingRules = _state.ConfigurationFingerprint is not null && _state.ConfigurationFingerprint != Fingerprint(_configuration);
        _settings = new SettingsWindow(_configuration);
        _settings.SaveRequested += SaveSettings;
        _settings.PreviewRequested += (_, _) => ShowPreview();
        _app.MainWindow = _settings;
        _tray = new Forms.NotifyIcon
        {
            Text = "CabiDock｜開發預覽",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("設定", null, (_, _) => _app.Dispatcher.Invoke(ShowSettings));
        _tray.ContextMenuStrip.Items.Add("結束程式", null, (_, _) => _app.Dispatcher.Invoke(() => _app.Shutdown()));
        _tray.DoubleClick += (_, _) => _app.Dispatcher.Invoke(ShowSettings);
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            async (_, _) => await RefreshAsync(), _app.Dispatcher);
        _refreshTimer.Stop();
        _layoutTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            (_, _) => { _layoutTimer!.Stop(); SaveState(); }, _app.Dispatcher);
        _layoutTimer.Stop();
        _watcher.Changed += change => _app.Dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _watcher.Error += message => _app.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                _lastStatus = null;
                _settings.SetStatus("桌面監看暫時中斷，將重新掃描並保留既有分類。\n" + message, true);
            }
        });
    }

    public void Start()
    {
        _tray.Visible = true;
        ShowSettings();
        _watcher.Start(_roots);
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    public void ShowSettings()
    {
        if (_disposed) return;
        _settings.Show();
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    private void ShowPreview()
    {
        if (_preview is null)
        {
            _preview = new GroupPreviewWindow();
            _preview.ItemOpenRequested += OpenItem;
            _preview.ManualAssignmentRequested += AssignManually;
            _preview.LayoutChanged += (categoryId, layout) =>
            {
                _state.Groups[categoryId] = layout;
                _stateDirty = true;
                _layoutTimer.Stop();
                _layoutTimer.Start();
            };
        }
        _preview.SetItems(_configuration, _state, _items);
        _stateDirty = true; // Initial layouts also need persistence.
        SaveState();
        _preview.Show();
        if (_preview.WindowState == WindowState.Minimized) _preview.WindowState = WindowState.Normal;
        _preview.Activate();
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        if (_scanning) { _rescanRequested = true; return; }
        _scanning = true;
        var version = _version;
        try
        {
            var roots = _overrideRoots ?? DesktopScanner.ResolveRoots();
            if (!_roots.SequenceEqual(roots, StringComparer.OrdinalIgnoreCase) || _watcher.NeedsRestart)
            {
                _roots = roots;
                _watcher.Start(roots);
            }
            var result = await Task.Run(() => _scanner.Scan(roots));
            if (_disposed) return;
            if (version != _version) { _rescanRequested = true; return; }
            if (!result.Succeeded)
            {
                _lastStatus = null;
                _settings.SetStatus("桌面掃描未完成，已保留原有分類清單。\n" + result.Error, true);
                return;
            }
            var itemsChanged = !SameItems(_items, result.Items);
            _items = result.Items;
            _stateDirty |= _classification.Reconcile(_items, _state, _configuration, rulesChanged: _pendingRules);
            var fingerprint = Fingerprint(_configuration);
            _stateDirty |= _state.ConfigurationFingerprint != fingerprint;
            _state.ConfigurationFingerprint = fingerprint;
            _pendingRules = false;
            if (itemsChanged || _stateDirty) _preview?.SetItems(_configuration, _state, _items);
            SaveState();
            ShowStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _lastStatus = null;
            _settings.SetStatus("無法更新桌面分類，既有紀錄保留。\n" + ex.Message, true);
        }
        finally
        {
            _scanning = false;
            if (_rescanRequested && !_disposed)
            {
                _rescanRequested = false;
                _ = _app.Dispatcher.BeginInvoke(() => _ = RefreshAsync(), DispatcherPriority.Background);
            }
        }
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (_disposed) return;
        ++_version;
        // Process removal immediately: move out and back before a rescan must lose its old assignment.
        if (change.Kind == WatcherChangeTypes.Deleted)
            _stateDirty |= _classification.Remove(change.FullPath, _state);
        else if (change.Kind == WatcherChangeTypes.Renamed && change.OldFullPath is not null)
            _stateDirty |= _classification.Rename(change.OldFullPath, new DesktopItem(change.FullPath, false), _state);
        SaveState();
        _ = RefreshAsync();
    }

    private async void SaveSettings(object? sender, SettingsSaveRequestedEventArgs e)
    {
        if (!_canSave)
        {
            e.ErrorMessage = "原有設定或狀態無法安全讀取；請先查看檔案錯誤，再重新啟動。現有檔案未被覆寫。";
            return;
        }
        var validation = ConfigurationService.Validate(e.Configuration);
        if (validation.Count > 0) { e.ErrorMessage = string.Join("\n", validation); return; }
        var configuration = ConfigurationService.Normalize(e.Configuration);
        var saved = _configurationFile.Save(configuration);
        if (!saved.Success) { e.ErrorMessage = saved.Error; return; }
        _configuration = configuration;
        _lastStatus = null;
        // A different fingerprint in the saved state makes interrupted rule updates recoverable.
        ++_version;
        _pendingRules = true;
        _preview?.SetItems(_configuration, _state, _items, rebuild: true);
        await RefreshAsync();
    }

    private void AssignManually(DesktopItem item, string categoryId)
    {
        if (!_canSave) { _settings.SetStatus("無法安全保存分類，請先修復狀態檔案。", true); ShowSettings(); return; }
        if (!_items.Any(value => string.Equals(value.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))) return;
        _stateDirty |= _classification.AssignManually(item, categoryId, _state, _configuration);
        SaveState();
        _preview?.SetItems(_configuration, _state, _items);
        ShowStatus();
    }

    private void OpenItem(DesktopItem item)
    {
        try { Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(_preview, "無法開啟此項目。\n\n" + ex.Message, "CabiDock", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SaveState()
    {
        if (!_stateDirty || !_canSave) return;
        var saved = _stateFile.Save(_state);
        _saveError = saved.Error;
        if (saved.Success) _stateDirty = false;
        else
        {
            _lastStatus = null;
            _settings.SetStatus("分類尚未儲存，將自動重試。\n" + saved.Error, true);
        }
    }

    private void ShowStatus()
    {
        var status = $"已掃描 {_items.Count} 個桌面項目 · {_configuration.Categories.Count} 個分類\n開發預覽：原生桌面圖示保留。可開啟群組預覽操作，桌面接管尚未啟用。";
        if (!string.IsNullOrWhiteSpace(_loadWarning)) status += "\n" + _loadWarning;
        if (_saveError is not null) status += "\n分類尚未儲存：" + _saveError;
        // Idle polling must not erase a validation error the user is currently correcting.
        if (_lastStatus == status) return;
        _lastStatus = status;
        _settings.SetStatus(status, !_canSave || _saveError is not null);
    }

    private static string Fingerprint(CabiDockConfiguration configuration) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(configuration, JsonFileStore<CabiDockConfiguration>.SerializerOptions)));

    private static bool SameItems(IReadOnlyList<DesktopItem> first, IReadOnlyList<DesktopItem> second) =>
        first.Count == second.Count && first.Zip(second).All(pair => pair.First.FullPath == pair.Second.FullPath
            && pair.First.Identity == pair.Second.Identity && pair.First.IsDirectory == pair.Second.IsDirectory);

    public void PrepareExit()
    {
        _settings.AllowClose = true;
        if (_preview is not null) _preview.AllowClose = true;
        _layoutTimer.Stop();
        SaveState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        PrepareExit();
        _disposed = true;
        _watcher.Dispose();
        _refreshTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _preview?.Close();
        _settings.Close();
    }
}
