using System.Globalization;
using System.IO;
using System.Windows.Controls;
using System.Windows.Markup;
using HistoLens.Core;
using HistoLens.Data;
using Microsoft.Win32;

namespace HistoLens;

public sealed partial class MainWindow
{
    private readonly IHistoricalDataProvider _dataProvider;
    private readonly MarketDataStore _marketDataStore;
    private readonly TextBox _stockCode = new();
    private readonly DatePicker _downloadStart = new(), _downloadEnd = new();
    private readonly ComboBox _marketData = new();
    private readonly ComboBox _downloadMarket = new();
    private readonly TextBlock _downloadSourceInfo = Text();
    private Button _download = null!, _openMarketData = null!;
    private const string SyntheticDataKey = "builtin:synthetic";
    private bool _syncingMarketSelection;
    private TextBlock? _sourceMethods;
    private IReadOnlyList<DataIssue> _dataDiagnostics = [];

    private void BuildSavedDataInputs(Panel settings)
    {
        AddInput(settings, _marketData, "Saved stock", "已保存股票", "保存済み銘柄");
        _marketData.DisplayMemberPath = nameof(SavedItem.Label);
        _marketData.SelectionChanged += async (_, _) =>
        {
            if (_syncingMarketSelection || _closed || _marketData.SelectedItem is not SavedItem item) return;
            if (SameDataPath(item.Path, _loadedMarketDataPath) || item.Path == SyntheticDataKey && Snapshot?.IsSynthetic == true) return;
            if (!await OpenMarketDataFromUi(item.Path) && ReferenceEquals(_marketData.SelectedItem, item))
                SelectMarketData(Snapshot?.IsSynthetic == true ? SyntheticDataKey : _loadedMarketDataPath);
        };
    }

    private void BuildDownloadInputs(Panel settings)
    {
        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")).DateTime).AddDays(-1);
        _downloadStart.SelectedDate = yesterday.AddYears(-1).ToDateTime(TimeOnly.MinValue);
        _downloadEnd.SelectedDate = yesterday.ToDateTime(TimeOnly.MinValue);
        Bind(() => _downloadSourceInfo.Text = T("Download source: FinMind", "下載來源：FinMind", "取得元：FinMind"));
        settings.Children.Add(_downloadSourceInfo);
        foreach (var market in new[] { "TWSE", "TPEx" })
        {
            var item = new ComboBoxItem { Tag = market };
            Bind(() => item.Content = MarketLabel(market));
            _downloadMarket.Items.Add(item);
        }
        _downloadMarket.SelectedIndex = 0;
        AddInput(settings, _downloadMarket, "Market", "市場", "市場");
        BuildStockCatalogInputs(settings);
        AddInput(settings, _downloadStart, "Download start", "下載起日", "取得開始日");
        AddInput(settings, _downloadEnd, "Download end", "下載迄日", "取得終了日");
        foreach (var input in new[] { _downloadStart, _downloadEnd })
        {
            ConfigureDateInput(input, DownloadInputChanged);
        }
        _download = Button("Download missing data", "下載缺少資料", "不足データを取得", async (_, _) => await DownloadFromUi());
        settings.Children.Add(_download);
        var help = Text(); help.FontSize = 12;
        Bind(() => help.Text = T("Select a stock and date range. FinMind downloads only missing ranges after checking saved coverage and internal gaps. Volume is in shares.",
            "選擇股票與起迄日期，先檢查已保存範圍及中間缺口，透過 FinMind 只下載缺少區間。成交量單位為股。",
            "銘柄と期間を選択します。保存範囲と途中の欠損を確認し、不足期間だけFinMindから取得します。出来高は株数です。"));
        settings.Children.Add(help);
        _openMarketData = Button("Open market data file…", "開啟行情檔案…", "行情ファイルを開く…", async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "HistoLens market data (*.market-data.json;*.twse-data.json)|*.market-data.json;*.twse-data.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true && await OpenMarketDataFromUi(dialog.FileName)) ShowDataManagement(false);
        });
        settings.Children.Add(_openMarketData);
        _stockCode.TextChanged += (_, _) => StockCodeChanged();
        _downloadMarket.SelectionChanged += (_, _) => StockMarketChanged();
    }

    private void DownloadInputChanged()
    {
        if (_updating) return;
        InvalidateWork();
        SetStatus(() => T("Download settings changed; current data and research retained.", "下載設定已變更；保留目前資料與研究。", "取得設定を変更しました。現在のデータと研究を保持します。"));
        SetEnabled();
    }

    private void ConfigureDateInput(DatePicker input, Action changed)
    {
        input.SelectedDateFormat = DatePickerFormat.Short;
        Bind(() => UpdateDateLanguage(input));
        input.SelectedDateChanged += (_, _) => changed();
        input.DateValidationError += (_, error) =>
        {
            error.ThrowException = false;
            // Do not allow WPF's invalid-text rollback to resubmit a previously valid day.
            input.SelectedDate = null;
            SetStatus(() => T("Invalid date; select or enter a valid date.", "日期有誤，請選擇或輸入有效日期。", "日付が無効です。有効な日付を選択または入力してください。"));
        };
    }

    private void UpdateDateLanguage(DatePicker input)
    {
        var culture = CultureInfo.GetCultureInfo(T("en-US", "zh-TW", "ja-JP"));
        if (input.Language.IetfLanguageTag == culture.Name) return;
        var updating = _updating;
        _updating = true;
        try
        {
            var selected = input.SelectedDate;
            input.Language = XmlLanguage.GetLanguage(culture.Name);
            if (selected.HasValue) input.Text = selected.Value.ToString("d", culture);
        }
        finally { _updating = updating; }
    }
    public async Task DownloadMarketDataAsync(HistoricalDataRequest request, CancellationToken cancellationToken = default)
    {
        if (_closed) return;
        InvalidateWork(); var generation = _generation;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _work = cancellation; SetEnabled();
        SetStatus(() => T("Checking saved dates before downloading…", "下載前逐日比對本機資料…", "取得前に保存済みの日付を確認中…"));
        var progress = new Progress<DownloadProgress>(value =>
        {
            if (!_closed && generation == _generation && ReferenceEquals(_work, cancellation) && !cancellation.IsCancellationRequested)
                SetStatus(() => T("Downloading ", "下載中 ", "取得中 ") + $"{value.Completed}/{value.Total} · {value.Message}");
        });
        try
        {
            var cache = await new CachedMarketDataDownloader(_dataProvider, _marketDataStore)
                .DownloadAsync(request, progress, cancellation.Token);
            var download = cache.Download;
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            var presentation = await Task.Run(() => PrepareResult(null, download.Snapshot, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            await RefreshMarketDataAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            CommitMarketData(download, presentation);
            _loadedMarketDataPath = Path.GetFullPath(cache.Path);
            SelectMarketData(cache.Path);
            SetMarketDataLoadedStatus(cache);
        }
        catch (OperationCanceledException)
        {
            if (!_closed && generation == _generation)
                SetStatus(() => T("Download cancelled; previous data and research retained.", "已取消下載，保留原資料與研究。", "取得をキャンセルしました。前回データと研究を保持します。"));
            throw;
        }
        catch (Exception error)
        {
            if (!_closed && generation == _generation)
                SetStatus(() => T("Download failed; previous data and research retained: ", "下載失敗，保留原資料與研究：", "取得エラー。前回データと研究を保持：") + error.Message);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) _work = null;
            cancellation.Dispose(); if (!_closed) SetEnabled();
        }
    }

    public async Task LoadMarketDataAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_closed) return;
        InvalidateWork(); var generation = _generation;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _work = cancellation; SetEnabled();
        SetStatus(() => T("Opening saved market data…", "開啟離線行情中…", "保存した行情を読込中…"));
        try
        {
            var download = await MarketDataStore.LoadAsync(path, cancellation.Token);
            var presentation = await Task.Run(() => PrepareResult(null, download.Snapshot, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            CommitMarketData(download, presentation);
            _loadedMarketDataPath = Path.GetFullPath(path);
            SelectMarketData(path);
            SetMarketDataLoadedStatus();
        }
        catch (OperationCanceledException)
        {
            if (!_closed && generation == _generation)
                SetStatus(() => T("Open cancelled; previous work retained.", "已取消開啟，保留原工作。", "読込をキャンセルしました。前回の作業を保持します。"));
            throw;
        }
        catch (Exception error)
        {
            if (!_closed && generation == _generation)
                SetStatus(() => T("Open failed; previous work retained: ", "開啟失敗，保留原工作：", "読込エラー。前回の作業を保持：") + error.Message);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) _work = null;
            cancellation.Dispose(); if (!_closed) SetEnabled();
        }
    }

    private void CommitMarketData(HistoricalDataDownload download, ResultPresentation presentation)
    {
        var snapshot = download.Snapshot;
        var template = (ResearchTemplate)((ComboBoxItem)_template.SelectedItem).Tag;
        var lookback = int.TryParse(_lookback.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var requestedLookback)
            && requestedLookback is >= 1 and <= ResearchEngine.MaxLookback ? requestedLookback : template.DefaultLookback;
        var eventStart = snapshot.Calendar.TradingDates.Count == 0 ? snapshot.Calendar.CoverageStart
            : snapshot.Calendar.TradingDates.Order().ElementAt(Math.Min(lookback + 1, snapshot.Calendar.TradingDates.Count - 1));
        _updating = true;
        try
        {
            _stockCode.Text = snapshot.Instrument.Code;
            SelectDownloadMarket(snapshot.Instrument.Market);
            SyncStockSelection(allowMarketChange: false);
            _downloadStart.SelectedDate = snapshot.Calendar.CoverageStart.ToDateTime(TimeOnly.MinValue);
            _downloadEnd.SelectedDate = snapshot.Calendar.CoverageEnd.ToDateTime(TimeOnly.MinValue);
            _start.Text = eventStart.ToString("yyyy-MM-dd");
            _end.Text = _asOf.Text = snapshot.DataAsOf.ToString("yyyy-MM-dd");
            Snapshot = snapshot; Result = null; _stale = false; _attemptIssues = null;
            _dataDiagnostics = download.Diagnostics;
            ResetSimilarity(); UpdateDataInfo(); RenderResult(presentation);
        }
        finally { _updating = false; }
    }

    private void SetMarketDataLoadedStatus(CachedMarketDataResult? cache = null)
    {
        var blocked = MergeIssues(_dataDiagnostics, SnapshotValidator.Validate(Snapshot!)).Select(SimilarityEngine.ForScan).Any(issue => issue.BlocksResearch);
        SetStatus(() => (cache is null ? "" : cache.UsesDateRanges ? T(
                $"Reused {cache.ReusedDays} days; fetched {cache.DownloadedRanges} missing ranges; added {cache.AddedBars}, repaired {cache.RepairedBars} bars. ",
                $"已略過 {cache.ReusedDays} 天；下載 {cache.DownloadedRanges} 段缺少區間，新增 {cache.AddedBars} 筆、補齊 {cache.RepairedBars} 筆日線。",
                $"{cache.ReusedDays} 日を再利用、{cache.DownloadedRanges} 件の不足期間を取得。{cache.AddedBars} 行追加、{cache.RepairedBars} 行修復。") : T(
                $"Reused {cache.ReusedDays} days; fetched {cache.DownloadedMonths} missing months; added {cache.AddedBars}, repaired {cache.RepairedBars} bars. ",
                $"已略過 {cache.ReusedDays} 天；補查 {cache.DownloadedMonths} 個月份，新增 {cache.AddedBars} 筆、補齊 {cache.RepairedBars} 筆日線。",
                $"{cache.ReusedDays} 日を再利用、{cache.DownloadedMonths} か月を補完。{cache.AddedBars} 行追加、{cache.RepairedBars} 行修復。")) + (blocked
            ? T("Data saved locally. Source checks block scanning; see Data quality.", "資料已保存到本機；來源檢查未通過，請看資料品質。", "データを保存しました。データ源の検証が不十分です。データ品質を確認してください。")
            : T("Data saved locally. Scan the raw prices; known events are annotated and unknown coverage remains a warning.", "資料已保存到本機；可用原始價格掃描，已知事件標日期，覆蓋未知會顯示提醒。", "データを保存しました。原価格で検索し、既知のイベントは日付で注記、網羅性が未確認の場合は警告を表示します。")));
    }

    private async Task DownloadFromUi()
    {
        HistoricalDataRequest request;
        try
        {
            var start = SelectedDay(_downloadStart);
            var end = SelectedDay(_downloadEnd);
            if (start > end) throw new ArgumentException(T("Download start must not be after the end.", "下載起日不得晚於迄日。", "取得開始日は終了日以前にしてください。"));
            if (_downloadMarket.SelectedItem is not ComboBoxItem { Tag: string market })
                throw new ArgumentException(T("Select a market.", "請選擇市場。", "市場を選択してください。"));
            request = new(_stockCode.Text.Trim(), start, end, market);
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { SetStatus(() => T("Invalid download settings: ", "下載設定有誤：", "取得設定エラー：") + error.Message); return; }
        try { await DownloadMarketDataAsync(request); }
        catch (OperationCanceledException) { /* The workflow has already reported cancellation. */ }
        catch (Exception) { /* The workflow has already reported the failure without replacing work. */ }
    }

    private DateOnly SelectedDay(DatePicker input)
    {
        var culture = input.Language.GetSpecificCulture();
        if (!input.SelectedDate.HasValue || string.IsNullOrWhiteSpace(input.Text) || !DateTime.TryParse(input.Text, culture, DateTimeStyles.None, out var selected))
        {
            input.SelectedDate = null;
            throw new FormatException(T("Select or enter both start and end dates.", "請選擇或輸入起日與迄日。", "開始日と終了日を選択または入力してください。"));
        }
        input.SelectedDate = selected.Date;
        return DateOnly.FromDateTime(input.SelectedDate.Value);
    }

    private async Task<bool> OpenMarketDataFromUi(string path)
    {
        if (path == SyntheticDataKey) { LoadDemo(); return true; }
        var generation = _generation + 1;
        try { await LoadMarketDataAsync(path); return !_closed && generation == _generation; }
        catch (OperationCanceledException) { return false; }
        catch (Exception) { return false; }
    }

    public async Task RefreshMarketDataAsync(CancellationToken cancellationToken = default)
    {
        var refresh = ++_marketDataRefreshGeneration;
        var selectedPath = (_inventory.SelectedItem as MarketDataEntry)?.Path;
        var stockPath = (_marketData.SelectedItem as SavedItem)?.Path ?? (Snapshot?.IsSynthetic == true ? SyntheticDataKey : _loadedMarketDataPath);
        try
        {
            var entries = await _marketDataStore.ListEntriesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_closed || refresh != _marketDataRefreshGeneration) return;
            var demo = DemoData.Create();
            var inventory = entries.Append(new MarketDataEntry(SyntheticDataKey, demo.Instrument.Code,
                demo.Instrument.Name, demo.Calendar.CoverageStart, demo.Calendar.CoverageEnd,
                demo.Calendar.TradingDates.Min(), demo.Calendar.TradingDates.Max(), demo.Bars.Count, false, true, demo.Instrument.Market)).ToArray();
            _syncingMarketSelection = true;
            try
            {
                _inventory.ItemsSource = inventory;
                _inventory.SelectedItem = inventory.FirstOrDefault(entry => SameDataPath(entry.Path, selectedPath)) ?? inventory.First();
                _marketData.ItemsSource = inventory.Select(entry => new SavedItem(entry.Path,
                    (entry.IsSynthetic ? "" : SourceLabel(entry.SourceId) + " · " + MarketLabel(entry.Market) + " · ") + $"{entry.Code} · {entry.Name}")).ToArray();
                _marketData.SelectedItem = _marketData.Items.Cast<SavedItem>().FirstOrDefault(item => SameDataPath(item.Path, stockPath));
            }
            finally { _syncingMarketSelection = false; }
            UpdateInventorySummary(); SetEnabled();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!_closed && refresh == _marketDataRefreshGeneration)
            {
                _syncingMarketSelection = true;
                try { _inventory.ItemsSource = null; _marketData.ItemsSource = null; }
                finally { _syncingMarketSelection = false; }
                _inventorySummary.Text = T("Unable to read saved data: ", "無法讀取已保存資料：", "保存データを読めません：") + error.Message;
                SetStatus(() => error.Message); SetEnabled();
            }
        }
    }

    private void SelectMarketData(string? path)
    {
        _syncingMarketSelection = true;
        try
        {
            _marketData.SelectedItem = _marketData.Items.Cast<SavedItem>().FirstOrDefault(item => SameDataPath(item.Path, path));
            _inventory.SelectedItem = _inventory.Items.Cast<MarketDataEntry>().FirstOrDefault(item => SameDataPath(item.Path, path));
        }
        finally { _syncingMarketSelection = false; }
    }

    private void UpdateSavedStockLabels()
    {
        var selectedPath = (_marketData.SelectedItem as SavedItem)?.Path;
        _syncingMarketSelection = true;
        try
        {
            _marketData.ItemsSource = _inventory.Items.Cast<MarketDataEntry>().Select(entry => new SavedItem(entry.Path,
                (entry.IsSynthetic ? "" : SourceLabel(entry.SourceId) + " · " + MarketLabel(entry.Market) + " · ") + $"{entry.Code} · {entry.Name}")).ToArray();
            _marketData.SelectedItem = _marketData.Items.Cast<SavedItem>().FirstOrDefault(item => SameDataPath(item.Path, selectedPath));
        }
        finally { _syncingMarketSelection = false; }
    }

    private void SetMarketDataEnabled(bool busy)
    {
        _download.IsEnabled = _openMarketData.IsEnabled = !busy;
        _marketData.IsEnabled = !busy;
        _inventoryOpen.IsEnabled = !busy && _inventory.SelectedItem is MarketDataEntry;
        _inventoryDelete.IsEnabled = !busy && _inventory.SelectedItem is MarketDataEntry { IsSynthetic: false };
        _inventoryRefresh.IsEnabled = !busy;
        _stockCode.IsEnabled = _downloadStart.IsEnabled = _downloadEnd.IsEnabled = !busy;
        _downloadMarket.IsEnabled = !busy;
        _stockSelector.IsEnabled = !busy && _stockCatalog is not null;
        _refreshStockCatalog.IsEnabled = !busy && !_catalogLoading && _stockCatalogProvider is not null;
    }
    private string MarketLabel(string market) => market == "TPEx"
        ? T("OTC", "上櫃", "店頭")
        : market == "TWSE" ? T("Listed", "上市", "上場") : market;

    private string SourceLabel(string? source) => source switch
    {
        "FinMind" => "FinMind",
        "TWSE" or "TPEx" => T("Legacy local data", "本機舊資料", "旧ローカルデータ"),
        _ => source ?? ""
    };

    private void SelectDownloadMarket(string market)
    {
        var item = _downloadMarket.Items.Cast<ComboBoxItem>().FirstOrDefault(value => (string)value.Tag == market);
        if (item is not null) _downloadMarket.SelectedItem = item;
    }
    private static IReadOnlyList<DataIssue> MergeIssues(IReadOnlyList<DataIssue> first, IReadOnlyList<DataIssue> second) => first.Concat(second).Distinct().ToArray();

    private string SourceKind(bool synthetic) => synthetic
        ? T("SYNTHETIC TEST DATA", "合成測試資料", "合成テストデータ")
        : Snapshot?.SourceId == "FinMind" ? T("FinMind HISTORICAL DATA", "FinMind 歷史資料", "FinMind 履歴データ")
        : T("LEGACY LOCAL DATA", "本機舊資料", "旧ローカルデータ");

    private void UpdateSourceNotice()
    {
        _notice.Text = Snapshot is null
            ? T("006 · FinMind listed / OTC daily data on request · Analysis also fetches the latest P/E. Synthetic data stays offline.", "006 · FinMind 上市／上櫃日線資料 · 點擊下載才連線取得日線；分析時另查本益比。合成資料維持離線。", "006 · FinMindの上場・店頭日足は取得操作時のみ。分析時に最新PERも取得します。合成データはオフラインです。")
            : "006 · " + SourceKind(Snapshot.IsSynthetic) + (Snapshot.IsSynthetic
                ? T(" · Generated prices, not a real stock.", " · 產生的測試價格，非真實股票。", " · 生成した価格。実際の銘柄ではありません。")
                : T(" · Check Data quality before research.", " · 研究前請確認資料品質。", " · 研究前にデータ品質を確認してください。"));
        if (_sourceMethods is not null) _sourceMethods.Text = SourceMethodsText();
    }

    private string SourceMethodsText() => (Snapshot?.IsSynthetic == true
        ? T("Current source: synthetic sample. Prices, volume, calendar and actions are generated test data.\n\n", "目前來源：合成測試資料。價格、成交量、日曆及公司行動皆為產生的測試內容。\n\n", "現在のデータ源：合成サンプル。価格、出来高、取引日、企業行動は生成値です。\n\n")
        : Snapshot is { SourceId: not "FinMind" } ? T(
            "Current source: legacy local data. Its original source and security identity remain in the saved file. All new downloads, stock-list updates and latest P/E requests use FinMind.\n\n",
            "目前來源：本機舊資料。保存檔保留原始來源與證券身分；新增下載、股票清單更新及最新本益比查詢都使用 FinMind。\n\n",
            "現在の取得元：旧ローカルデータ。元の取得元と銘柄識別情報を保存ファイルに保持します。新規取得・銘柄一覧更新・最新PER照会はFinMindを使用します。\n\n")
        : T("Source: FinMind. Download raw daily prices, share volumes, a separate trading calendar and available events on request. Check saved coverage and internal gaps first, then fetch only missing ranges. Current stock-list updates and the latest published P/E also use FinMind.\n\n",
            "資料來源：FinMind。按下載才取得原始日線、成交股數、獨立交易日曆及可取得事件；先檢查本機範圍與中間缺口，只補缺少區間。股票清單更新及分析時查詢最新公布本益比也使用 FinMind。\n\n",
            "取得元：FinMind。原価格、出来高（株数）、独立した取引日、取得可能なイベントを取得操作時に読み込みます。保存範囲と途中の欠損を確認し、不足期間だけ取得します。銘柄一覧更新と最新公表PERの照会もFinMindを使用します。\n\n")) + T(
            "Raw prices are not adjusted. Similarity scans retain windows crossing known events and annotate their dates; unknown event coverage stays visible. This is not a complete corporate-action catalogue. Missing prices or unverified calendars are not filled or inferred. Advanced condition research retains its conservative exclusions.\n\nFinMind volume is already in shares.\n\nChanging settings retains the previous result and marks it stale. Research describes historical cases, not predictions or trading instructions.",
            "使用未還原原價。相似度掃描保留跨越已知事件的窗口並按日期註記；公司行動覆蓋未知會提示，不宣稱完整事件目錄。缺價不補零、未確認日曆不推算；進階條件研究保留其保守排除政策。\n\nFinMind 成交量已是股，不再乘千。\n\n修改設定保留前次結果並標示未重跑。結果是歷史案例描述，不是未來預測或交易指示。",
            "未調整の原価格を使用します。類似検索は既知イベントをまたぐ期間も残し、日付で注記します。網羅性が不明な場合は明示し、完全な企業行動一覧とはしません。欠損価格を補充せず、未確認の取引日を推定しません。詳細条件研究は従来の保守的除外を維持します。\n\nFinMindの出来高は既に株数です。\n\n条件変更時は前回結果を保持して未更新と表示します。履歴の記述であり、予測や売買指示ではありません。");
}
