using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using HistoLens.Data;

namespace HistoLens;

public sealed partial class MainWindow
{
    private readonly IStockCatalogProvider? _stockCatalogProvider;
    private readonly StockCatalogStore _stockCatalogStore;
    private readonly ComboBox _stockSelector = new();
    private readonly TextBlock _stockCatalogInfo = Text();
    private readonly CancellationTokenSource _catalogLoadCancellation = new();
    private Button _refreshStockCatalog = null!;
    private StockCatalog? _stockCatalog;
    private bool _syncingStockSelection, _catalogLoading = true, _catalogUpdating;
    private string? _stockCatalogError;
    internal Task StockCatalogLoadTask { get; private set; } = Task.CompletedTask;
    internal Task StockCatalogRefreshTask { get; private set; } = Task.CompletedTask;

    private void BuildStockCatalogInputs(Panel settings)
    {
        var label = Text(); label.FontSize = 12;
        Bind(() =>
        {
            label.Text = T("Code / stock", "代號／股票", "コード／銘柄");
            AutomationProperties.SetName(_stockCode, T("Common-stock code", "普通股代碼", "普通株コード"));
            AutomationProperties.SetName(_stockSelector, T("Listed and OTC stock list", "上市櫃股票清單", "上場・店頭銘柄一覧"));
        });
        settings.Children.Add(label);
        var row = new Grid { Margin = new Thickness(0, 0, 2, 2) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(66) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _stockCode.MinHeight = 28; _stockCode.Margin = new Thickness(0, 0, 6, 0);
        _stockSelector.MinHeight = 28; _stockSelector.MaxDropDownHeight = 360;
        Bind(() =>
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new DisplayValueConverter(value => value is StockCatalogEntry entry ? StockLabel(entry) : "") });
            _stockSelector.ItemTemplate = new DataTemplate { VisualTree = text };
            _stockSelector.ToolTip = _stockSelector.SelectedItem is StockCatalogEntry entry ? StockLabel(entry) : null;
        });
        _stockSelector.SetResourceReference(StyleProperty, "FontPickerComboBoxStyle");
        _stockSelector.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        VirtualizingPanel.SetIsVirtualizing(_stockSelector, true);
        VirtualizingPanel.SetVirtualizationMode(_stockSelector, VirtualizationMode.Recycling);
        Grid.SetColumn(_stockSelector, 1); row.Children.Add(_stockCode); row.Children.Add(_stockSelector);
        settings.Children.Add(row);
        _stockSelector.SelectionChanged += (_, _) => StockSelected();
        _refreshStockCatalog = Button("Update listed / OTC stock list", "更新上市櫃股票清單", "上場・店頭銘柄一覧を更新", async (_, _) =>
        {
            try { await (StockCatalogRefreshTask = RefreshStockCatalogAsync()); }
            catch (Exception) { /* The operation retains the previous list and presents its failure. */ }
        });
        settings.Children.Add(_refreshStockCatalog);
        _stockCatalogInfo.FontSize = 12;
        _stockCatalogInfo.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Bind(UpdateStockCatalogInfo); settings.Children.Add(_stockCatalogInfo);
    }

    private async Task LoadSavedStockCatalogAsync()
    {
        try
        {
            var catalog = await _stockCatalogStore.LoadAsync(_catalogLoadCancellation.Token);
            if (_closed) return;
            ApplyStockCatalog(catalog);
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            if (!_closed) _stockCatalogError = error.Message;
        }
        finally
        {
            _catalogLoading = false;
            if (!_closed) { UpdateStockCatalogInfo(); SetEnabled(); }
        }
    }

    public async Task RefreshStockCatalogAsync(CancellationToken cancellationToken = default)
    {
        await StockCatalogLoadTask;
        if (_closed) return;
        if (_work is not null) throw new InvalidOperationException("Wait for the current operation before updating the stock list.");
        if (_stockCatalogProvider is null) throw new InvalidOperationException("No stock-list provider is configured.");
        InvalidateWork(); var generation = _generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _work = cancellation; _catalogUpdating = true; _stockCatalogError = null;
        SetEnabled(); UpdateStockCatalogInfo();
        SetStatus(() => T("Updating stock names and codes…", "正在更新股票名稱與代號…", "銘柄名とコードを更新中…"));
        try
        {
            var catalog = await _stockCatalogProvider.GetAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            await _stockCatalogStore.SaveAsync(catalog, cancellation.Token);
            if (_closed || generation != _generation) return;
            ApplyStockCatalog(catalog);
            SetStatus(() => T("Stock list updated; no historical prices downloaded.", "股票清單已更新；尚未下載歷史行情。", "銘柄一覧を更新しました。履歴行情は取得していません。"));
        }
        catch (OperationCanceledException)
        {
            if (!_closed && generation == _generation)
                SetStatus(() => T("Stock-list update cancelled; previous list retained.", "已取消更新股票清單，保留原清單。", "銘柄一覧の更新をキャンセルしました。以前の一覧を保持します。"));
            throw;
        }
        catch (Exception error)
        {
            if (!_closed && generation == _generation)
            {
                _stockCatalogError = error.Message;
                SetStatus(() => T("Stock-list update failed; previous list retained: ", "股票清單更新失敗，保留原清單：", "銘柄一覧の更新に失敗。以前の一覧を保持：") + error.Message);
            }
            throw;
        }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) _work = null;
            _catalogUpdating = false;
            if (!_closed) { UpdateStockCatalogInfo(); SetEnabled(); }
        }
    }

    private void ApplyStockCatalog(StockCatalog? catalog)
    {
        _stockCatalog = catalog;
        _syncingStockSelection = true;
        try
        {
            _stockSelector.ItemsSource = catalog?.Entries.OrderBy(entry => entry.Code, StringComparer.Ordinal)
                .ThenBy(entry => entry.Market, StringComparer.Ordinal).ToArray();
        }
        finally { _syncingStockSelection = false; }
        SyncStockSelection(allowMarketChange: true);
        UpdateStockCatalogInfo();
    }

    private void StockCodeChanged()
    {
        if (_syncingStockSelection || _updating) return;
        SyncStockSelection(allowMarketChange: true);
        DownloadInputChanged();
    }

    private void StockMarketChanged()
    {
        if (_syncingStockSelection || _updating) return;
        SyncStockSelection(allowMarketChange: false);
        DownloadInputChanged();
    }

    private void SyncStockSelection(bool allowMarketChange)
    {
        if (_syncingStockSelection) return;
        var code = _stockCode.Text.Trim();
        var market = (_downloadMarket.SelectedItem as ComboBoxItem)?.Tag as string;
        var matches = _stockCatalog?.Entries.Where(entry => entry.Code == code).ToArray() ?? [];
        var selected = matches.FirstOrDefault(entry => entry.Market == market);
        if (selected is null && allowMarketChange && matches.Length == 1) selected = matches[0];
        _syncingStockSelection = true;
        try
        {
            _stockSelector.SelectedItem = selected;
            _stockSelector.ToolTip = selected is null ? null : StockLabel(selected);
            if (selected is not null && selected.Market != market) SelectDownloadMarket(selected.Market);
        }
        finally { _syncingStockSelection = false; }
    }

    private void StockSelected()
    {
        if (_syncingStockSelection || _updating || _stockSelector.SelectedItem is not StockCatalogEntry entry) return;
        _syncingStockSelection = true;
        try
        {
            _stockCode.Text = entry.Code;
            SelectDownloadMarket(entry.Market);
            _stockSelector.ToolTip = StockLabel(entry);
        }
        finally { _syncingStockSelection = false; }
        DownloadInputChanged();
    }

    private void UpdateStockCatalogInfo()
    {
        var state = _catalogUpdating
            ? T("Updating both markets…", "正在更新上市及上櫃清單…", "上場・店頭の一覧を更新中…")
            : _stockCatalog is null
                ? T("Update the list to choose by name. You can also enter a code directly.", "更新清單後可選股票名稱，也可直接輸入代號。", "一覧更新後に銘柄名で選択できます。コードの直接入力も可能です。")
                : T($"Listed {_stockCatalog.Entries.Count(entry => entry.Market == "TWSE")} · OTC {_stockCatalog.Entries.Count(entry => entry.Market == "TPEx")} · Updated ",
                    $"上市 {_stockCatalog.Entries.Count(entry => entry.Market == "TWSE")} · 上櫃 {_stockCatalog.Entries.Count(entry => entry.Market == "TPEx")} · 更新 ",
                    $"上場 {_stockCatalog.Entries.Count(entry => entry.Market == "TWSE")} · 店頭 {_stockCatalog.Entries.Count(entry => entry.Market == "TPEx")} · 更新 ") +
                    _stockCatalog.UpdatedAtUtc.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm");
        if (_stockCatalogError is not null)
            state += "\n" + T("List unavailable or update failed; previous data retained. ", "清單讀取或更新失敗，保留原資料。", "一覧の読込・更新に失敗。以前のデータを保持。") + _stockCatalogError;
        _stockCatalogInfo.Text = state;
    }

    private string StockLabel(StockCatalogEntry entry) => $"{entry.Code} · {entry.Name} · {MarketLabel(entry.Market)}";
}
