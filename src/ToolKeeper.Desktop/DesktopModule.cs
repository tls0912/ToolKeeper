using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CabiDock.Models;
using CabiDock.Services;
using CabiDock.Views;
using CabiDock.Desktop;


namespace CabiDock;

public sealed class DesktopModule : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly JsonFileStore<CabiDockConfiguration> _configurationFile;
    private readonly JsonFileStore<CabiDockState> _stateFile;
    private readonly Func<IReadOnlyList<string>, Task<DesktopScanResult>> _scan;
    private readonly DesktopWatcher _watcher = new();
    private readonly ClassificationService _classification = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _changeTimer;
    private readonly DispatcherTimer _layoutTimer;
    private readonly SettingsWindow _settings;
    private readonly DesktopTakeoverService? _desktop;
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
    private bool _started;
    private bool _stopping;
    private bool _enabled = true;
    private IReadOnlyList<DesktopTool> _tools = [];
    private Action<string>? _activateTool;
    private long _version;
    private string? _loadWarning;
    private string? _saveError;
    private string? _lastStatus;

    public bool Enabled => _enabled;
    public bool SupportsDesktop => _desktop is not null;
    public event Action? StateChanged;

    public DesktopModule(Dispatcher dispatcher, string dataDirectory, IReadOnlyList<string>? roots = null)
        : this(dispatcher, dataDirectory, roots, scanRoots => Task.Run(() => new DesktopScanner().Scan(scanRoots)))
    {
    }

    internal DesktopModule(Dispatcher dispatcher, string dataDirectory, IReadOnlyList<string>? roots,
        Func<IReadOnlyList<string>, Task<DesktopScanResult>> scan)
    {
        _dispatcher = dispatcher;
        _dispatcher.VerifyAccess();
        _scan = scan;
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
        _settings = new SettingsWindow(_configuration)
        {
            PreferencesPath = Path.Combine(dataDirectory, "ui-preferences.json")
        };
        _settings.SaveRequested += SaveSettings;
        _settings.PreviewRequested += (_, _) => ShowPreview();
        if (roots is null)
        {
            _desktop = new DesktopTakeoverService(_dispatcher);
            _desktop.ItemOpenRequested += OpenItem;
            _desktop.ManualAssignmentRequested += AssignManually;
            _desktop.LayoutChanged += SaveLayout;
            _desktop.StatusChanged += () =>
            {
                _settings.SetDesktopState(_desktop.Enabled);
                ShowStatus();
                StateChanged?.Invoke();
            };
            _settings.DesktopToggleRequested += (_, _) => SetEnabled(!Enabled);
        }
        _settings.SetDesktopState(_desktop?.Enabled == true, _desktop is not null);
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            async (_, _) => await RefreshAsync(), _dispatcher);
        _refreshTimer.Stop();
        _changeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background,
            async (_, _) => { _changeTimer!.Stop(); await RefreshAsync(); }, _dispatcher);
        _changeTimer.Stop();
        _layoutTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            (_, _) => { _layoutTimer!.Stop(); SaveState(); }, _dispatcher);
        _layoutTimer.Stop();
        _watcher.Changed += change => _dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _watcher.Error += message => _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && !_stopping)
            {
                _lastStatus = null;
                _settings.SetLocalizedStatus("Desktop monitoring was interrupted. Existing categories will be retained during a rescan.\n" + message,
                    "桌面監看暫時中斷，將重新掃描並保留既有分類。\n" + message,
                    "デスクトップの監視が中断しました。分類を保持して再スキャンします。\n" + message, true);
            }
        });
    }

    public void Start(bool showSettings = false)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || _stopping || _started) return;
        _started = true;
        _desktop?.SetEnabled(_enabled);
        if (showSettings) ShowSettings();
        _watcher.Start(_roots);
        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    public void ShowSettings()
    {
        _dispatcher.VerifyAccess();
        if (_disposed || _stopping) return;
        _settings.Show();
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    public void SetEnabled(bool enabled)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || _stopping) return;
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (enabled) _preview?.Hide();
        if (_started) _desktop?.SetEnabled(enabled);
        _settings.SetDesktopState(enabled && SupportsDesktop, SupportsDesktop);
        StateChanged?.Invoke();
    }

    /// <summary>Supplies platform entries independently of desktop files and classification.</summary>
    public void SetTools(IReadOnlyList<DesktopTool> tools, Action<string> activateTool)
    {
        _dispatcher.VerifyAccess();
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(activateTool);
        if (_disposed || _stopping) return;
        if (tools.Any(tool => string.IsNullOrWhiteSpace(tool.Id)) || tools.Select(tool => tool.Id).Distinct(StringComparer.Ordinal).Count() != tools.Count)
            throw new ArgumentException("Tool IDs must be nonempty and unique.", nameof(tools));
        _tools = tools.ToArray();
        _activateTool = activateTool;
        _desktop?.SetTools(_tools, ActivateTool);
        _preview?.SetTools(_tools, ActivateTool);
        if (_preview is not null) _preview.SetItems(_configuration, _state, _items);
    }

    private void ActivateTool(string activationUri)
    {
        if (!_disposed && !_stopping && !string.IsNullOrWhiteSpace(activationUri)
            && _tools.Any(tool => tool.ActivationUri == activationUri && tool.CanActivate)) _activateTool?.Invoke(activationUri);
    }

    private void ShowPreview()
    {
        if (_disposed || _stopping) return;
        SetEnabled(false);
        if (_preview is null)
        {
            _preview = new GroupPreviewWindow();
            _preview.ItemOpenRequested += OpenItem;
            _preview.ManualAssignmentRequested += AssignManually;
            _preview.LayoutChanged += SaveLayout;
            _preview.SetTools(_tools, ActivateTool);
        }
        _preview.SetItems(_configuration, _state, _items);
        _stateDirty = true; // Initial layouts also need persistence.
        SaveState();
        _preview.Show();
        if (_preview.WindowState == WindowState.Minimized) _preview.WindowState = WindowState.Normal;
        _preview.Activate();
    }

    private void SaveLayout(string categoryId, GroupLayout layout)
    {
        if (_disposed || _stopping) return;
        _state.Groups[categoryId] = layout;
        _stateDirty = true;
        _layoutTimer.Stop();
        _layoutTimer.Start();
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _stopping) return;
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
            var result = await _scan(roots);
            if (_disposed || _stopping) return;
            if (version != _version) { _rescanRequested = true; return; }
            if (!result.Succeeded)
            {
                _desktop?.Suspend("桌面掃描未完成，已恢復原生圖示。");
                _lastStatus = null;
                _settings.SetLocalizedStatus("Desktop scan did not finish. Existing categories are retained.\n" + result.Error,
                    "桌面掃描未完成，已保留原有分類清單。\n" + result.Error,
                    "デスクトップのスキャンが完了しませんでした。既存の分類は保持されています。\n" + result.Error, true);
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
            _desktop?.Update(_configuration, _state, _items);
            SaveState();
            ShowStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (_disposed || _stopping) return;
            _desktop?.Suspend("無法更新桌面分類，已恢復原生圖示。");
            _lastStatus = null;
            _settings.SetLocalizedStatus("Could not update desktop categories. Existing records are retained.\n" + ex.Message,
                "無法更新桌面分類，既有紀錄保留。\n" + ex.Message,
                "デスクトップの分類を更新できませんでした。既存の記録は保持されています。\n" + ex.Message, true);
        }
        finally
        {
            _scanning = false;
            if (_rescanRequested && !_disposed && !_stopping)
            {
                _rescanRequested = false;
                _ = _dispatcher.BeginInvoke(() => _ = RefreshAsync(), DispatcherPriority.Background);
            }
        }
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (_disposed || _stopping) return;
        ++_version;
        // Process removal immediately: move out and back before a rescan must lose its old assignment.
        if (change.Kind == WatcherChangeTypes.Deleted)
            _stateDirty |= _classification.Remove(change.FullPath, _state);
        else if (change.Kind == WatcherChangeTypes.Renamed && change.OldFullPath is not null)
            _stateDirty |= _classification.Rename(change.OldFullPath, new DesktopItem(change.FullPath, false), _state);
        // Rename/copy operations commonly raise several notifications. Preserve the attached
        // groups while one scan reconciles the burst; native geometry is watched separately.
        _changeTimer.Stop();
        _changeTimer.Start();
    }

    private async void SaveSettings(object? sender, SettingsSaveRequestedEventArgs e)
    {
        if (_disposed || _stopping) return;
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
        var classificationChanged = Fingerprint(_configuration) != Fingerprint(configuration);
        _configuration = configuration;
        _lastStatus = null;
        if (!classificationChanged)
        {
            _preview?.SetItems(_configuration, _state, _items);
            _desktop?.Update(_configuration, _state, _items);
            ShowStatus();
            return;
        }
        // A different fingerprint in the saved state makes interrupted rule updates recoverable.
        ++_version;
        _pendingRules = true;
        _preview?.SetItems(_configuration, _state, _items, rebuild: true);
        await RefreshAsync();
    }

    private void AssignManually(DesktopItem item, string categoryId)
    {
        if (_disposed || _stopping) return;
        if (!_canSave)
        {
            _settings.SetLocalizedStatus("Categories cannot be saved safely. Repair the state file first.",
                "無法安全保存分類，請先修復狀態檔案。", "分類を安全に保存できません。先に状態ファイルを修復してください。", true);
            ShowSettings(); return;
        }
        if (!_items.Any(value => string.Equals(value.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))) return;
        _stateDirty |= _classification.AssignManually(item, categoryId, _state, _configuration);
        SaveState();
        _preview?.SetItems(_configuration, _state, _items);
        _desktop?.Update(_configuration, _state, _items);
        ShowStatus();
    }

    private void OpenItem(DesktopItem item)
    {
        try { Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            System.Windows.MessageBox.Show("無法開啟此項目。\n\n" + ex.Message, "CabiDock", MessageBoxButton.OK, MessageBoxImage.Information);
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
            _settings.SetLocalizedStatus("Categories have not been saved. Saving will be retried automatically.\n" + saved.Error,
                "分類尚未儲存，將自動重試。\n" + saved.Error,
                "分類はまだ保存されていません。自動的に再試行します。\n" + saved.Error, true);
        }
    }

    private void ShowStatus()
    {
        var status = $"已掃描 {_items.Count} 個桌面項目 · {_configuration.Categories.Count} 個分類\n"
            + (_desktop?.Status ?? "自訂目錄預覽：保留真實桌面圖示。");
        if (!string.IsNullOrWhiteSpace(_loadWarning)) status += "\n" + _loadWarning;
        if (_saveError is not null) status += "\n分類尚未儲存：" + _saveError;
        // Idle polling must not erase a validation error the user is currently correcting.
        if (_lastStatus == status) return;
        _lastStatus = status;
        var details = string.Join("\n", new[] { _desktop?.Status, _loadWarning }.Where(value => !string.IsNullOrWhiteSpace(value)));
        _settings.SetLocalizedStatus(
            $"Scanned {_items.Count} desktop items · {_configuration.Categories.Count} categories\n"
                + (_desktop is null ? "Custom folder preview: native desktop icons are preserved.\n" : "") + details
                + (_saveError is null ? "" : "\nCategories have not been saved: " + _saveError),
            status,
            $"デスクトップ項目 {_items.Count} 件をスキャン · 分類 {_configuration.Categories.Count} 個\n"
                + (_desktop is null ? "指定フォルダーのプレビュー：標準デスクトップアイコンを保持します。\n" : "") + details
                + (_saveError is null ? "" : "\n分類はまだ保存されていません：" + _saveError),
            !_canSave || _saveError is not null);
    }

    private static string Fingerprint(CabiDockConfiguration configuration) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { configuration.Categories, configuration.KeywordRules }, JsonFileStore<CabiDockConfiguration>.SerializerOptions)));

    private static bool SameItems(IReadOnlyList<DesktopItem> first, IReadOnlyList<DesktopItem> second) =>
        first.Count == second.Count && first.Zip(second).All(pair => pair.First.FullPath == pair.Second.FullPath
            && pair.First.Identity == pair.Second.Identity && pair.First.IsDirectory == pair.Second.IsDirectory);

    public void PrepareExit()
    {
        _dispatcher.VerifyAccess();
        if (_stopping) return;
        _stopping = true;
        _refreshTimer.Stop();
        _changeTimer.Stop();
        _watcher.Dispose();
        _desktop?.Dispose();
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
        _refreshTimer.Stop();
        _changeTimer.Stop();
        _preview?.Close();
        _settings.Close();
    }
}
