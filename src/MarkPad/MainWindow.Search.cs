using System.Windows;
using System.Windows.Input;

namespace MarkPad;

public partial class MainWindow
{
    private int _searchCount;

    private void SetupSearch()
    {
        SearchBox.TextChanged += async (_, _) =>
        {
            ClearSearchButton.Visibility = SearchBox.Text.Length == 0 ? Visibility.Hidden : Visibility.Visible;
            if (SearchPanel.IsVisible) await GuardAsync(() => FindAsync(restart: true));
        };
        SearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await GuardAsync(() => FindAsync(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))); }
        };
        ClearSearchButton.Click += (_, _) => { SearchBox.Clear(); SearchBox.Focus(); };
        CloseSearchButton.Click += async (_, _) => await GuardAsync(CloseSearchAsync);
        CaseButton.Click += async (_, _) => { Settings.MatchCase = !Settings.MatchCase; App.ApplyPreferences(); await GuardAsync(() => FindAsync(restart: true)); };
        PreviousSearchButton.Click += async (_, _) => await GuardAsync(() => FindAsync(true));
        NextSearchButton.Click += async (_, _) => await GuardAsync(() => FindAsync());
        ExpandReplaceButton.Click += (_, _) =>
        { if (_current?.IsPreviewMode == false) ReplacePanel.Visibility = ReplacePanel.IsVisible ? Visibility.Collapsed : Visibility.Visible; };
        ReplaceButton.Click += (_, _) => { if (_current?.IsPreviewMode == false) _editor.Replace(ReplaceBox.Text); };
        ReplaceAllButton.Click += (_, _) =>
        {
            if (_current is not { IsPreviewMode: false, IsReadOnly: false } tab || _searchCount == 0) return;
            var version = tab.Document.Version;
            var query = SearchBox.Text;
            var replacement = ReplaceBox.Text;
            var matchCase = Settings.MatchCase;
            if (Choose(T($"Replace {_searchCount} occurrences?", $"將取代 {_searchCount} 處，是否繼續？", $"{_searchCount} 件を置換しますか？"),
                ("replace", T("Replace all", "全部取代", "すべて置換")), ("cancel", T("Cancel", "取消", "キャンセル"))) != "replace") return;
            // Modal dialogs pump the dispatcher: an incoming file request can switch tabs.
            if (_current != tab || !Documents.Contains(tab) || tab.IsReadOnly || tab.IsPreviewMode
                || !ReferenceEquals(version, tab.Document.Version))
            {
                Toast(T("The document changed. Please search again.", "文件已變更，請重新搜尋。", "文書が変更されました。もう一度検索してください。"));
                return;
            }
            var count = _editor.ReplaceAll(query, replacement, matchCase);
            Toast(T($"Replaced {count} occurrences", $"已取代 {count} 處", $"{count} 件を置換しました"));
        };
    }

    private async Task OpenSearchAsync()
    {
        var tab = _current;
        if (tab is null) return;
        var selected = tab.IsPreviewMode ? await _preview.GetSelectionAsync() : _editor.SelectedText;
        if (_current != tab) return;
        if (!string.IsNullOrEmpty(selected)) SearchBox.Text = selected.Split(['\r', '\n'])[0];
        SearchPanel.Visibility = Visibility.Visible;
        ExpandReplaceButton.Visibility = tab.IsPreviewMode ? Visibility.Collapsed : Visibility.Visible;
        if (tab.IsPreviewMode) ReplacePanel.Visibility = Visibility.Collapsed;
        await FindAsync(restart: true);
        SearchBox.Focus(); SearchBox.SelectAll();
    }

    private async Task FindAsync(bool backwards = false, bool restart = false)
    {
        if (_current is null) return;
        if (_current.IsPreviewMode) await _preview.FindAsync(SearchBox.Text, Settings.MatchCase, backwards, restart);
        else _editor.Find(SearchBox.Text, Settings.MatchCase, backwards, restart);
    }

    private async Task CloseSearchAsync()
    {
        SearchPanel.Visibility = ReplacePanel.Visibility = Visibility.Collapsed;
        var view = _currentView;
        if (view is null) return;
        view.Editor.Find("", Settings.MatchCase);
        await view.Preview.FindAsync("", Settings.MatchCase);
        if (ReferenceEquals(view, _currentView) && _current?.IsPreviewMode == false) view.Editor.FocusEditor();
    }

    private void ShowSearchResult(int index, int count, bool wrapped)
    {
        _searchCount = count;
        SearchCountLabel.Text = $"{index} / {count}";
        if (wrapped) Toast(T("Search wrapped around", "已循環繼續搜尋", "先頭または末尾から検索を続けます"));
    }

    private async void OnKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (modifiers.HasFlag(ModifierKeys.Alt)) return;
        string? command = null;
        if (ctrl)
        {
            command = e.Key switch
            {
                Key.N => "Ctrl+N", Key.O => "Ctrl+O", Key.S => shift ? "Ctrl+Shift+S" : "Ctrl+S",
                Key.F => "Ctrl+F", Key.W => "Ctrl+W", Key.E => "Ctrl+E", Key.Tab => shift ? "Ctrl+Shift+Tab" : "Ctrl+Tab", _ => null
            };
        }
        else command = e.Key switch { Key.F11 => "F11", Key.F3 => shift ? "Shift+F3" : "F3", Key.Escape => "Escape", _ => null };
        if (command is null) return;
        e.Handled = true;
        await GuardAsync(() => ShortcutAsync(command));
    }

    private async Task ShortcutAsync(string command)
    {
        switch (command)
        {
            case "Ctrl+N": NewDocument(); break;
            case "Ctrl+O": await OpenDialogAsync(); break;
            case "Ctrl+S": if (_current is not null) await SaveAsync(_current); break;
            case "Ctrl+Shift+S": if (_current is not null) await SaveAsync(_current, true); break;
            case "Ctrl+F": await OpenSearchAsync(); break;
            case "Ctrl+W": if (_current is not null) await CloseDocumentAsync(_current); break;
            case "Ctrl+E": await ToggleModeAsync(); break;
            case "Ctrl+Tab": CycleTab(1); break;
            case "Ctrl+Shift+Tab": CycleTab(-1); break;
            case "F3": case "Shift+F3":
                if (!SearchPanel.IsVisible) await OpenSearchAsync();
                else await FindAsync(command == "Shift+F3");
                break;
            case "F11": ToggleFullScreen(); break;
            case "Escape":
                if ((_previewOverlay || _current?.IsPreviewMode == true) && await _preview.CloseOverlayAsync()) { _previewOverlay = false; }
                else if (ReplacePanel.IsVisible) ReplacePanel.Visibility = Visibility.Collapsed;
                else if (SearchPanel.IsVisible) await CloseSearchAsync();
                else if (_flyout?.IsOpen == true) _flyout.IsOpen = false;
                else if (_currentView?.Editor.DismissSelectionToolbar() == true) { }
                else if (_fullScreen) ToggleFullScreen();
                break;
        }
    }

    private void CycleTab(int direction)
    {
        if (Documents.Count < 2) return;
        var index = _current is null ? 0 : Documents.IndexOf(_current);
        SelectDocument(Documents[(index + direction + Documents.Count) % Documents.Count]);
    }
}
