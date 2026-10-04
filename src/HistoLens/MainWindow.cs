using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HistoLens.Core;
using HistoLens.Data;
using Microsoft.Win32;
using ToolKeeper.UI;

namespace HistoLens;

public sealed partial class MainWindow : AppWindow
{
    private readonly ResearchStore _store;
    private readonly List<Action> _translations = [];
    private readonly ComboBox _template = new(), _sampling = new(), _saved = new();
    private readonly TextBox _lookback = new(), _threshold = new(), _start = new(), _end = new(), _asOf = new();
    private readonly TextBox _horizons = new() { Text = "5,10,20,60" }, _upper = new() { Text = "5" }, _lower = new() { Text = "-3" };
    private readonly TextBlock _notice = Text(), _dataInfo = Text(), _formula = Text(), _status = Text(), _summary = Text(), _detail = Text();
    private readonly DataGrid _stats = Table(), _cases = Table(), _quality = Table(), _evaluations = Table(), _bars = Table();
    private readonly DataGrid _marketBars = Table();
    private readonly PriceChart _chart = new() { Height = 120, Margin = new Thickness(0, 4, 0, 4) };
    private Button _runButton = null!, _cancel = null!, _save = null!, _open = null!, _loadSaved = null!;
    private CancellationTokenSource? _work;
    private int _generation;
    private bool _updating, _closed, _stale;
    private IReadOnlyList<DataIssue>? _attemptIssues;
    private Func<string>? _statusText;
    public DataSnapshot? Snapshot { get; private set; }
    public ResearchRun? Result { get; private set; }
    public bool IsResultStale => _stale;

    public MainWindow(string? dataDirectory = null) : this(dataDirectory, new FinMindHistoricalDataProvider(),
        new FinMindValuationProvider(), new FinMindStockCatalogProvider()) { }

    // Injected historical sources stay offline unless an optional valuation or stock-list source is supplied.
    public MainWindow(string? dataDirectory, IHistoricalDataProvider dataProvider, ICurrentValuationProvider? valuationProvider = null,
        IStockCatalogProvider? stockCatalogProvider = null)
    {
        var directory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper", "HistoLens");
        _store = new ResearchStore(Path.Combine(directory, "research"));
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        _valuationProvider = valuationProvider;
        _stockCatalogProvider = stockCatalogProvider;
        _stockCatalogStore = new StockCatalogStore(Path.Combine(directory, "stock-catalog.json"));
        _marketDataStore = new MarketDataStore(Path.Combine(directory, "market-data"));
        PreferencesPath = Path.Combine(directory, "ui.json");
        SelectedTheme = "Ink";
        MainName = "HistoLens";
        Icon = ProductIcon.Source;
        SubName = "Historical Stock Research";
        AboutAuthor = "不告訴你";
        Width = 1280; Height = 880; MinWidth = 900; MinHeight = 660;
        WindowState = WindowState.Maximized;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/HistoLens;component/ResearchStyles.xaml", UriKind.Relative) });
        BuildWorkspace();
        UiPreferencesChanged += (_, _) => Translate();
        Closed += (_, _) => { _closed = true; _generation++; _work?.Cancel(); CancelValuationRefresh(); _catalogLoadCancellation.Cancel(); };
        ApplyUiPreferences();
        RefreshSaved();
        SetStatus(() => T("Choose saved data, or download a stock on the Data management page.", "選取已保存資料，或到「資料管理」下載股票行情。", "保存済みデータを選ぶか、「データ管理」で行情を取得してください。"));
        LoadIndicatorPreferences(directory);
        SetEnabled();
        StockCatalogLoadTask = LoadSavedStockCatalogAsync();
        _ = RefreshMarketDataAsync();
    }

    private void BuildWorkspace()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _notice.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _notice.Margin = new Thickness(0, 0, 0, 10);
        Bind(UpdateSourceNotice);
        root.Children.Add(_notice);
        var body = new Grid();
        body.ColumnDefinitions.Add(new() { Width = new GridLength(265) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _magnifierPage = body; Grid.SetRow(body, 1); root.Children.Add(body);
        var settings = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        var scroller = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroller.SetResourceReference(StyleProperty, "SidebarScrollViewerStyle");
        body.Children.Add(scroller);
        BuildSavedDataInputs(settings);
        _stockActions = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        _scanButton = Button("Find similar periods", "尋找相似區間", "類似期間を検索", async (_, _) => await RunSimilarityAsync());
        _cancel = Button("Cancel", "取消", "キャンセル", (_, _) => { _work?.Cancel(); CancelValuationRefresh(); });
        _stockActions.Children.Add(_scanButton); _stockActions.Children.Add(_cancel);
        settings.Children.Add(_stockActions);
        BuildValuationInfo(settings);
        BuildSimilarityInputs(settings);
        settings.Children.Add(Section(_dataInfo, "Data details", "資料詳情", "データ詳細"));
        var legacySettings = new StackPanel();
        _advancedResearch = new Expander { Content = legacySettings, Visibility = Visibility.Collapsed };
        Bind(() => _advancedResearch.Header = T("Condition research (advanced)", "條件研究（進階）", "条件研究（詳細）"));
        _advancedResearch.Expanded += (_, _) => SetLegacyTabsVisibility();
        _advancedResearch.Collapsed += (_, _) => SetLegacyTabsVisibility();
        settings.Children.Add(_advancedResearch);
        settings = legacySettings;
        foreach (var template in ResearchTemplates.All)
        {
            var item = new ComboBoxItem { Tag = template };
            Bind(() => item.Content = template.Id + " · " + (template.Id switch
            {
                "HL-R001" => T("Near prior low", "接近歷史區間低點", "過去の安値付近"),
                "HL-R002" => T("Consecutive declines", "連續下跌", "連続下落"),
                "HL-R003" => T("Rising price and volume", "量增上漲", "価格と出来高の上昇"),
                _ => T("Close above prior high", "收盤突破前高", "終値が過去高値を上抜け")
            }));
            _template.Items.Add(item);
        }
        _template.SelectionChanged += (_, _) => { if (!_updating) { ApplyTemplateDefaults(); Edited(); } };
        AddInput(settings, _template, "Research template", "研究模板", "研究テンプレート");
        AddInput(settings, _lookback, "Lookback / decline days", "回看日數／連跌日數", "参照日数／続落日数");
        AddInput(settings, _threshold, "Low distance % / volume multiple", "距低點上限 %／相對量倍數", "安値からの上限 %／出来高倍率");
        _formula.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _formula.FontSize = 12; settings.Children.Add(_formula);
        AddInput(settings, _start, "Event start (yyyy-MM-dd)", "事件起日（yyyy-MM-dd）", "イベント開始日（yyyy-MM-dd）");
        AddInput(settings, _end, "Event end (yyyy-MM-dd)", "事件迄日（yyyy-MM-dd）", "イベント終了日（yyyy-MM-dd）");
        AddInput(settings, _asOf, "Data cutoff (yyyy-MM-dd)", "研究截止日（yyyy-MM-dd）", "データ基準日（yyyy-MM-dd）");
        AddInput(settings, _horizons, "Horizons (trading days, comma separated)", "觀察期（交易日，以逗號分隔）", "観察期間（取引日・カンマ区切り）");
        _sampling.ItemsSource = Enum.GetValues<SamplingPolicy>(); _sampling.SelectedIndex = 0;
        AddInput(settings, _sampling, "Sampling", "採樣方式", "サンプリング");
        AddInput(settings, _upper, "Upper threshold %", "上方價格門檻 %", "上方価格しきい値 %");
        AddInput(settings, _lower, "Lower threshold %", "下方價格門檻 %", "下方価格しきい値 %");
        var samplingHelp = Text();
        Bind(() => samplingHelp.Text = T("FirstInRun: first matching day after a known non-match. EveryMatch: all matches. NonOverlapping: skip the longest observation window.",
            "FirstInRun：連續成立取首日。EveryMatch：每個符合日。NonOverlapping：避開最長觀察期重疊。", "FirstInRun：連続一致の初日。EveryMatch：全一致日。NonOverlapping：最長観察期間の重複を除外。"));
        samplingHelp.FontSize = 12; settings.Children.Add(samplingHelp);
        var actions = new WrapPanel();
        _runButton = Button("Run research", "執行研究", "研究を実行", async (_, _) => await RunResearchAsync());
        actions.Children.Add(_runButton);
        _save = Button("Save this completed result", "保存這次完整研究", "完了した研究を保存", async (_, _) => await SaveFromUi());
        actions.Children.Add(_save); settings.Children.Add(actions);
        AddInput(settings, _saved, "Saved research (local)", "已保存研究（本機）", "保存した研究（ローカル）");
        _saved.DisplayMemberPath = nameof(SavedItem.Label);
        _loadSaved = Button("Open selected", "開啟所選研究", "選択した研究を開く", async (_, _) =>
        {
            if (_saved.SelectedItem is SavedItem item) await LoadFromUi(item.Path);
        });
        settings.Children.Add(_loadSaved);
        _open = Button("Open a saved research file…", "開啟研究檔案…", "研究ファイルを開く…", async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "HistoLens (*.histolens.json)|*.histolens.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) await LoadFromUi(dialog.FileName);
        });
        settings.Children.Add(_open);
        var tabs = new TabControl { Margin = new Thickness(8, 0, 0, 0) };
        _resultTabs = tabs;
        tabs.SetResourceReference(Control.BackgroundProperty, "PaperBackgroundBrush");
        tabs.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        Grid.SetColumn(tabs, 1); body.Children.Add(tabs);
        Tab(tabs, BuildSimilarityResults(), "Similar periods", "相似區間", "類似期間");
        var summary = new DockPanel(); _summary.Margin = new Thickness(10);
        DockPanel.SetDock(_summary, Dock.Top); summary.Children.Add(_summary); summary.Children.Add(_stats);
        Tab(tabs, summary, "Summary", "統計摘要", "統計概要");
        var cases = new Grid(); cases.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        cases.RowDefinitions.Add(new() { Height = GridLength.Auto });
        cases.RowDefinitions.Add(new() { Height = GridLength.Auto });
        cases.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        cases.Children.Add(_cases);
        _detail.Margin = new Thickness(6); Grid.SetRow(_detail, 1); cases.Children.Add(_detail);
        Grid.SetRow(_chart, 2); cases.Children.Add(_chart); Grid.SetRow(_bars, 3); cases.Children.Add(_bars);
        _cases.SelectionChanged += (_, _) => UpdateCaseDetail();
        Tab(tabs, cases, "Historical cases", "歷史案例", "履歴ケース");
        Tab(tabs, _evaluations, "Sampling / exclusions", "採樣／排除原因", "採用／除外理由");
        Tab(tabs, _quality, "Data quality", "資料品質", "データ品質");
        var notes = Text(); notes.Margin = new Thickness(14);
        _sourceMethods = notes;
        Bind(() => notes.Text = SourceMethodsText());
        Tab(tabs, new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "Source / methods", "資料來源／方法", "データ源／方法");
        Tab(tabs, _marketBars, "Downloaded daily bars", "原始日線行情", "元の日足行情");
        BuildPages(root);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
        Workspace = root;
        foreach (var input in new[] { _lookback, _threshold, _start, _end, _asOf, _horizons, _upper, _lower }) input.TextChanged += (_, _) => Edited();
        _sampling.SelectionChanged += (_, _) => Edited();
        _template.SelectedIndex = 1;
        foreach (var input in new[] { _lookback, _threshold, _upper, _lower, _recentDays, _minimumSimilarity }) input.TextAlignment = TextAlignment.Right;
        ConfigureColumns();
        SetLegacyTabsVisibility();
    }

    public void LoadDemo()
    {
        InvalidateWork();
        _loadedMarketDataPath = null;
        Snapshot = DemoData.Create(); Result = null; _stale = false; _attemptIssues = null; _dataDiagnostics = [];
        _updating = true;
        try
        {
            _start.Text = Snapshot.Calendar.TradingDates[Math.Min(130, Snapshot.Calendar.TradingDates.Count - 1)].ToString("yyyy-MM-dd");
            _end.Text = _asOf.Text = Snapshot.DataAsOf.ToString("yyyy-MM-dd");
        }
        finally { _updating = false; }
        ResetSimilarity(); RenderResult(); UpdateDataInfo(); SelectMarketData(SyntheticDataKey); SetEnabled();
        SetStatus(() => T("Synthetic data loaded. Set the recent window, scan range and minimum similarity.", "合成資料已載入，設定近期日數、掃描區間與相似度門檻後即可掃描。", "合成データを読み込みました。参照日数、検索期間、類似度を設定してください。"));
    }

    public async Task RunResearchAsync()
    {
        if (Snapshot is null || _closed) return;
        _advancedResearch.IsExpanded = true;
        _resultTabs.SelectedIndex = 1;
        ResearchDefinition definition;
        try { definition = ReadDefinition(); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        { SetStatus(() => T("Invalid settings: ", "設定有誤：", "設定エラー：") + error.Message); return; }
        InvalidateWork();
        if (_dataDiagnostics.Any(issue => issue.BlocksResearch))
        {
            _attemptIssues = MergeIssues(_dataDiagnostics, SnapshotValidator.Validate(Snapshot));
            _quality.ItemsSource = _attemptIssues;
            _stale = Result is not null;
            UpdateSummary(); SetEnabled();
            SetStatus(() => T("Research blocked by source checks. See Data quality.", "來源檢查尚未通過，無法研究；請看資料品質。", "データ源の確認が不十分です。データ品質を確認してください。"));
            return;
        }
        var generation = _generation;
        var snapshot = Snapshot;
        var cancellation = new CancellationTokenSource(); _work = cancellation; SetEnabled();
        SetStatus(() => T("Researching…", "研究中…", "研究中…"));
        StartValuationRefresh(snapshot, generation);
        try
        {
            var completed = await Task.Run(() =>
            {
                var run = new ResearchEngine().Run(snapshot, definition, cancellation.Token);
                return (Run: run, Presentation: run.IsResearchAllowed ? PrepareResult(run, snapshot, cancellation.Token) : null);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            var result = completed.Run;
            if (!result.IsResearchAllowed)
            {
                _attemptIssues = result.Diagnostics;
                _quality.ItemsSource = MergeIssues(_dataDiagnostics, _attemptIssues);
                _stale = Result is not null;
                UpdateSummary();
                SetStatus(() => T("Research blocked; previous result retained. See Data quality: ", "資料條件不足，保留前次結果；請看資料品質：", "研究できません。前回結果を保持します。データ品質：") + string.Join(" · ", result.BlockingReasons));
                return;
            }
            Result = result; _stale = false; _attemptIssues = null; RenderResult(completed.Presentation);
            SetStatus(() => T("Completed. Historical statistics use each horizon's own valid sample count.", "研究完成。各觀察期分別使用自己的有效樣本數。", "完了。各観察期間の有効ケース数を分母に使用します。"));
        }
        catch (OperationCanceledException)
        { if (!_closed && generation == _generation) SetStatus(() => T("Cancelled; previous result retained.", "已取消，保留前次完整結果。", "キャンセルしました。前回結果を保持します。")); }
        catch (Exception error)
        { if (!_closed && generation == _generation) SetStatus(() => T("Research failed: ", "研究失敗：", "研究エラー：") + error.Message); }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) _work = null;
            cancellation.Dispose(); if (!_closed) SetEnabled();
        }
    }

    private ResearchDefinition ReadDefinition()
    {
        var template = (ResearchTemplate)((ComboBoxItem)_template.SelectedItem).Tag;
        var lookback = int.Parse(_lookback.Text, NumberStyles.None, CultureInfo.InvariantCulture);
        decimal? threshold = template.DefaultThreshold is null ? null : Number(_threshold.Text);
        if (template.Id == "HL-R001") threshold /= 100;
            var definition = ResearchTemplates.CreateDefinition(template.Id, Date(_start.Text), Date(_end.Text), Date(_asOf.Text), lookback, threshold);
        definition = definition with
        {
            Horizons = _horizons.Text.Split(',', StringSplitOptions.TrimEntries).Select(value => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray(),
            SamplingPolicy = (SamplingPolicy)_sampling.SelectedItem,
            UpperThreshold = Number(_upper.Text) / 100,
            LowerThreshold = Number(_lower.Text) / 100
        };
        ResearchEngine.ValidateDefinition(definition);
        return definition;
    }

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static decimal Number(string text) => decimal.Parse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private void ApplyTemplateDefaults()
    {
        if (_template.SelectedItem is not ComboBoxItem { Tag: ResearchTemplate template }) return;
        _updating = true;
        try
        {
            _lookback.Text = template.DefaultLookback.ToString(CultureInfo.InvariantCulture);
            _threshold.Text = template.DefaultThreshold is { } threshold ? (template.Id == "HL-R001" ? threshold * 100 : threshold).ToString(CultureInfo.InvariantCulture) : "";
            _threshold.IsEnabled = template.DefaultThreshold is not null;
            _formula.Text = template.Formula;
        }
        finally { _updating = false; }
    }

    private void Edited()
    {
        if (_updating) return;
        InvalidateWork();
        _stale = Result is not null;
        if (_stale) SetStatus(() => T("Settings changed. Results below still show the previous completed run.", "設定已變更，尚未重新執行；下方仍是前次完整結果。", "設定変更済み・未再実行です。前回の結果を表示しています。"));
        else SetStatus(() => T("Settings changed. Run research when ready.", "設定已變更，準備完成後可執行研究。", "設定を変更しました。準備ができたら研究を実行します。"));
        UpdateSummary(); SetEnabled();
    }

    private void InvalidateWork() { _generation++; _work?.Cancel(); CancelValuationRefresh(); }

    public async Task<string> SaveResearchAsync(CancellationToken cancellationToken = default)
    {
        if (_closed || Snapshot is null || Result is not { IsResearchAllowed: true } || _stale || _work is not null) throw new InvalidOperationException("Run current settings before saving.");
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _work = cancellation; SetEnabled();
        try { return await _store.SaveAsync(Snapshot, Result, cancellation.Token); }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) _work = null;
            cancellation.Dispose(); if (!_closed) SetEnabled();
        }
    }

    private async Task SaveFromUi()
    {
        var generation = _generation;
        try
        {
            SetStatus(() => T("Saving…", "保存中…", "保存中…"));
            await SaveResearchAsync();
            if (_closed || generation != _generation) return;
            RefreshSaved(); SetStatus(() => T("Saved locally with the complete snapshot.", "已保存到本機，包含完整資料快照。", "完全なスナップショットとともに保存しました。"));
        }
        catch (OperationCanceledException)
        { if (!_closed && generation == _generation) SetStatus(() => T("Save cancelled; completed result retained.", "已取消保存，保留完整結果。", "保存をキャンセルしました。完了した結果を保持します。")); }
        catch (Exception error)
        { if (!_closed && generation == _generation) SetStatus(() => T("Save failed: ", "保存失敗：", "保存エラー：") + error.Message); }
    }

    public Task LoadResearchAsync(string path) => LoadResearchAsync(path, default);

    public async Task LoadResearchAsync(string path, CancellationToken cancellationToken)
    {
        if (_closed) return;
        InvalidateWork(); var generation = _generation;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _work = cancellation; SetEnabled();
        try
        {
            var saved = await ResearchStore.LoadAsync(path, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            var editor = PrepareEditor(saved.Run.Definition);
            var presentation = await Task.Run(() => PrepareResult(saved.Run, saved.Snapshot, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            // Everything that depends on file content has been checked and prepared.
            _updating = true;
            try
            {
                _template.SelectedItem = editor.Template;
                _lookback.Text = editor.Lookback; _threshold.Text = editor.Threshold;
                var template = (ResearchTemplate)editor.Template.Tag;
                _threshold.IsEnabled = template.DefaultThreshold is not null; _formula.Text = template.Formula;
                _start.Text = editor.Start; _end.Text = editor.End; _asOf.Text = editor.AsOf;
                _horizons.Text = editor.Horizons; _sampling.SelectedItem = saved.Run.Definition.SamplingPolicy;
                _upper.Text = editor.Upper; _lower.Text = editor.Lower;
                _loadedMarketDataPath = null; Snapshot = saved.Snapshot; Result = saved.Run; _stale = false; _attemptIssues = null; _dataDiagnostics = [];
                ResetSimilarity(); UpdateDataInfo(); RenderResult(presentation);
                _advancedResearch.IsExpanded = true; _resultTabs.SelectedIndex = 1;
            }
            finally { _updating = false; }
            SetStatus(() => T("Saved run opened. Its snapshot and engine version are preserved.", "已開啟保存研究，保留原始快照與引擎版本。", "保存した研究を開きました。元のデータとエンジン版を保持しています。"));
        }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) _work = null;
            cancellation.Dispose(); if (!_closed) SetEnabled();
        }
    }

    private EditorValues PrepareEditor(ResearchDefinition definition)
    {
        // Validate supported settings before changing any current work.
        ResearchEngine.ValidateDefinition(definition);
        var selectedItem = _template.Items.Cast<ComboBoxItem>().FirstOrDefault(item => ((ResearchTemplate)item.Tag).Id == definition.TemplateId)
            ?? throw new InvalidDataException("This preview cannot edit that template.");
        var selected = (ResearchTemplate)selectedItem.Tag;
        var lookback = selected.Id == "HL-R002" ? checked((int)definition.Conditions[0].Value) : definition.Conditions[0].Lookback;
        decimal? threshold = selected.Id == "HL-R001" ? definition.Conditions.Last().Value : null;
        if (selected.Id == "HL-R003")
        {
            var volume = definition.Conditions.Single(item => item.Feature == ResearchFeature.RelativeVolume);
            lookback = volume.Lookback; threshold = volume.Value;
        }
        var expected = ResearchTemplates.CreateDefinition(selected.Id, definition.EventStart, definition.EventEnd, definition.DataAsOf, lookback, threshold);
        if (definition.TemplateVersion != selected.Version || !definition.Conditions.SequenceEqual(expected.Conditions))
            throw new InvalidDataException("Saved template conditions or version are not supported by this preview.");
        return new(selectedItem, lookback.ToString(CultureInfo.InvariantCulture),
            threshold is { } value ? (selected.Id == "HL-R001" ? InputPercent(value) : value.ToString(CultureInfo.InvariantCulture)) : "",
            definition.EventStart.ToString("yyyy-MM-dd"), definition.EventEnd.ToString("yyyy-MM-dd"), definition.DataAsOf.ToString("yyyy-MM-dd"),
            string.Join(',', definition.Horizons), InputPercent(definition.UpperThreshold), InputPercent(definition.LowerThreshold));
    }

    private static string InputPercent(decimal fraction)
    {
        try { return checked(fraction * 100).ToString(CultureInfo.InvariantCulture); }
        catch (OverflowException error) { throw new InvalidDataException("Saved percentage cannot be edited by this preview.", error); }
    }

    private sealed record EditorValues(ComboBoxItem Template, string Lookback, string Threshold, string Start, string End, string AsOf, string Horizons, string Upper, string Lower);

    private async Task LoadFromUi(string path)
    {
        // LoadResearchAsync starts a new generation before its first await.
        var generation = _generation + 1;
        try { await LoadResearchAsync(path); }
        catch (OperationCanceledException)
        { if (!_closed && generation == _generation) SetStatus(() => T("Open cancelled; previous work retained.", "已取消開啟，保留原工作。", "読込をキャンセルしました。前回の作業を保持します。")); }
        catch (Exception error)
        { if (!_closed && generation == _generation) SetStatus(() => T("Open failed; previous work retained: ", "開啟失敗，保留原工作：", "読込エラー。前回の作業を保持：") + error.Message); }
    }

    private void RefreshSaved()
    {
        try { _saved.ItemsSource = _store.List().Select(path => new SavedItem(path, Path.GetFileName(path).Replace(".histolens.json", ""))).ToArray(); _saved.SelectedIndex = 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { SetStatus(() => error.Message); }
    }

    private void SetEnabled()
    {
        var busy = _work is not null;
        _runButton.IsEnabled = Snapshot is not null && !busy;
        _cancel.IsEnabled = busy || _valuationWork is not null; _open.IsEnabled = _loadSaved.IsEnabled = !busy;
        _save.IsEnabled = Result is { IsResearchAllowed: true } && !_stale && !busy;
        _scanButton.IsEnabled = Snapshot is not null && !busy;
        SetMarketDataEnabled(busy);
    }

    private void SetStatus(Func<string> text) { _statusText = text; _status.Text = text(); }
    private void Bind(Action action) { _translations.Add(action); action(); }
    private void Translate()
    {
        Description = T("Compare the recent trend with similar historical periods.", "以近期走勢為基準，找出歷史相似區間。", "直近の値動きに似た過去の期間を探します。");
        foreach (var action in _translations) action();
        _inventory.Items.Refresh();
        UpdateSavedStockLabels();
        if (_statusText is not null) _status.Text = _statusText();
        UpdateDataInfo(); UpdateSummary(); UpdateCaseDetail(); _chart.InvalidateVisual();
        UpdateSimilaritySummary(); UpdateSimilarityDetail();
    }
    private static TextBlock Text()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return text;
    }
    private Button Button(string en, string zh, string ja, RoutedEventHandler click)
    {
        var button = new Button { Margin = new Thickness(0, 4, 6, 4), HorizontalContentAlignment = HorizontalAlignment.Left };
        Bind(() => button.Content = T(en, zh, ja)); button.Click += click; return button;
    }
    private void AddInput(Panel panel, Control input, string en, string zh, string ja)
    {
        var label = Text(); label.FontSize = 12; Bind(() => { label.Text = T(en, zh, ja); System.Windows.Automation.AutomationProperties.SetName(input, label.Text); });
        input.MinHeight = 28; input.Margin = new Thickness(0, 0, 2, 2);
        if (input is ComboBox) input.SetResourceReference(StyleProperty, "FontPickerComboBoxStyle");
        panel.Children.Add(label); panel.Children.Add(input);
    }
    private GroupBox Section(UIElement content, string en, string zh, string ja)
    {
        var section = new GroupBox { Content = content, Margin = new Thickness(0, 8, 0, 5), Padding = new Thickness(6) };
        section.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        section.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
        Bind(() => section.Header = T(en, zh, ja));
        return section;
    }
    private void Tab(TabControl tabs, UIElement content, string en, string zh, string ja)
    { var tab = new TabItem { Content = content }; Bind(() => tab.Header = T(en, zh, ja)); tabs.Items.Add(tab); }
    private static DataGrid Table()
    {
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false,
            EnableRowVirtualization = true, EnableColumnVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0), FontSize = 12, MinRowHeight = 28 };
        table.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush"); table.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        table.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "LineBrush");
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("HoverBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6))); table.ColumnHeaderStyle = headerStyle;
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush"))); table.RowStyle = rowStyle;
        return table;
    }
    private void Column(DataGrid grid, string property, string en, string zh, string ja, string? sortProperty = null, bool numeric = false)
    {
        var column = new DataGridTextColumn { Binding = new Binding(property) { StringFormat = property == "Date" ? "{0:yyyy-MM-dd}" : null },
            SortMemberPath = sortProperty ?? property, CanUserSort = sortProperty != "", MinWidth = 76, Width = DataGridLength.Auto };
        if (numeric)
        {
            var textStyle = new Style(typeof(TextBlock));
            textStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            textStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0)));
            column.ElementStyle = textStyle;
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            column.CellStyle = cellStyle;
            var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), grid.ColumnHeaderStyle);
            headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
            column.HeaderStyle = headerStyle;
        }
        Bind(() => column.Header = T(en, zh, ja)); grid.Columns.Add(column);
    }
    private sealed record SavedItem(string Path, string Label);
}
