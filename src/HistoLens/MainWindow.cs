using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HistoLens.Core;
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
    private readonly PriceChart _chart = new() { Height = 120, Margin = new Thickness(0, 4, 0, 4) };
    private Button _demo = null!, _runButton = null!, _cancel = null!, _save = null!, _open = null!, _loadSaved = null!;
    private CancellationTokenSource? _work;
    private int _generation;
    private bool _updating, _closed, _stale;
    private Func<string>? _statusText;
    public DataSnapshot? Snapshot { get; private set; }
    public ResearchRun? Result { get; private set; }
    public bool IsResultStale => _stale;

    public MainWindow(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper", "HistoLens");
        _store = new ResearchStore(Path.Combine(directory, "research"));
        PreferencesPath = Path.Combine(directory, "ui.json");
        MainName = "HistoLens";
        SubName = "Historical Stock Research";
        AboutAuthor = "不告訴你";
        Width = 1280; Height = 880; MinWidth = 900; MinHeight = 660;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/HistoLens;component/ResearchStyles.xaml", UriKind.Relative) });
        BuildWorkspace();
        UiPreferencesChanged += (_, _) => Translate();
        Closed += (_, _) => { _closed = true; _generation++; _work?.Cancel(); };
        ApplyUiPreferences();
        RefreshSaved();
        SetStatus(() => T("Load the synthetic sample to start.", "載入合成資料即可開始。", "合成データを読み込んで開始します。"));
        SetEnabled();
    }

    private void BuildWorkspace()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _notice.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _notice.Margin = new Thickness(0, 0, 0, 10);
        Bind(() => _notice.Text = T("006 · DEVELOPMENT PREVIEW · Synthetic data, not real stock research. No network requests.",
            "006 · 開發預覽 · 示範資料，非真實股票研究結果。此版本不連線下載行情。", "006 · 開発プレビュー · 合成データです。実際の株式研究ではありません。通信は行いません。"));
        root.Children.Add(_notice);
        var body = new Grid();
        body.ColumnDefinitions.Add(new() { Width = new GridLength(265) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1); root.Children.Add(body);
        var settings = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        var scroller = new ScrollViewer { Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        scroller.SetResourceReference(StyleProperty, "SidebarScrollViewerStyle");
        body.Children.Add(scroller);
        _demo = Button("Load synthetic data", "載入合成測試資料", "合成データを読み込む", (_, _) => LoadDemo());
        settings.Children.Add(_demo);
        settings.Children.Add(_dataInfo);
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
        _cancel = Button("Cancel", "取消", "キャンセル", (_, _) => _work?.Cancel());
        actions.Children.Add(_runButton); actions.Children.Add(_cancel);
        _save = Button("Save this completed result", "保存這次完整研究", "完了した研究を保存", async (_, _) => await SaveFromUi());
        actions.Children.Add(_save); HeaderActions = actions;
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
        tabs.SetResourceReference(Control.BackgroundProperty, "PaperBackgroundBrush");
        tabs.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        Grid.SetColumn(tabs, 1); body.Children.Add(tabs);
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
        Bind(() => notes.Text = T(
            "Synthetic sample only. Prices, volume, calendar and corporate actions are generated, not Taiwan exchange data.\n\nFree sources are being evaluated: TWSE / TPEx official open data and FinMind. Historical coverage, market calendars, corporate actions and data-use rights must be checked before enabling real-stock research.\n\nRaw prices; windows crossing price-affecting corporate actions are excluded. A complete market-day calendar is required; missing or suspended days are never skipped.\n\nChanging settings keeps the previous result and marks it stale. Save stores the exact data snapshot, definition, engine version and full result.\n\nResearch describes historical cases; it does not predict future prices or execute trades.",
            "目前使用合成測試資料。價格、成交量、日曆與公司行動皆為產生的測試內容，不代表台灣交易所資料。\n\n免費來源正評估證交所、櫃買中心公開資料與 FinMind。真實行情需確認歷史範圍、交易日曆、公司行動及使用權利後才能開放研究。\n\n採保守原始價格：跨越影響價格的公司行動時排除案例；缺價或停牌不能跳過後拿下一筆補足交易日。\n\n修改設定會保留舊結果並標示未重跑。保存內容包含當次資料快照、完整設定、引擎版本與結果。\n\n結果是歷史案例描述，不是未來預測，也不執行交易。",
            "合成テストデータのみです。価格・出来高・カレンダー・企業行動は生成値です。台湾市場の実データではありません。\n\n無料データ源として TWSE、TPEx、FinMind を調査中です。履歴範囲、取引日、企業行動、利用条件の確認後に実データ研究を提供します。\n\n原価格を使用し、価格に影響する企業行動をまたぐケースを除外します。欠損日や売買停止日を飛ばしません。\n\n設定変更時は前回の結果を保持して未再実行と表示します。保存にはスナップショット、設定、エンジン版、結果を含みます。\n\n将来の予測や売買執行は行いません。"));
        Tab(tabs, new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, "Source / methods", "資料來源／方法", "データ源／方法");
        _status.Margin = new Thickness(0, 10, 0, 0); Grid.SetRow(_status, 2); root.Children.Add(_status);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
        Workspace = root;
        foreach (var input in new[] { _lookback, _threshold, _start, _end, _asOf, _horizons, _upper, _lower }) input.TextChanged += (_, _) => Edited();
        _sampling.SelectionChanged += (_, _) => Edited();
        _template.SelectedIndex = 1;
        ConfigureColumns();
    }

    public void LoadDemo()
    {
        InvalidateWork();
        Snapshot = DemoData.Create(); Result = null; _stale = false;
        _updating = true;
        _start.Text = Snapshot.Calendar.TradingDates[Math.Min(130, Snapshot.Calendar.TradingDates.Count - 1)].ToString("yyyy-MM-dd");
        _end.Text = _asOf.Text = Snapshot.DataAsOf.ToString("yyyy-MM-dd");
        _updating = false;
        RenderResult(); UpdateDataInfo(); SetEnabled();
        SetStatus(() => T("Synthetic data loaded. Choose a template and run.", "合成資料已載入，選擇模板後可執行研究。", "合成データを読み込みました。テンプレートを選んで実行します。"));
    }

    public async Task RunResearchAsync()
    {
        if (Snapshot is null || _closed) return;
        ResearchDefinition definition;
        try { definition = ReadDefinition(); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        { SetStatus(() => T("Invalid settings: ", "設定有誤：", "設定エラー：") + error.Message); return; }
        InvalidateWork();
        var generation = _generation;
        var snapshot = Snapshot;
        var cancellation = new CancellationTokenSource(); _work = cancellation; SetEnabled();
        SetStatus(() => T("Researching…", "研究中…", "研究中…"));
        try
        {
            var result = await Task.Run(() => new ResearchEngine().Run(snapshot, definition, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            Result = result; _stale = false; RenderResult();
            SetStatus(() => result.IsResearchAllowed
                ? T("Completed. Historical statistics use each horizon's own valid sample count.", "研究完成。各觀察期分別使用自己的有效樣本數。", "完了。各観察期間の有効ケース数を分母に使用します。")
                : T("Research blocked by data quality. See Data quality.", "資料條件不足，無法研究；請看資料品質。", "データ品質により研究できません。データ品質を確認してください。"));
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
        return definition with
        {
            Horizons = _horizons.Text.Split(',', StringSplitOptions.TrimEntries).Select(value => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray(),
            SamplingPolicy = (SamplingPolicy)_sampling.SelectedItem,
            UpperThreshold = Number(_upper.Text) / 100,
            LowerThreshold = Number(_lower.Text) / 100
        };
    }

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static decimal Number(string text) => decimal.Parse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private void ApplyTemplateDefaults()
    {
        if (_template.SelectedItem is not ComboBoxItem { Tag: ResearchTemplate template }) return;
        _updating = true;
        _lookback.Text = template.DefaultLookback.ToString(CultureInfo.InvariantCulture);
        _threshold.Text = template.DefaultThreshold is { } threshold ? (template.Id == "HL-R001" ? threshold * 100 : threshold).ToString(CultureInfo.InvariantCulture) : "";
        _threshold.IsEnabled = template.DefaultThreshold is not null;
        _formula.Text = template.Formula; _updating = false;
    }

    private void Edited()
    {
        if (_updating) return;
        InvalidateWork();
        _stale = Result is not null;
        if (_stale) SetStatus(() => T("Settings changed. Results below still show the previous completed run.", "設定已變更，尚未重新執行；下方仍是前次完整結果。", "設定変更済み・未再実行です。前回の結果を表示しています。"));
        UpdateSummary(); SetEnabled();
    }

    private void InvalidateWork() { _generation++; _work?.Cancel(); }

    public async Task<string> SaveResearchAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot is null || Result is not { IsResearchAllowed: true } || _stale || _work is not null) throw new InvalidOperationException("Run current settings before saving.");
        return await _store.SaveAsync(Snapshot, Result, cancellationToken);
    }

    private async Task SaveFromUi()
    {
        try { await SaveResearchAsync(); RefreshSaved(); SetStatus(() => T("Saved locally with the complete snapshot.", "已保存到本機，包含完整資料快照。", "完全なスナップショットとともに保存しました。")); }
        catch (Exception error) { SetStatus(() => T("Save failed: ", "保存失敗：", "保存エラー：") + error.Message); }
    }

    public async Task LoadResearchAsync(string path)
    {
        InvalidateWork(); var generation = _generation;
        var saved = await ResearchStore.LoadAsync(path);
        if (_closed || generation != _generation) return;
        // Validate supported settings before changing any current work.
        var definition = saved.Run.Definition;
        var selectedItem = _template.Items.Cast<ComboBoxItem>().FirstOrDefault(item => ((ResearchTemplate)item.Tag).Id == definition.TemplateId)
            ?? throw new InvalidDataException("This preview cannot edit that template.");
        var selected = (ResearchTemplate)selectedItem.Tag;
        if (definition.Conditions is null || definition.Conditions.Count == 0 || definition.Conditions.Any(item => item is null) ||
            definition.Horizons is null || definition.Horizons.Count is < 1 or > 4 ||
            definition.Horizons.Any(h => h is < 1 or > ResearchEngine.MaxHorizon) || definition.Horizons.Distinct().Count() != definition.Horizons.Count ||
            !Enum.IsDefined(definition.SamplingPolicy) || definition.PriceMode != PriceMode.ConservativeRaw ||
            definition.EventStart == default || definition.EventStart > definition.EventEnd || definition.EventEnd > definition.DataAsOf ||
            definition.UpperThreshold <= 0 || definition.LowerThreshold is <= -1 or >= 0)
            throw new InvalidDataException("Saved settings are not valid for this preview.");
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
        Snapshot = saved.Snapshot; Result = saved.Run; _stale = false;
        _updating = true; _template.SelectedItem = selectedItem; _updating = false;
        ApplyTemplateDefaults(); _updating = true;
        _lookback.Text = definition.Conditions[0].Lookback.ToString(CultureInfo.InvariantCulture);
        if (selected.Id == "HL-R002") _lookback.Text = definition.Conditions[0].Value.ToString(CultureInfo.InvariantCulture);
        if (selected.Id == "HL-R001") _threshold.Text = (definition.Conditions.Last().Value * 100).ToString(CultureInfo.InvariantCulture);
        if (selected.Id == "HL-R003")
        {
            var volume = definition.Conditions.Single(item => item.Feature == ResearchFeature.RelativeVolume);
            _lookback.Text = volume.Lookback.ToString(CultureInfo.InvariantCulture); _threshold.Text = volume.Value.ToString(CultureInfo.InvariantCulture);
        }
        _start.Text = definition.EventStart.ToString("yyyy-MM-dd"); _end.Text = definition.EventEnd.ToString("yyyy-MM-dd");
        _asOf.Text = definition.DataAsOf.ToString("yyyy-MM-dd"); _horizons.Text = string.Join(',', definition.Horizons);
        _sampling.SelectedItem = definition.SamplingPolicy;
        _upper.Text = (definition.UpperThreshold * 100).ToString(CultureInfo.InvariantCulture); _lower.Text = (definition.LowerThreshold * 100).ToString(CultureInfo.InvariantCulture);
        _updating = false; UpdateDataInfo(); RenderResult(); SetEnabled();
        SetStatus(() => T("Saved run opened. Its snapshot and engine version are preserved.", "已開啟保存研究，保留原始快照與引擎版本。", "保存した研究を開きました。元のデータとエンジン版を保持しています。"));
    }

    private async Task LoadFromUi(string path)
    {
        try { await LoadResearchAsync(path); }
        catch (Exception error) { SetStatus(() => T("Open failed; previous work retained: ", "開啟失敗，保留原工作：", "読込エラー。前回の作業を保持：") + error.Message); }
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
        _cancel.IsEnabled = busy; _demo.IsEnabled = _open.IsEnabled = _loadSaved.IsEnabled = !busy;
        _save.IsEnabled = Result is { IsResearchAllowed: true } && !_stale && !busy;
    }

    private void SetStatus(Func<string> text) { _statusText = text; _status.Text = text(); }
    private void Bind(Action action) { _translations.Add(action); action(); }
    private void Translate()
    {
        Description = T("Select a question. Explore historical cases.", "選一個問題，把歷史攤開來研究。", "問いを選び、過去のケースを研究します。");
        foreach (var action in _translations) action();
        if (_statusText is not null) _status.Text = _statusText();
        UpdateDataInfo(); UpdateSummary(); _chart.InvalidateVisual();
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
    private void Column(DataGrid grid, string property, string en, string zh, string ja)
    {
        var column = new DataGridTextColumn { Binding = new Binding(property) { StringFormat = property == "Date" ? "{0:yyyy-MM-dd}" : null }, MinWidth = 76, Width = DataGridLength.Auto };
        Bind(() => column.Header = T(en, zh, ja)); grid.Columns.Add(column);
    }
    private sealed record SavedItem(string Path, string Label);
}
