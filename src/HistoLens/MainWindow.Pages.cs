using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace HistoLens;

public sealed partial class MainWindow
{
    private Grid _magnifierPage = null!, _dataManagementPage = null!;
    private Button _magnifierNavigation = null!, _dataNavigation = null!;
    private Button _inventoryOpen = null!, _inventoryDelete = null!, _inventoryRefresh = null!;
    private WrapPanel _stockActions = null!, _dataActions = null!;
    private readonly DataGrid _inventory = Table();
    private readonly TextBlock _inventorySummary = Text();
    private string? _loadedMarketDataPath;
    private int _marketDataRefreshGeneration;

    private void BuildPages(Grid root)
    {
        var navigation = new WrapPanel();
        _magnifierNavigation = Button("Magnifier", "放大鏡", "拡大鏡", (_, _) => ShowDataManagement(false));
        _dataNavigation = Button("Data management", "資料管理", "データ管理", (_, _) => ShowDataManagement(true));
        navigation.Children.Add(_magnifierNavigation); navigation.Children.Add(_dataNavigation);
        HeaderActions = navigation;
        _dataManagementPage = BuildDataManagementPage();
        Grid.SetRow(_dataManagementPage, 1); root.Children.Add(_dataManagementPage);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        _status.Margin = new Thickness(12, 4, 0, 4); _status.VerticalAlignment = VerticalAlignment.Center;
        _status.FontSize = 12;
        footer.Children.Add(_status); Grid.SetRow(footer, 2); root.Children.Add(footer);
        ShowDataManagement(false);
    }

    private Grid BuildDataManagementPage()
    {
        var page = new Grid();
        page.ColumnDefinitions.Add(new() { Width = new GridLength(245) });
        page.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var downloadInputs = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        BuildDownloadInputs(downloadInputs);
        _dataActions = new WrapPanel(); downloadInputs.Children.Add(_dataActions);
        var scroller = new ScrollViewer { Content = downloadInputs, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroller.SetResourceReference(StyleProperty, "SidebarScrollViewerStyle"); page.Children.Add(scroller);
        var saved = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        saved.RowDefinitions.Add(new() { Height = GridLength.Auto });
        saved.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        saved.RowDefinitions.Add(new() { Height = GridLength.Auto });
        saved.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetColumn(saved, 1); page.Children.Add(saved);
        Bind(UpdateInventorySummary); saved.Children.Add(_inventorySummary);
        Column(_inventory, nameof(MarketDataEntry.Code), "Code", "代碼", "コード");
        Column(_inventory, nameof(MarketDataEntry.Name), "Stock", "股票", "銘柄");
        Column(_inventory, nameof(MarketDataEntry.CoverageStart), "Saved from", "保存起日", "保存開始日");
        Column(_inventory, nameof(MarketDataEntry.CoverageEnd), "Saved through", "保存迄日", "保存終了日");
        Column(_inventory, nameof(MarketDataEntry.FirstPriceDate), "First price", "行情首日", "最初の行情");
        Column(_inventory, nameof(MarketDataEntry.LastPriceDate), "Last price", "行情末日", "最後の行情");
        Column(_inventory, nameof(MarketDataEntry.BarCount), "Daily rows", "日線筆數", "日足件数", numeric: true);
        Column(_inventory, nameof(MarketDataEntry.Market), "Market", "市場", "市場");
        Column(_inventory, nameof(MarketDataEntry.SourceId), "Source", "來源", "取得元");
        ((Binding)((DataGridTextColumn)_inventory.Columns[7]).Binding).Converter = new DisplayValueConverter(value => MarketLabel(value as string ?? ""));
        ((Binding)((DataGridTextColumn)_inventory.Columns[8]).Binding).Converter = new DisplayValueConverter(value => SourceLabel(value as string));
        foreach (var column in _inventory.Columns.OfType<DataGridTextColumn>().Skip(2).Take(4))
            if (column.Binding is System.Windows.Data.Binding binding) binding.StringFormat = "{0:yyyy-MM-dd}";
        Grid.SetRow(_inventory, 1); saved.Children.Add(_inventory);
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _inventoryOpen = Button("Open in Magnifier", "在放大鏡開啟", "拡大鏡で開く", async (_, _) =>
        {
            if (_inventory.SelectedItem is MarketDataEntry entry && await OpenMarketDataFromUi(entry.Path)) ShowDataManagement(false);
        });
        _inventoryDelete = Button("Delete selected data…", "刪除所選資料…", "選択データを削除…", async (_, _) => await DeleteSelectedDataFromUi());
        _inventoryRefresh = Button("Refresh list", "重新整理清單", "一覧を更新", async (_, _) => await RefreshMarketDataAsync());
        actions.Children.Add(_inventoryOpen); actions.Children.Add(_inventoryDelete); actions.Children.Add(_inventoryRefresh);
        Grid.SetRow(actions, 2); saved.Children.Add(actions);
        var note = Text(); note.FontSize = 12; note.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Bind(() => note.Text = T("Saved dates show the outer range; gaps may remain. Price dates include usable traded closes only. Check Data quality in Magnifier before scanning.",
            "保存起迄日表示資料涵蓋的外圍範圍，區間內仍可能有缺漏。行情首末日只計有有效收盤價的交易日；掃描前可在放大鏡查看資料品質。",
            "保存期間内に欠損がある場合があります。行情の初日・末日は有効な終値のある取引日です。検索前に拡大鏡のデータ品質を確認してください。"));
        Grid.SetRow(note, 3); saved.Children.Add(note);
        _inventory.SelectionChanged += (_, _) => { if (_inventoryOpen is not null) SetMarketDataEnabled(_work is not null); };
        _marketData.SelectionChanged += (_, _) => { if (_inventoryOpen is not null) SetMarketDataEnabled(_work is not null); };
        return page;
    }

    private void ShowDataManagement(bool show)
    {
        _magnifierPage.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _dataManagementPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _scanButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        var cancelParent = (Panel)_cancel.Parent;
        var cancelTarget = show ? _dataActions : _stockActions;
        if (cancelParent != cancelTarget) { cancelParent.Children.Remove(_cancel); cancelTarget.Children.Add(_cancel); }
        foreach (var (button, active) in new[] { (_magnifierNavigation, !show), (_dataNavigation, show) })
        {
            button.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            if (active) button.SetResourceReference(Control.BackgroundProperty, "HoverBrush");
            else button.ClearValue(Control.BackgroundProperty);
        }
    }

    private void UpdateInventorySummary()
    {
        var count = _inventory.Items.Cast<MarketDataEntry>().Count(entry => !entry.IsSynthetic);
        _inventorySummary.Text = T($"Saved stocks: {count} · Built-in synthetic sample",
            $"已保存股票：{count} 支 · 另有內建合成測試資料", $"保存済み銘柄：{count} 件 · 内蔵合成サンプル");
    }

    private static bool SameDataPath(string? first, string? second) => first is not null && second is not null &&
        (first == SyntheticDataKey || second == SyntheticDataKey ? first == second :
            string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase));

    private sealed class DisplayValueConverter(Func<object, string> display) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => display(value);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private async Task DeleteSelectedDataFromUi()
    {
        if (_inventory.SelectedItem is not MarketDataEntry { IsSynthetic: false } entry || _work is not null) return;
        var message = $"{entry.Market} · {entry.Code} · {entry.Name}\n{entry.CoverageStart:yyyy-MM-dd} → {entry.CoverageEnd:yyyy-MM-dd}\n\n" + T(
            "Delete this stock's saved market data? Saved research snapshots and legacy backups are retained.",
            "確定刪除這支股票的已保存行情？已保存的研究快照與舊版備份會保留。",
            "この銘柄の保存行情を削除しますか？保存済み研究と旧版バックアップは保持されます。");
        if (MessageBox.Show(this, message, T("Delete market data", "刪除行情資料", "行情データを削除"),
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { await DeleteMarketDataAsync(entry.Path); }
        catch (OperationCanceledException) { /* The workflow already reported cancellation. */ }
        catch (Exception) { /* The workflow already reported failure. */ }
    }

    public async Task DeleteMarketDataAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_closed) return;
        if (path == SyntheticDataKey) throw new ArgumentException("The built-in synthetic sample is not a saved stock file.", nameof(path));
        if (_work is not null) throw new InvalidOperationException("Wait for the current operation before deleting data.");
        InvalidateWork(); var generation = _generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _work = cancellation; SetEnabled();
        try
        {
            await _marketDataStore.DeleteAsync(path, cancellation.Token);
            if (_closed) return;
            // Deletion has committed. Clear any loaded copy of that file and refresh even if Cancel arrives now.
            if (SameDataPath(_loadedMarketDataPath, path))
            {
                _loadedMarketDataPath = null; Snapshot = null; Result = null;
                _stale = false; _attemptIssues = null; _dataDiagnostics = [];
                ResetSimilarity(); UpdateDataInfo(); RenderResult();
            }
            await RefreshMarketDataAsync();
            if (!_closed && generation == _generation)
                SetStatus(() => T("Selected market data deleted.", "已刪除所選行情資料。", "選択した行情データを削除しました。"));
        }
        catch (OperationCanceledException)
        {
            if (!_closed && generation == _generation) SetStatus(() => T("Deletion cancelled.", "已取消刪除。", "削除をキャンセルしました。"));
            throw;
        }
        catch (Exception error)
        {
            if (!_closed && generation == _generation) SetStatus(() => T("Deletion failed: ", "刪除失敗：", "削除エラー：") + error.Message);
            throw;
        }
        finally { if (ReferenceEquals(_work, cancellation)) _work = null; if (!_closed) SetEnabled(); }
    }
}
