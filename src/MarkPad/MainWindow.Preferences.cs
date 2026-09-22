using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MarkPad;

public partial class MainWindow
{
    private bool _railExpanded, _fullScreen;
    private readonly DispatcherTimer _fullScreenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Rect _beforeFullScreen, _previousSize;
    private WindowState _beforeFullScreenState;
    private string? _sizePreset;
    private Popup? _flyout;

    private void SetupRail()
    {
        RailToggleButton.Click += (_, _) =>
        {
            Settings.ToolbarPinned = !Settings.ToolbarPinned;
            // The sidebar toggle does not redraw the document or move the editor's caret.
            try { App.Preferences.Save(); } catch (Exception ex) { Report(ex); }
            foreach (var window in Application.Current.Windows.OfType<MainWindow>()) window.BuildActions();
        };
    }

    private void BuildActions()
    {
        if (ActionPanel is null) return;
        _railExpanded = Settings.ToolbarPinned;
        Rail.Width = _railExpanded ? Math.Max(200, Math.Ceiling(UiSize(200))) : 44;
        // The old 44px layout slot clipped expanded labels and their mouse hit targets.
        RailColumn.Width = new GridLength(Rail.Width);
        // The full-screen title reveal must not cover the always-available sidebar toggle.
        TitleBar.Margin = TopReveal.Margin = new Thickness(_fullScreen ? Rail.Width : 0, 0, 0, 0);
        ActionScrollViewer.VerticalScrollBarVisibility = _railExpanded ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        var toggleLabel = _railExpanded ? T("Collapse sidebar", "收合工具列", "サイドバーを閉じる") : T("Expand sidebar", "展開工具列", "サイドバーを開く");
        var toggleRow = new StackPanel { Orientation = Orientation.Horizontal };
        toggleRow.Children.Add(new TextBlock { Text = _railExpanded ? "\uE76B" : "\uE700", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Width = 28, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (_railExpanded) toggleRow.Children.Add(new TextBlock { Text = toggleLabel, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        RailToggleButton.Content = toggleRow;
        RailToggleButton.ToolTip = _railExpanded ? null : toggleLabel;
        System.Windows.Automation.AutomationProperties.SetName(RailToggleButton, toggleLabel);
        System.Windows.Automation.AutomationProperties.SetHelpText(RailToggleButton, _railExpanded
            ? T("Show icons only", "只顯示按鍵圖示", "アイコンのみを表示")
            : T("Show button names, descriptions and shortcuts", "顯示按鍵名稱、功能說明與快捷鍵", "機能名・説明・ショートカットを表示"));
        ActionPanel.Children.Clear();
        AddGroup(T("Document", "文件", "文書"));
        AddAction(_current?.IsPreviewMode == false ? "\uE70F" : "\uE890", _current?.IsPreviewMode == false ? T("Edit", "編輯", "編集") : T("Preview", "預覽", "プレビュー"),
            _current?.IsReadOnly == true ? T("Read-only; editing unavailable", "唯讀文件，無法切換編輯", "読み取り専用の文書です")
                : _current?.IsPreviewMode == false ? T("Switch to formatted reading", "切換為排版後的閱讀畫面", "読みやすい表示に切り替え")
                : T("Switch to Markdown editing", "切換為 Markdown 原始碼編輯", "Markdown の編集に切り替え"),
            () => ToggleModeAsync(), "Ctrl+E", _current is not null);
        AddAction("\uE8E5", T("Open", "開啟", "開く"), T("Open Markdown in a new tab", "在分頁中開啟 Markdown 檔案", "Markdown を新しいタブで開く"), OpenDialogAsync, "Ctrl+O");
        AddAction("\uE74E", T("Save", "儲存", "保存"), T("Save this document's changes", "儲存目前文件的變更", "現在の文書の変更を保存"),
            async () => { if (_current is not null) await SaveAsync(_current); }, "Ctrl+S", _current is not null);
        AddAction("PDF", T("Export PDF", "匯出 PDF", "PDF 書き出し"),
            T("Save, then export PDF beside Markdown", "儲存原稿，於旁邊匯出 PDF", "保存して Markdown の隣に PDF を作成"),
            ExportPdfAsync, enabled: _current is not null && !_exportingPdf);
        AddAction("\uE721", T("Search", "搜尋", "検索"), T("Find text; replace in Edit mode", "尋找文字，編輯模式可取代", "文字を検索・編集時は置換も可能"), OpenSearchAsync, "Ctrl+F", _current is not null);
        AddAction("\uE8BB", T("Close", "關閉文件", "閉じる"), T("Close the current document tab", "關閉目前文件分頁", "現在の文書タブを閉じる"),
            async () => { if (_current is not null) await CloseDocumentAsync(_current); }, "Ctrl+W", _current is not null);
        AddGroup(T("Reading", "閱讀", "表示"));
        AddAction("\uE740", T("Window size", "視窗尺寸", "サイズ"), T("Resize, snap or go full screen", "調整寬度、半螢幕或全螢幕", "幅・半画面・全画面を選択"), () => { ShowSizeMenu(); return Task.CompletedTask; });
        AddAction("Aa", T("Font", "字型", "フォント"), T("Choose UI and document fonts", "設定介面與內文字型、字級", "UI・本文の字体とサイズを設定"), () => { ShowFontPicker(); return Task.CompletedTask; });
        AddAction(_dark ? "\uE708" : "\uE706", _dark ? T("Dark", "暗色", "ダーク") : T("Light", "亮色", "ライト"),
            _dark ? T("Switch to the light theme", "切換成亮色主題", "ライトテーマに切り替え") : T("Switch to the dark theme", "切換成暗色主題", "ダークテーマに切り替え"), () =>
        { Settings.Theme = _dark ? "Light" : "Dark"; App.ApplyPreferences(); return Task.CompletedTask; });
        AddAction("\uE774", T("Language", "語言", "言語"), T("Change the interface language", "切換介面顯示語言", "画面に表示する言語を変更"), () => { ShowLanguagePicker(); return Task.CompletedTask; });
        AddGroup("");
        AddAction("\uE712", T("More", "更多", "その他"), T("Recent files, auto save and more", "最近開啟、自動儲存與設定", "最近のファイル・自動保存・設定"), () => { ShowMore(); return Task.CompletedTask; });
    }

    private void AddGroup(string name)
    {
        // Reserve the same height in both states so revealing descriptions never moves the icons.
        var group = new Grid { Height = Math.Max(name.Length == 0 ? 12 : 20, UiSize(name.Length == 0 ? 12 : 20)), Margin = new Thickness(8, 0, 8, 0) };
        group.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, Background = B("LineBrush") });
        if (_railExpanded && name.Length > 0)
            group.Children.Add(new TextBlock { Text = name, Foreground = B("MutedBrush"), Background = B("WindowBackground"), FontSize = UiSize(11),
                Padding = new Thickness(0, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        ActionPanel.Children.Add(group);
    }

    private void AddAction(string glyph, string label, string description, Func<Task> action, string? shortcut = null, bool enabled = true)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(glyph is "Aa" or "PDF" ? "Segoe UI" : "Segoe MDL2 Assets"),
            FontSize = glyph == "PDF" ? 12 : 16, FontWeight = glyph == "PDF" ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center });
        if (_railExpanded)
        {
            var details = new StackPanel { Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            var heading = new DockPanel();
            if (shortcut is not null)
            {
                var key = new TextBlock { Text = shortcut, FontSize = UiSize(10), Foreground = B("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                DockPanel.SetDock(key, Dock.Right); heading.Children.Add(key);
            }
            heading.Children.Add(new TextBlock { Text = label, FontSize = UiSize(12), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            details.Children.Add(heading);
            details.Children.Add(new TextBlock { Text = description, FontSize = UiSize(11), LineHeight = UiSize(14), LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = B("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(details, 1); row.Children.Add(details);
        }
        var button = new Button { Content = row, Height = Math.Max(54, UiSize(54)), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(4, 3, 4, 3), IsEnabled = enabled };
        if (!_railExpanded) button.ToolTip = label + (shortcut is null ? "" : "  (" + shortcut + ")") + "\n" + description;
        ToolTipService.SetShowOnDisabled(button, true);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, description);
        button.Click += async (_, _) => await GuardAsync(action);
        ActionPanel.Children.Add(button);
    }
    public void ApplyPreferences()
    {
        if (_disposed) return;
        ApplyUiTypography();
        _dark = Settings.Theme == "Dark" || Settings.Theme == "System" && IsSystemDark();
        var colors = _dark ? new[] { "#161B22", "#0D1117", "#E6EDF3", "#8B949E", "#30363D", "#252C36", "#58A6FF" }
            : new[] { "#D9DEE4", "#E8EBEF", "#1F2328", "#656D76", "#BEC6CF", "#CDD4DD", "#0969DA" };
        var keys = new[] { "WindowBackground", "SurfaceBrush", "TextBrush", "MutedBrush", "LineBrush", "HoverBrush", "AccentBrush" };
        for (var i = 0; i < keys.Length; i++) Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        _editor.ApplyOptions(_dark, Settings.EditorFontFamily, Settings.EditorFontSize);
        _editor.ApplyLanguage(UiLanguage);
        EmptyLabel.Text = T("Drop or open Markdown", "拖入或開啟 Markdown", "Markdown をドロップまたは開く");
        EmptyOpenButton.Content = T("Open a document  ·  Ctrl+O", "開啟文件  ·  Ctrl+O", "文書を開く  ·  Ctrl+O");
        ExpandReplaceButton.Content = ReplaceButton.Content = T("Replace", "取代", "置換");
        ReplaceAllButton.Content = T("Replace all", "全部取代", "すべて置換");
        SearchBox.ToolTip = T("Find text", "尋找文字", "文字列を検索");
        ReplaceBox.ToolTip = T("Replace with", "取代為", "置換後の文字列");
        CaseButton.ToolTip = T("Match case", "區分大小寫", "大文字と小文字を区別");
        CaseButton.Background = Settings.MatchCase ? B("HoverBrush") : Brushes.Transparent;
        ClearSearchButton.ToolTip = T("Clear", "清除", "クリア");
        CloseSearchButton.ToolTip = T("Close search", "關閉搜尋", "検索を閉じる");
        PreviousSearchButton.ToolTip = T("Previous  ·  Shift+F3", "上一個  ·  Shift+F3", "前へ  ·  Shift+F3");
        NextSearchButton.ToolTip = T("Next  ·  F3", "下一個  ·  F3", "次へ  ·  F3");
        MinimizeButton.ToolTip = T("Minimize", "最小化", "最小化");
        MaximizeButton.ToolTip = T("Maximize / restore", "最大化／還原", "最大化／元に戻す");
        WindowCloseButton.ToolTip = T("Close window", "關閉視窗", "ウィンドウを閉じる");
        OverflowButton.ToolTip = T("Documents", "文件分頁", "文書一覧");

        BuildActions(); BuildTabs(); UpdateStatus();
        ScheduleAutoSave();
        if (_current?.IsPreviewMode == true) _ = GuardAsync(() => RenderAsync());
    }

    private static bool IsSystemDark()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        catch { return false; }
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    { if (Settings.Theme == "System") Dispatcher.BeginInvoke(new Action(ApplyPreferences)); }

    private MenuItem Item(string label, Action action, bool? check = null)
    {
        var item = new MenuItem { Header = label };
        if (check.HasValue) { item.IsCheckable = true; item.IsChecked = check.Value; }
        item.Click += (_, e) => { e.Handled = true; try { action(); } catch (Exception ex) { Report(ex); } };
        return item;
    }

    private MenuItem AsyncItem(string label, Func<Task> action)
    {
        var item = new MenuItem { Header = label };
        item.Click += async (_, e) => { e.Handled = true; await GuardAsync(action); };
        return item;
    }

    private void OpenMenu(ContextMenu menu)
    {
        // Popups live outside the window's visual tree; share its live theme resources explicitly.
        menu.Resources = Resources;
        menu.PlacementTarget = Rail; menu.Placement = PlacementMode.Right;
        menu.MaxWidth = Math.Max(320, UiSize(320));
        menu.IsOpen = true;
    }

    public void ShowLanguagePicker(bool modal = false)
    {
        if (modal)
        {
            var choice = Choose("Choose your language · 選擇語言 · 言語を選択", ("System", T("System", "跟隨系統", "システムに従う")), ("en", "English"), ("zh-TW", "繁體中文"), ("ja", "日本語"));
            Settings.Language = choice ?? "System";
            App.ApplyPreferences();
            return;
        }
        var menu = new ContextMenu();
        foreach (var (id, label) in new[] { ("System", T("System", "跟隨系統", "システムに従う")), ("en", "English"), ("zh-TW", "繁體中文"), ("ja", "日本語") })
            menu.Items.Add(Item(label, () => { Settings.Language = id; App.ApplyPreferences(); }, Settings.Language == id));
        OpenMenu(menu);
    }

    private void ShowMore()
    {
        var menu = new ContextMenu();
        MenuItem Section(string name) { var item = new MenuItem { Header = name, FontWeight = FontWeights.SemiBold }; menu.Items.Add(item); return item; }
        var general = Section(T("General", "一般", "一般"));
        general.Items.Add(AsyncItem(T("New document  ·  Ctrl+N", "新增文件  ·  Ctrl+N", "新規文書  ·  Ctrl+N"), () => { NewDocument(); return Task.CompletedTask; }));
        general.Items.Add(Item(T("New window", "新增視窗", "新しいウィンドウ"), () => App.CreateWindow()));
        general.Items.Add(Item(T("Remember window size", "記住視窗尺寸", "ウィンドウサイズを記憶"), () => { Settings.RememberWindowSize = !Settings.RememberWindowSize; App.ApplyPreferences(); }, Settings.RememberWindowSize));
        var theme = new MenuItem { Header = T("Theme", "主題", "テーマ") };
        foreach (var (value, label) in new[] { ("Light", T("Light", "亮色", "ライト")), ("Dark", T("Dark", "暗色", "ダーク")), ("System", T("System", "跟隨系統", "システム")) })
            theme.Items.Add(Item(label, () => { Settings.Theme = value; App.ApplyPreferences(); }, Settings.Theme == value));
        general.Items.Add(theme);
        var editor = Section(T("Editor", "編輯器", "エディター"));
        editor.Items.Add(Item(T("Auto save after 3 seconds", "停止輸入 3 秒自動儲存", "入力停止から 3 秒後に自動保存"), () => { Settings.AutoSave = !Settings.AutoSave; App.ApplyPreferences(); }, Settings.AutoSave));
        var preview = Section(T("Preview", "預覽", "プレビュー"));
        preview.Items.Add(Item(T("Code block line numbers", "程式碼區塊行號", "コードブロックの行番号"), () => { Settings.CodeLineNumbers = !Settings.CodeLineNumbers; App.ApplyPreferences(); }, Settings.CodeLineNumbers));
        preview.Items.Add(Item(T("Convert emoji shortcodes", "轉換 Emoji 短碼", "絵文字ショートコードを変換"), () => { Settings.EmojiShortcodes = !Settings.EmojiShortcodes; App.ApplyPreferences(); }, Settings.EmojiShortcodes));
        var files = Section(T("File", "檔案", "ファイル"));
        var saveAs = AsyncItem(T("Save as…  ·  Ctrl+Shift+S", "另存新檔…  ·  Ctrl+Shift+S", "名前を付けて保存…  ·  Ctrl+Shift+S"), async () => { if (_current is not null) await SaveAsync(_current, true); });
        saveAs.IsEnabled = _current is not null; files.Items.Add(saveAs);
        var recent = new MenuItem { Header = T("Recent files", "最近開啟", "最近使ったファイル") };
        foreach (var path in Settings.RecentFiles.ToArray()) recent.Items.Add(AsyncItem(path, () => OpenPathsAsync([path])));
        if (recent.Items.Count == 0) recent.Items.Add(new MenuItem { Header = T("No recent files", "沒有最近開啟的檔案", "最近のファイルはありません"), IsEnabled = false });
        files.Items.Add(recent);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(T("Reset settings…", "重設設定…", "設定をリセット…"), () =>
        {
            if (Choose(T("Reset all MarkPad settings?", "要將所有 MarkPad 設定重設為預設值嗎？", "MarkPad の設定を初期状態に戻しますか？"), ("reset", T("Reset", "重設", "リセット")), ("cancel", T("Cancel", "取消", "キャンセル"))) != "reset") return;
            App.Preferences.Reset();
            App.ApplyPreferences();
        }));
        menu.Items.Add(Item(T($"About MarkPad {AppVersion}", $"關於 MarkPad {AppVersion}", $"MarkPad {AppVersion} について"), () =>
        {
            // Release the menu's mouse capture before the dismissible About popup opens.
            menu.IsOpen = false;
            _ = Dispatcher.BeginInvoke(new Action(ShowAbout), DispatcherPriority.Input);
        }));
        menu.Items.Add(Item(T("Check for updates", "檢查更新", "更新を確認"), () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://downloadsandupdates") { UseShellExecute = true })));
        OpenMenu(menu);
    }

    private static string AppVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";

    private void ShowAbout()
    {
        var body = new StackPanel { Margin = new Thickness(20), Width = Math.Max(260, UiSize(260)) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new Image { Source = Icon, Width = 28, Height = 28, Margin = new Thickness(0, 0, 10, 0) });
        var title = new TextBlock { Text = $"MarkPad {AppVersion}", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.FontSizeProperty, "UiSectionFontSize");
        heading.Children.Add(title); body.Children.Add(heading);
        var brand = new TextBlock { Text = T("ToolKeeper", "工具番 · ToolKeeper", "ToolKeeper"), Margin = new Thickness(0, 0, 0, 10) };
        brand.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(brand);
        body.Children.Add(new TextBlock
        {
            Text = T("Offline Markdown reader and editor.\nYour documents stay on this computer.",
                "離線 Markdown 閱讀與編輯工具。\n文件只在本機處理。",
                "オフライン Markdown リーダー・エディター。\n文書はこのコンピューターで処理されます。"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16)
        });
        body.Children.Add(new TextBlock { Text = T("Author: 不告訴你  Untold", "作者：不告訴你  Untold", "作者：不告訴你  Untold"), TextWrapping = TextWrapping.Wrap });
        var close = new Button { Content = T("Close", "關閉", "閉じる"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 70,
            Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(14, 6, 14, 6) };
        close.Click += (_, _) => { if (_flyout is not null) _flyout.IsOpen = false; };
        body.Children.Add(close);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            if (_flyout is not null) _flyout.IsOpen = false;
            e.Handled = true;
        };
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = Math.Max(180, ActualHeight - 80) };
        ShowFlyout(scroll, RootGrid, PlacementMode.Center);
        close.Focus();
    }

    private void ShowFlyout(UIElement body, UIElement? placementTarget = null, PlacementMode placement = PlacementMode.Right)
    {
        if (_flyout is not null) _flyout.IsOpen = false;
        var frame = new Border { Child = body, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        frame.Resources = Resources;
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        frame.SetResourceReference(FrameworkElement.MaxWidthProperty, "UiMenuMaxWidth");
        frame.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        frame.SetResourceReference(TextElement.FontFamilyProperty, "UiFontFamily");
        frame.SetResourceReference(TextElement.FontSizeProperty, "UiFontSize");
        _flyout = new Popup { Child = frame, PlacementTarget = placementTarget ?? Rail, Placement = placement, StaysOpen = false, AllowsTransparency = true };
        _flyout.IsOpen = true;
    }

    private void ShowSizeMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("900 px", () => ResizePreset("900")));
        menu.Items.Add(Item("1200 px", () => ResizePreset("1200")));
        menu.Items.Add(Item(T("Left half", "左半螢幕", "画面の左半分"), () => ResizePreset("left")));
        menu.Items.Add(Item(T("Right half", "右半螢幕", "画面の右半分"), () => ResizePreset("right")));
        menu.Items.Add(Item(T("Maximize", "最大化", "最大化"), () => ResizePreset("max")));
        menu.Items.Add(Item(T("Full screen  ·  F11", "全螢幕  ·  F11", "全画面  ·  F11"), ToggleFullScreen));
        OpenMenu(menu);
    }

    private void ResizePreset(string name)
    {
        if (_fullScreen) ToggleFullScreen();
        if (_sizePreset == name && !_previousSize.IsEmpty && _previousSize.Width > 0)
        { WindowState = WindowState.Normal; Left = _previousSize.Left; Top = _previousSize.Top; Width = _previousSize.Width; Height = _previousSize.Height; _sizePreset = null; return; }
        _previousSize = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _sizePreset = name;
        if (name == "max") { WindowState = WindowState.Maximized; return; }
        WindowState = WindowState.Normal;
        var area = MonitorWorkArea.Get(this);
        if (name is "900" or "1200") { Width = Math.Min(double.Parse(name, CultureInfo.InvariantCulture), area.Width); Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)); }
        else { Width = area.Width / 2; Height = area.Height; Top = area.Top; Left = name == "left" ? area.Left : area.Left + area.Width / 2; }
    }

    private void SetupFullScreen()
    {
        TopReveal.MouseEnter += (_, _) => RevealFullScreenTitle();
        _fullScreenTimer.Tick += (_, _) =>
        {
            if (!_fullScreen) { _fullScreenTimer.Stop(); return; }
            if (TitleBar.IsMouseOver || _flyout?.IsOpen == true) return;
            TitleBar.Visibility = Visibility.Collapsed;
            _fullScreenTimer.Stop();
        };
    }

    private void RevealFullScreenTitle()
    {
        if (!_fullScreen) return;
        TitleBar.Visibility = Visibility.Visible;
        _fullScreenTimer.Stop(); _fullScreenTimer.Start();
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _beforeFullScreenState = WindowState;
            _beforeFullScreen = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _fullScreen = true;
            WindowState = WindowState.Normal;
            var bounds = MonitorWorkArea.Get(this, full: true);
            Left = bounds.Left; Top = bounds.Top; Width = bounds.Width; Height = bounds.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0) });
            TitleRow.Height = new GridLength(0);
            TitleBar.Height = UiTitleHeight; TitleBar.VerticalAlignment = VerticalAlignment.Top; Grid.SetRowSpan(TitleBar, 2);
            TitleBar.Visibility = Visibility.Collapsed;
            Rail.Visibility = Visibility.Visible;
            TopReveal.Visibility = Visibility.Visible;
        }
        else
        {
            _fullScreen = false; _fullScreenTimer.Stop();
            ResizeMode = ResizeMode.CanResize;
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = UiTitleHeight, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(4), UseAeroCaptionButtons = false });
            Left = _beforeFullScreen.Left; Top = _beforeFullScreen.Top; Width = _beforeFullScreen.Width; Height = _beforeFullScreen.Height; WindowState = _beforeFullScreenState;
            TitleRow.Height = new GridLength(UiTitleHeight);
            TitleBar.Height = double.NaN; TitleBar.VerticalAlignment = VerticalAlignment.Stretch; Grid.SetRowSpan(TitleBar, 1);
            TitleBar.Visibility = Rail.Visibility = Visibility.Visible;
            TopReveal.Visibility = Visibility.Collapsed;
        }
        BuildActions();
    }
}
