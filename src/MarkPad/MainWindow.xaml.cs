using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MarkPad.Editing;
using MarkPad.Models;
using MarkPad.Rendering;
using MarkPad.Services;

namespace MarkPad;

public partial class MainWindow : Window
{
    public List<DocumentTab> Documents { get; } = [];
    private DocumentTab? _current;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _maintenance = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _autoSaveTimer = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<Guid, DateTime> _modified = [];
    private readonly Dictionary<Guid, int> _previewSourceLines = [];
    private readonly HashSet<Guid> _saving = [];
    private readonly HashSet<Guid> _closingDocuments = [];
    private readonly HashSet<string> _opening = new(StringComparer.OrdinalIgnoreCase);
    private bool _checking, _closing, _allowClose, _disposed, _previewOverlay;
    private bool _autoSaveRunning;
    private bool _dark;
    private int _previewLine = 1;
    private AppSettings Settings => App.Preferences.Settings;
    private string UiLanguage => Settings.ResolveLanguage(CultureInfo.CurrentUICulture.Name);

    public MainWindow()
    {
        InitializeComponent();
        if (Settings.RememberWindowSize && Settings.WindowHeight > 0)
        {
            Width = Math.Clamp(Settings.WindowWidth, MinWidth, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
            Height = Math.Clamp(Settings.WindowHeight, MinHeight, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
            if (Settings.WindowMaximized) WindowState = WindowState.Maximized;
        }
        else WindowState = WindowState.Maximized;
        _renderTimer.Tick += async (_, _) => { _renderTimer.Stop(); await GuardAsync(() => RenderAsync()); };
        _maintenance.Tick += async (_, _) => await MaintainAsync();
        _autoSaveTimer.Tick += async (_, _) => await AutoSaveDueAsync();
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); StatusToast.Visibility = Visibility.Collapsed; };
        PreviewKeyDown += OnKeyDown;
        DocumentHost.PreviewMouseWheel += OnContentMouseWheel;
        PreviewDragOver += OnDragOver;
        Drop += async (_, e) => await GuardAsync(() => DropAsync(e));
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _disposed = true;
            _maintenance.Stop(); _autoSaveTimer.Stop(); _renderTimer.Stop(); _toastTimer.Stop(); _fullScreenTimer.Stop();
            foreach (var view in _documentViews.Values.ToArray()) ReleaseDocumentView(view.Document);
            _currentView = null;
            foreach (var tab in Documents) tab.PropertyChanged -= TabChanged;
            SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        };
        Loaded += (_, _) => { ApplyPreferences(); BuildTabs(); _maintenance.Start(); };
        Activated += async (_, _) => await MaintainAsync();
        SizeChanged += (_, _) => BuildTabs();
        // A larger UI font widens the sidebar; keep every search control inside the document area.
        ContentFrame.SizeChanged += (_, _) => SearchPanel.MaxWidth = Math.Max(0,
            ContentFrame.ActualWidth - ContentFrame.BorderThickness.Left - ContentFrame.BorderThickness.Right
            - SearchPanel.Margin.Left - SearchPanel.Margin.Right);
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        WindowCloseButton.Click += (_, _) => Close();
        EmptyOpenButton.Click += async (_, _) => await GuardAsync(OpenDialogAsync);
        OverflowButton.Click += (_, _) => ShowTabList();
        SetupSearch(); SetupRail(); SetupFullScreen();
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        ApplyPreferences();
    }

    public string T(string english, string chinese, string japanese) => UiLanguage switch { "zh-TW" => chinese, "ja" => japanese, _ => english };
    private Brush B(string key) => (Brush)FindResource(key);
    private static string Canonical(string path) => Path.GetFullPath(path);

    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        foreach (var value in paths)
        {
            if (_disposed || _closing) return;
            string? path = null;
            var ownsOpenRequest = false;
            try
            {
                path = Canonical(value);
                if (!File.Exists(path)) { Toast(T("File not found", "找不到檔案", "ファイルが見つかりません") + ": " + path); continue; }
                var existing = Application.Current.Windows.OfType<MainWindow>()
                    .SelectMany(w => w.Documents.Select(d => (Window: w, Tab: d)))
                    .FirstOrDefault(item => string.Equals(item.Tab.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (existing.Tab is not null)
                {
                    existing.Window.SelectDocument(existing.Tab);
                    existing.Window.Activate();
                    continue;
                }
                if (!(ownsOpenRequest = _opening.Add(path))) continue;
                var tab = await App.Files.OpenAsync(path);
                if (_disposed || _closing) return;
                // Another window may finish opening this same file while this read is in flight.
                existing = Application.Current.Windows.OfType<MainWindow>()
                    .SelectMany(w => w.Documents.Select(d => (Window: w, Tab: d)))
                    .FirstOrDefault(item => string.Equals(item.Tab.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (existing.Tab is not null)
                {
                    existing.Window.SelectDocument(existing.Tab);
                    existing.Window.Activate();
                    continue;
                }
                AddDocument(tab);
                App.Preferences.AddRecent(path);
                if (tab.IsLargeFile) Toast(T("Large file mode — preview renders on request", "大型檔案模式：切換預覽時才轉譯", "大きなファイル：プレビューは切替時に描画"));
            }
            catch (Exception ex) { Report(ex); }
            finally { if (ownsOpenRequest && path is not null) _opening.Remove(path); }
        }
    }

    public void AddDocument(DocumentTab tab)
    {
        if (!Documents.Contains(tab))
        {
            Documents.Add(tab);
            tab.PropertyChanged += TabChanged;
        }
        SelectDocument(tab);
    }

    private void TabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentTab tab || _disposed) return;
        if (e.PropertyName == nameof(DocumentTab.Content)) ContentModified(tab);
        if (e.PropertyName is nameof(DocumentTab.IsDirty) or nameof(DocumentTab.IsMissing) or nameof(DocumentTab.IsReadOnly)) ScheduleAutoSave();
        if (e.PropertyName is nameof(DocumentTab.Title) or nameof(DocumentTab.FilePath)) BuildTabs();
        if (ReferenceEquals(tab, _current)) UpdateStatus();
    }

    private void ContentModified(DocumentTab tab)
    {
        _modified[tab.Id] = DateTime.UtcNow;
        ScheduleAutoSave();
        if (ReferenceEquals(tab, _current) && (tab.IsPreviewMode || !tab.IsLargeFile))
        { _renderTimer.Stop(); _renderTimer.Start(); }
    }

    private void SelectDocument(DocumentTab? tab, int? sourceLine = null)
    {
        if (_disposed) return;
        _renderTimer.Stop();
        if (!ReferenceEquals(tab, _current)) _currentView?.SetActive(false);
        _current = tab;
        _currentView = tab is null ? null : GetDocumentView(tab);
        _currentView?.SetActive(true);
        _previewOverlay = _currentView?.PreviewOverlay == true;
        _previewLine = tab is null ? 1 : _previewSourceLines.GetValueOrDefault(tab.Id, 1);
        if (tab is not null) tab.LastActivatedUtc = DateTime.UtcNow;
        if (_currentView is not null && !SearchPanel.IsVisible) _editor.Find("", Settings.MatchCase);
        EmptyState.Visibility = tab is null ? Visibility.Visible : Visibility.Collapsed;
        if (tab is null) SearchPanel.Visibility = Visibility.Collapsed;
        if (tab?.IsPreviewMode != false) ReplacePanel.Visibility = Visibility.Collapsed;
        ExpandReplaceButton.Visibility = tab?.IsPreviewMode == false ? Visibility.Visible : Visibility.Collapsed;
        BuildTabs(); BuildActions(); UpdateStatus();
        if (tab?.IsPreviewMode == true) _ = GuardAsync(() => RenderAsync(sourceLine));
        else if (tab is not null) { _editor.FocusEditor(); if (SearchPanel.IsVisible) _editor.Find(SearchBox.Text, Settings.MatchCase, restart: true); }
    }

    private async void OnPreviewMessageReceived(object? sender, PreviewMessage message)
    {
        if (_disposed || sender is not DocumentView view
            || !_documentViews.TryGetValue(view.Document.Id, out var owner) || !ReferenceEquals(owner, view)) return;
        await GuardAsync(async () =>
        {
            // Hidden pages may finish loading or scrolling; keep those updates with their owner.
            switch (message.Type)
            {
                case "ready":
                    // Loading may finish after a theme change, even while this tab is hidden.
                    await view.Preview.SetThemeAsync(_dark);
                    await view.Preview.SetFontSizeAsync(Settings.PreviewFontSize);
                    return;
                case "scroll":
                    if (double.TryParse(message.Text, CultureInfo.InvariantCulture, out var scroll) && double.IsFinite(scroll))
                        view.Document.PreviewScroll = Math.Max(0, scroll);
                    _previewSourceLines[view.Document.Id] = Math.Max(1, message.Line);
                    if (ReferenceEquals(view, _currentView)) _previewLine = Math.Max(1, message.Line);
                    return;
                case "overlay":
                    view.PreviewOverlay = message.Flag;
                    if (ReferenceEquals(view, _currentView)) _previewOverlay = message.Flag;
                    return;
                case "error":
                    if (message.Text is not null) LocalLog.Write(new InvalidOperationException(message.Text));
                    return;
            }
            if (ReferenceEquals(view, _currentView) && view.Document.IsPreviewMode)
                await HandlePreviewAsync(message);
        });
    }

    private async Task RenderAsync(int? line = null)
    {
        var tab = _current;
        var view = _currentView;
        if (tab is null || view is null || _disposed || !tab.IsPreviewMode && tab.IsLargeFile) return;
        var options = new PreviewOptions(_dark, PreviewFontName, Settings.PreviewFontSize,
            Settings.CodeLineNumbers, Settings.EmojiShortcodes, UiLanguage, tab.IsReadOnly);
        if (!tab.IsPreviewMode)
        {
            await view.Preview.PrepareAsync(tab.Content, tab.FilePath, options);
            return;
        }
        await view.Preview.ShowAsync(tab.Content, tab.FilePath, options, tab.PreviewScroll, line);
        if (ReferenceEquals(view, _currentView) && ReferenceEquals(tab, _current) && tab.IsPreviewMode)
            await view.Preview.FindAsync(SearchPanel.IsVisible ? SearchBox.Text : "", Settings.MatchCase, restart: true);
    }

    private async Task ToggleModeAsync(int? sourceLine = null)
    {
        var tab = _current;
        if (tab is null) return;
        if (tab.IsPreviewMode && tab.IsReadOnly) { Toast(T("Read-only document", "唯讀文件", "読み取り専用の文書")); return; }
        if (tab.IsPreviewMode)
        {
            tab.PreviewScroll = await _preview.GetScrollAsync();
            if (!ReferenceEquals(tab, _current)) return;
            tab.IsPreviewMode = false;
            SelectDocument(tab);
            _editor.GoToLine(sourceLine ?? _previewLine);
        }
        else
        {
            var line = _editor.Editor.TextArea.Caret.Line;
            tab.IsPreviewMode = true;
            SelectDocument(tab, line);
        }
    }

    private void NewDocument() => AddDocument(new DocumentTab { IsPreviewMode = false });

    private async Task OpenDialogAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Markdown|*.md;*.markdown;*.mdown;*.mkd|Text files|*.txt|All files|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await OpenPathsAsync(dialog.FileNames);
    }

    private async Task<bool> SaveAsync(DocumentTab tab, bool saveAs = false, bool automatic = false)
    {
        if (_disposed || !Documents.Contains(tab)) return false;
        if (!_saving.Add(tab.Id)) return false;
        try
        {
            string? path = null;
            if (saveAs || tab.FilePath is null || tab.IsMissing || tab.IsReadOnly)
            {
                if (automatic) return false;
                var dialog = new SaveFileDialog { FileName = tab.DisplayName, DefaultExt = ".md", AddExtension = true, Filter = "Markdown|*.md|All files|*.*", OverwritePrompt = true };
                if (tab.FilePath is not null) dialog.InitialDirectory = Path.GetDirectoryName(tab.FilePath);
                if (dialog.ShowDialog(this) != true) return false;
                path = dialog.FileName;
                if (App.AllDocuments.Any(other => other != tab && string.Equals(other.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                { Toast(T("That file is already open in another tab.", "該檔案已在另一個分頁開啟。", "そのファイルは別のタブで開いています。")); return false; }
            }
            await App.Files.SaveAsync(tab, path);
            try { App.Preferences.AddRecent(tab.FilePath!); } catch (Exception ex) { LocalLog.Write(ex); }
            if (!tab.IsDirty) App.Recovery.Delete(tab.Id);
            if (_disposed || !Documents.Contains(tab)) return !tab.IsDirty;
            UpdateStatus(); BuildTabs();
            if (!automatic) Toast(T("Saved", "已儲存", "保存しました"));
            return !tab.IsDirty;
        }
        catch (FileConflictException)
        {
            if (automatic) _modified.Remove(tab.Id);
            if (_disposed || !Documents.Contains(tab)) return false;
            if (App.Files.RefreshStatus(tab) == FileChangeStatus.Missing)
            {
                Toast(T("File missing — use Save As", "檔案已消失，可另存新檔", "ファイルが見つかりません。名前を付けて保存できます"));
                return false;
            }
            if (automatic) return false;
            var choice = Choose(T("The file changed outside MarkPad.", "檔案已被其他程式修改。", "ファイルが外部で変更されました。"),
                ("reload", T("Reload", "重新載入", "再読み込み")), ("keep", T("Keep current", "保留目前內容", "現在の内容を保持")), ("cancel", T("Cancel", "取消", "キャンセル")));
            if (choice == "reload") await ReloadAsync(tab, whileClosing: true);
            else if (choice == "keep") { App.Files.AcceptExternalChanges(tab); Toast(T("Current content kept. Save again to overwrite.", "已保留目前內容，再次儲存即可寫入。", "現在の内容を保持しました。再度保存すると書き込みます。")); }
            return false;
        }
        catch (Exception ex) { if (automatic) _modified.Remove(tab.Id); Report(ex); return false; }
        finally { _saving.Remove(tab.Id); ScheduleAutoSave(); }
    }

    private async Task ReloadAsync(DocumentTab tab, bool whileClosing = false)
    {
        if (tab.FilePath is null || _disposed || _closing && !whileClosing || !Documents.Contains(tab)) return;
        var path = tab.FilePath;
        var version = tab.Document.Version;
        var baseline = tab.DiskHash;
        var fresh = await App.Files.OpenAsync(path);
        if (_disposed || _closing && !whileClosing || !Documents.Contains(tab) || tab.FilePath != path
            || tab.DiskHash != baseline || !ReferenceEquals(version, tab.Document.Version)) return;
        tab.Content = fresh.Content;
        tab.Document.UndoStack.ClearAll();
        tab.Encoding = fresh.Encoding; tab.HasBom = fresh.HasBom;
        tab.IsReadOnly = fresh.IsReadOnly; tab.IsLargeFile = fresh.IsLargeFile;
        tab.DiskHash = fresh.DiskHash; tab.DiskLength = fresh.DiskLength;
        tab.LastWriteTimeUtc = fresh.LastWriteTimeUtc; tab.IsMissing = false;
        tab.IsDirty = false;
        App.Recovery.Delete(tab.Id);
        if (tab.IsReadOnly) tab.IsPreviewMode = true;
        if (_current == tab) SelectDocument(tab);
    }

    private async Task CloseDocumentAsync(DocumentTab tab)
    {
        if (_disposed || _closing || !Documents.Contains(tab) || !_closingDocuments.Add(tab.Id)) return;
        try
        {
            if (_saving.Contains(tab.Id))
            {
                Toast(T("Saving is still in progress. Please close again when it finishes.", "檔案正在儲存，完成後即可關閉。", "保存中です。完了後にもう一度閉じてください。"));
                return;
            }
            if (tab.IsDirty)
            {
                var choice = Choose(tab.DisplayName + "\n" + T("Save changes?", "要儲存變更嗎？", "変更を保存しますか？"),
                    ("save", T("Save", "儲存", "保存")), ("discard", T("Don't save", "不要儲存", "保存しない")), ("cancel", T("Cancel", "取消", "キャンセル")));
                if (choice is null or "cancel" || choice == "save" && !await SaveAsync(tab)) return;
            }
            if (_disposed || !Documents.Contains(tab)) return;
            App.Recovery.Delete(tab.Id);
            RemoveDocument(tab);
        }
        finally { _closingDocuments.Remove(tab.Id); ScheduleAutoSave(); }
    }

    private void RemoveDocument(DocumentTab tab)
    {
        tab.PropertyChanged -= TabChanged;
        Documents.Remove(tab); _modified.Remove(tab.Id);
        _previewSourceLines.Remove(tab.Id);
        if (_current == tab) SelectDocument(Documents.OrderByDescending(d => d.LastActivatedUtc).FirstOrDefault());
        else BuildTabs();
        ReleaseDocumentView(tab);
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        _autoSaveTimer.Stop();
        if (_disposed || _closing || _autoSaveRunning || !Settings.AutoSave) return;
        DateTime? nextDue = null;
        foreach (var tab in Documents)
        {
            if (!CanAutoSave(tab) || !_modified.TryGetValue(tab.Id, out var modified)) continue;
            var due = modified + TimeSpan.FromSeconds(3);
            if (nextDue is null || due < nextDue) nextDue = due;
        }
        if (nextDue is null) return;
        _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, (nextDue.Value - DateTime.UtcNow).TotalMilliseconds));
        _autoSaveTimer.Start();
    }

    private bool CanAutoSave(DocumentTab tab) => tab.IsDirty && tab.FilePath is not null && !tab.IsReadOnly && !tab.IsMissing
        && !_saving.Contains(tab.Id) && !_closingDocuments.Contains(tab.Id) && Documents.Contains(tab);

    private async Task AutoSaveDueAsync()
    {
        _autoSaveTimer.Stop();
        if (_disposed || _closing || _autoSaveRunning || !Settings.AutoSave) return;
        _autoSaveRunning = true;
        try
        {
            foreach (var tab in Documents.ToArray())
            {
                if (_disposed || _closing || !Settings.AutoSave) break;
                if (!CanAutoSave(tab) || !_modified.TryGetValue(tab.Id, out var modified)
                    || DateTime.UtcNow - modified < TimeSpan.FromSeconds(3)) continue;
                await SaveAsync(tab, automatic: true);
            }
        }
        catch (Exception ex) { Report(ex); }
        finally { _autoSaveRunning = false; ScheduleAutoSave(); }
    }

    private async Task MaintainAsync()
    {
        if (_checking || _closing || _disposed) return;
        _checking = true;
        try
        {
            foreach (var tab in Documents.ToArray())
            {
                if (_closing || _disposed) return;
                if (!Documents.Contains(tab) || _saving.Contains(tab.Id) || _closingDocuments.Contains(tab.Id)) continue;
                try
                {
                    var wasMissing = tab.IsMissing;
                    var state = App.Files.RefreshStatus(tab);
                    if (state == FileChangeStatus.Changed)
                    {
                        if (!tab.IsDirty) await ReloadAsync(tab);
                        else
                        {
                            var choice = Choose(tab.DisplayName + "\n" + T("Changed outside MarkPad.", "已被其他程式修改。", "外部で変更されました。"),
                                ("reload", T("Reload", "重新載入", "再読み込み")), ("keep", T("Keep current", "保留目前內容", "現在の内容を保持")));
                            if (choice == "reload") await ReloadAsync(tab);
                            else if (choice == "keep") App.Files.AcceptExternalChanges(tab);
                            else continue;
                        }
                    }
                    if (_closing || _disposed) return;
                    if (!Documents.Contains(tab) || _saving.Contains(tab.Id) || _closingDocuments.Contains(tab.Id)) continue;
                    if (state == FileChangeStatus.Missing && !wasMissing) Toast(T("File missing — use Save As", "檔案已消失，可另存新檔", "ファイルが見つかりません。名前を付けて保存できます"));
                    if (tab.IsReadOnly && !tab.IsPreviewMode) { tab.IsPreviewMode = true; if (_current == tab) SelectDocument(tab); }
                }
                catch (Exception ex) { Report(ex); }
            }
            if (!_disposed && !_closing) await App.Recovery.SaveAsync(App.AllDocuments.ToArray());
        }
        catch (Exception ex) { Report(ex); }
        finally { _checking = false; }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        if (_saving.Count > 0 || _closingDocuments.Count > 0 || _exportingPdf)
        {
            Toast(T("Saving is still in progress. Please close again when it finishes.", "檔案正在處理，完成後即可關閉。", "処理中です。完了後にもう一度閉じてください。"));
            return;
        }
        _closing = true;
        _autoSaveTimer.Stop();
        var wasEnabled = IsEnabled;
        IsEnabled = false;
        try
        {
            var closingTabs = Documents.ToArray();
            var dirty = closingTabs.Where(d => d.IsDirty).ToArray();
            var discardVersions = new Dictionary<Guid, ICSharpCode.AvalonEdit.Document.ITextSourceVersion>();
            if (dirty.Length > 0)
            {
                var selected = ConfirmClose(dirty);
                if (selected is null) return;
                foreach (var tab in dirty.Except(selected)) discardVersions[tab.Id] = tab.Document.Version;
                foreach (var tab in selected) if (!await SaveAsync(tab)) return;
            }
            if (Documents.Any(tab => !closingTabs.Contains(tab) || tab.IsDirty &&
                (!discardVersions.TryGetValue(tab.Id, out var version) || !ReferenceEquals(version, tab.Document.Version))))
            {
                Toast(T("A document changed while closing. Your changes are still open.", "關閉期間有文件變更，已保留視窗與內容。", "終了中に文書が変更されたため、ウィンドウを開いたままにしました。"));
                return;
            }
            if (Settings.RememberWindowSize && !_fullScreen)
            {
                Settings.WindowWidth = RestoreBounds.Width;
                Settings.WindowHeight = RestoreBounds.Height;
                Settings.WindowMaximized = WindowState == WindowState.Maximized;
                App.Preferences.Save();
            }
            foreach (var tab in Documents) App.Recovery.Delete(tab.Id);
            _allowClose = true;
            // Clean documents reach here synchronously during Closing. Wait until that event
            // has returned before closing again; WPF rejects a reentrant Close call.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception ex) { Report(ex); }
        finally { _closing = false; if (!_disposed) IsEnabled = wasEnabled; ScheduleAutoSave(); }
    }

    private async Task HandlePreviewAsync(PreviewMessage message)
    {
        switch (message.Type)
        {
            case "link": if (message.Text is not null) await OpenLinkAsync(message.Text); break;
            case "edit": await ToggleModeAsync(message.Line); break;
            case "copy": case "copy-markdown": if (message.Text is not null) Clipboard.SetText(message.Text); break;
            case "copy-link":
                if (message.Text is { } href)
                    Clipboard.SetText(href.StartsWith('#') && _current?.FilePath is { } file ? new Uri(file).AbsoluteUri + href : href);
                break;
            case "task":
                if (_current is not { IsReadOnly: false } tab || message.Line < 1 || message.Line > tab.Document.LineCount) break;
                var line = tab.Document.GetLineByNumber(message.Line);
                var text = tab.Document.GetText(line);
                var match = Regex.Match(text, @"^(\s*(?:>\s*)*(?:[-+*]|\d+[.)])\s+)\[[ xX]\]");
                if (match.Success) tab.Document.Replace(line.Offset + match.Length - 2, 1, message.Flag ? "x" : " ");
                break;
            case "search": ShowSearchResult(message.Index, message.Count, message.Flag); break;
            case "shortcut": if (message.Text is not null) await ShortcutAsync(message.Text); break;
        }
    }

    private async Task OpenLinkAsync(string url)
    {
        if (url.StartsWith('#')) { await _preview.GoToAnchorAsync(url); return; }
        if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        { Clipboard.SetText(Uri.UnescapeDataString(url[7..].Split('?')[0])); Toast(T("Email copied", "已複製 Email", "メールアドレスをコピーしました")); return; }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return; }
        var hash = url.IndexOf('#');
        var local = Uri.UnescapeDataString(hash < 0 ? url : url[..hash]);
        if (string.IsNullOrEmpty(local)) return;
        if (Uri.TryCreate(local, UriKind.Absolute, out var absolute))
        { if (!absolute.IsFile) return; local = absolute.LocalPath; }
        if (Path.GetExtension(local).ToLowerInvariant() is not (".md" or ".markdown" or ".mdown" or ".mkd")) return;
        if (!Path.IsPathRooted(local))
        { if (_current?.FilePath is null) return; local = Path.Combine(Path.GetDirectoryName(_current.FilePath)!, local); }
        await OpenPathsAsync([local]);
        if (hash >= 0)
        {
            var target = Application.Current.Windows.OfType<MainWindow>()
                .FirstOrDefault(window => string.Equals(window._current?.FilePath, Canonical(local), StringComparison.OrdinalIgnoreCase));
            if (target?._current is { IsPreviewMode: true } destination)
            {
                await target.RenderAsync();
                if (ReferenceEquals(target._current, destination)) await target._preview.GoToAnchorAsync(url[hash..]);
            }
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("MarkPad.Tab")) { e.Effects = DragDropEffects.Move; e.Handled = true; }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; }
    }

    private async Task DropAsync(DragEventArgs e)
    {
        if (e.Data.GetData("MarkPad.Tab") is TabTransfer transfer)
        { AcceptTab(transfer); e.Handled = true; return; }
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        e.Handled = true;
        var target = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? App.CreateWindow() : this;
        foreach (var path in paths)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (target == this && _current?.IsPreviewMode == false && extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg")
                await ImportImageAsync(path);
            else if (extension is ".md" or ".markdown" or ".mdown" or ".mkd" or ".txt") await target.OpenPathsAsync([path]);
        }
    }

    private async Task ImportImageAsync(string source)
    {
        var tab = _current;
        if (tab is null || tab.IsReadOnly) return;
        if (tab.FilePath is null && !await SaveAsync(tab)) return;
        var documentPath = tab.FilePath!;
        var relative = await App.Files.ImportImageAsync(source, documentPath);
        if (_disposed || !Documents.Contains(tab) || _current != tab || tab.FilePath != documentPath) return;
        var alt = Path.GetFileNameWithoutExtension(source).Replace("[", "\\[").Replace("]", "\\]");
        _editor.Editor.SelectedText = $"![{alt}]({relative})";
    }

    private async Task PasteImageAsync()
    {
        var tab = _current;
        if (tab is null || tab.IsReadOnly || !Clipboard.ContainsImage()) return;
        var bitmap = Clipboard.GetImage();
        if (bitmap is null) return;
        if (tab.FilePath is null && !await SaveAsync(tab)) return;
        if (_disposed || !Documents.Contains(tab) || _current != tab) return;
        var directory = Path.Combine(Path.GetDirectoryName(tab.FilePath!)!, "images");
        Directory.CreateDirectory(directory);
        var name = $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.png";
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew)) encoder.Save(stream);
        if (!_disposed && Documents.Contains(tab) && _current == tab) _editor.Editor.SelectedText = $"![image](images/{name})";
    }

    private void UpdateStatus()
    {
        EncodingLabel.Text = _current?.Encoding.WebName.ToUpperInvariant() ?? "";
        if (_current?.HasBom == true) EncodingLabel.Text += " BOM";
        if (_current?.IsReadOnly == true) EncodingLabel.Text += "\n" + T("Read-only", "唯讀", "読取専用");
        if (_current?.IsMissing == true) EncodingLabel.Text += "\n" + T("File missing", "檔案消失", "ファイルなし");
        if (_current?.IsLargeFile == true) EncodingLabel.Text += "\n" + T("Large file", "大型檔案", "大容量");
        CaretLabel.Text = _current?.IsPreviewMode == false ? $"Ln {_editor.Editor.TextArea.Caret.Line}\nCol {_editor.Editor.TextArea.Caret.Column}" : "";
        // Keep the Windows taskbar title as MarkPad; filenames and dirty state belong to tabs.
    }

    private void Toast(string message) { StatusLabel.Text = message; StatusToast.Visibility = Visibility.Visible; _toastTimer.Stop(); _toastTimer.Start(); }
    private void Report(Exception ex) { LocalLog.Write(ex); Toast(T("Unable to complete the action: ", "無法完成操作：", "操作できませんでした：") + ex.Message); }
    private async Task GuardAsync(Func<Task> action) { try { await action(); } catch (Exception ex) { Report(ex); } }
}
