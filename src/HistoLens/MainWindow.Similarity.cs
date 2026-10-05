using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HistoLens.Core;

namespace HistoLens;

public sealed partial class MainWindow
{
    private readonly TextBox _recentDays = new() { Text = "20" },
        _minimumSimilarity = new() { Text = "60" };
    private readonly DatePicker _scanStart = new(), _scanEnd = new();
    private readonly TextBlock _similaritySummary = Text(), _similarityDetail = Text();
    private readonly DataGrid _similarities = Table(), _similarityIndicators = Table();
    private readonly SimilarityComparisonChart _comparisonChart = new() { Margin = new Thickness(8, 4, 8, 4) };
    private Button _scanButton = null!;
    private Expander _advancedResearch = null!;
    private TabControl _resultTabs = null!;
    private bool _similarityStale;
    public SimilarityRun? SimilarityResult { get; private set; }
    public bool IsSimilarityResultStale => _similarityStale;

    private void BuildSimilarityInputs(Panel settings)
    {
        AddInput(settings, _recentDays, "Recent window (5–250 trading days)", "近期區間（5–250 個交易日）", "直近の期間（5～250取引日）");
        AddInput(settings, _scanStart, "Scan start", "歷史掃描起日", "検索開始日");
        AddInput(settings, _scanEnd, "Scan end", "歷史掃描迄日", "検索終了日");
        ConfigureDateInput(_scanStart, SimilaritySettingsEdited);
        ConfigureDateInput(_scanEnd, SimilaritySettingsEdited);
        AddInput(settings, _minimumSimilarity, "Minimum total similarity (%)", "總相似度至少（%）", "総合類似度の下限（%・以上）");
        BuildIndicatorSelection(settings);
        var help = Text(); help.FontSize = 12;
        help.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Bind(() => help.Text = T("The reference ends at the latest completed day in your data. Only full historical windows inside the scan range are compared.",
            "基準截至資料中最近的已完成交易日。只比對完整落在掃描區間內的歷史片段。", "基準はデータ内の最新確定取引日です。検索期間に完全に含まれる過去の期間を比較します。"));
        settings.Children.Add(help);
        foreach (var input in new[] { _recentDays, _minimumSimilarity })
            input.TextChanged += (_, _) => SimilaritySettingsEdited();
    }
    private UIElement BuildSimilarityResults()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1.2, GridUnitType.Star), MinHeight = 80 });
        root.RowDefinitions.Add(new() { Height = new GridLength(1.8, GridUnitType.Star) });
        _similaritySummary.Margin = new Thickness(10, 4, 10, 4); _similaritySummary.FontSize = 12;
        root.Children.Add(_similaritySummary);
        Grid.SetRow(_similarities, 1); root.Children.Add(_similarities);
        Grid.SetRow(_comparisonChart, 2); root.Children.Add(_comparisonChart);
        var content = new StackPanel();
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.MinHeight = 350;
        root.SetBinding(HeightProperty, new Binding(nameof(ActualHeight)) { Source = scroll });
        content.Children.Add(root);
        var detailPanel = new DockPanel();
        DockPanel.SetDock(_similarityDetail, Dock.Top); detailPanel.Children.Add(_similarityDetail);
        _similarityIndicators.Height = 174; detailPanel.Children.Add(_similarityIndicators);
        content.Children.Add(Section(detailPanel, "Selected period: indicator comparison", "所選區間：指標實值與相似度", "選択した期間：指標値と類似度"));
        var explanation = Text(); explanation.FontSize = 12;
        Bind(() => explanation.Text = T(
            "Select any of 10 indicators; the total is the mean of selected scores (equal weights). Each score = 100 / (1 + difference / scale). N is the chosen window. Price-path RMSE and high/low distance scales: 5 pp; slope: 5/(N−1) pp/day; return volatility: 2 pp. Window RSI: 20 points; SMA deviation: 5 pp; Bollinger width: 10 pp; normalized ATR: 2 pp; mean-normalized volume-path RMSE: 0.5×. RSI and ATR use initial averages from N−1 in-window changes/ranges, without earlier Wilder smoothing; flat RSI=50. Bollinger bands use N closes and ±2 population standard deviations. All calculations stay inside each compared window. Filtering includes unrounded totals at or above the threshold; overlapping windows keep the highest score. Scores are descriptive, not probabilities. Formula v2. Hover over each indicator for its formula.",
            "10 項指標可自由勾選，總分＝已選分數的平均（等權）。單項＝100÷（1＋差異÷尺度）。N 是所選區間日數。價格路徑 RMSE／高低點差距尺度為 5 個百分點；斜率 5÷(N−1) 個百分點／日；日漲跌波動度 2 個百分點。區間 RSI 20 點；均線乖離 5 個百分點；布林帶寬 10 個百分點；正規化 ATR 2 個百分點；均量正規化的成交量路徑 RMSE 0.5 倍。RSI／ATR 採區間內 N−1 次變動／振幅的初始平均，不延續區間外 Wilder 平滑；全平盤 RSI=50。布林帶採 N 日收盤、母體標準差、上下各 2σ。所有計算都限定在比對區間內。未四捨五入總分大於或等於門檻才列出，重疊時保留最高分。分數不代表上漲機率。公式 v2；各指標提示可查看詳細公式。",
            "10指標から選択し、総合は選択した得点の等分平均。各得点=100÷(1+差÷尺度)。Nは期間日数。価格経路RMSEと高安値距離：5ポイント、傾き：5/(N−1)ポイント/日、変動性：2ポイント。期間RSI：20点、平均乖離：5ポイント、バンド幅：10ポイント、正規化ATR：2ポイント、平均正規化出来高のRMSE：0.5倍。RSI/ATRは期間内N−1回の初期平均で、期間外のWilder平滑値は不使用。横ばいRSI=50。バンドはN終値の母標準偏差±2σ。期間内のデータのみを計算し、丸め前の総合が下限以上の結果を表示。重複は最高得点を残します。確率ではありません。式v2。指標のヒントに詳細式を表示。"));
        content.Children.Add(Section(explanation, "How similarity is calculated", "相似度怎麼算", "類似度の計算方法"));
        Column(_similarities, nameof(SimilarityRow.Period), "Historical period", "歷史區間", "過去の期間", "Match.Features.Start");
        Column(_similarities, nameof(SimilarityRow.Total), "Total %", "總相似度 %", "総合 %", "Match.Scores.Total", numeric: true);
        BuildIndicatorColumns();
        Column(_similarityIndicators, nameof(IndicatorRow.Name), "Indicator", "指標", "指標", "");
        Column(_similarityIndicators, nameof(IndicatorRow.Reference), "Current reference", "近期基準實值", "直近の値", "", numeric: true);
        Column(_similarityIndicators, nameof(IndicatorRow.Historical), "Historical value", "歷史實值", "過去の値", "", numeric: true);
        Column(_similarityIndicators, nameof(IndicatorRow.Score), "Similarity %", "相似度 %", "類似度 %", "", numeric: true);
        _similarities.SelectionChanged += (_, _) => UpdateSimilarityDetail();
        UpdateSimilaritySummary();
        return scroll;
    }

    private void SetLegacyTabsVisibility()
    {
        if (_resultTabs is null) return;
        for (var i = 1; i < _resultTabs.Items.Count; i++)
            ((TabItem)_resultTabs.Items[i]).Visibility = i >= 4 || _advancedResearch.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (!_advancedResearch.IsExpanded && _resultTabs.SelectedIndex is >= 1 and <= 3) _resultTabs.SelectedIndex = 0;
    }

    private static DateOnly TaipeiToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")).DateTime);

    private DateOnly SimilarityCutoff() => Snapshot!.DataAsOf < TaipeiToday() ? Snapshot.DataAsOf : TaipeiToday();

    private void ResetSimilarity()
    {
        ResetValuation();
        SimilarityResult = null; _similarityStale = false; UpdateIndicatorColumns();
        _similarities.ItemsSource = null; _similarityIndicators.ItemsSource = null;
        _comparisonChart.SetHistory(Snapshot);
        _comparisonChart.ClearComparison();
        var updating = _updating; _updating = true;
        try
        {
            if (Snapshot is not null)
            {
                var dates = Snapshot.Calendar.TradingDates.Where(date => date <= SimilarityCutoff()).Order().ToArray();
                var n = int.TryParse(_recentDays.Text, out var value) && value is >= 5 and <= 250 ? value : 20;
                _scanStart.SelectedDate = Snapshot.Calendar.CoverageStart.ToDateTime(TimeOnly.MinValue);
                _scanEnd.SelectedDate = (dates.Length > n ? dates[dates.Length - n - 1] : SimilarityCutoff()).ToDateTime(TimeOnly.MinValue);
            }
        }
        finally { _updating = updating; }
        _resultTabs.SelectedIndex = 0;
        UpdateSimilaritySummary(); UpdateSimilarityDetail();
    }

    public async Task RunSimilarityAsync()
    {
        if (Snapshot is null || _closed) return;
        SimilarityDefinition definition;
        try
        {
            definition = new()
            {
                Lookback = int.Parse(_recentDays.Text, NumberStyles.None, CultureInfo.InvariantCulture),
                ScanStart = SelectedDay(_scanStart), ScanEnd = SelectedDay(_scanEnd),
                DataAsOf = SimilarityCutoff(), MinimumSimilarity = Number(_minimumSimilarity.Text),
                SelectedIndicators = SelectedIndicators
            };
            SimilarityEngine.ValidateDefinition(definition);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { SetStatus(() => T("Invalid scan settings: ", "掃描設定有誤：", "検索条件エラー：") + error.Message); return; }
        InvalidateWork(); var generation = _generation;
        _resultTabs.SelectedIndex = 0;
        var sourceIssues = _dataDiagnostics.Select(SimilarityEngine.ForScan).ToArray();
        if (sourceIssues.Any(issue => issue.BlocksResearch))
        {
            SetStatus(() => T("Source checks block this scan: ", "來源檢查未通過，無法掃描：", "データ検証のため検索できません：") + string.Join(" · ", sourceIssues.Where(issue => issue.BlocksResearch).Select(issue => issue.Message)));
            return;
        }
        var snapshot = Snapshot;
        var cancellation = new CancellationTokenSource(); _work = cancellation; SetEnabled();
        SetStatus(() => T("Scanning similar historical periods…", "掃描歷史相似區間中…", "過去の類似期間を検索中…"));
        StartValuationRefresh(snapshot, generation);
        try
        {
            var run = await Task.Run(() => new SimilarityEngine().Run(snapshot, definition, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            if (!run.IsAllowed)
            {
                _similarityStale = SimilarityResult is not null;
                UpdateSimilaritySummary();
                SetStatus(() => T("Scan blocked; previous result retained: ", "無法掃描，保留前次結果：", "検索できません。前回結果を保持：") + string.Join(" · ", run.BlockingReasons));
                return;
            }
            SimilarityResult = run; _similarityStale = false; UpdateIndicatorColumns();
            _similarities.Items.SortDescriptions.Clear();
            _similarities.ItemsSource = run.Matches.Select(match => new SimilarityRow(match)).ToArray();
            _similarities.SelectedIndex = _similarities.Items.Count > 0 ? 0 : -1;
            UpdateSimilaritySummary(); UpdateSimilarityDetail();
            SetStatus(() => run.Matches.Count == 0
                ? T("No periods meet this threshold. Adjust the threshold or scan range.", "沒有區間達到此門檻；可調整門檻或掃描區間。", "下限以上の期間がありません。条件を調整してください。")
                : T("Scan complete. Select a period to locate it in the full history and inspect what followed.", "掃描完成，點選相似區間即可定位完整歷史圖，查看該區間之後的走勢。", "検索完了。期間を選ぶと履歴上の位置に移動し、その後の値動きを確認できます。"));
        }
        catch (OperationCanceledException)
        { if (!_closed && generation == _generation) SetStatus(() => T("Scan cancelled; previous result retained.", "已取消掃描，保留前次完整結果。", "検索をキャンセルしました。前回結果を保持します。")); }
        catch (Exception error)
        { if (!_closed && generation == _generation) SetStatus(() => T("Scan failed: ", "掃描失敗：", "検索エラー：") + error.Message); }
        finally
        {
            if (ReferenceEquals(_work, cancellation)) _work = null;
            cancellation.Dispose(); if (!_closed) SetEnabled();
        }
    }

    private void UpdateSimilaritySummary()
    {
        var run = SimilarityResult;
        var selected = run?.Definition.SelectedIndicators ?? SelectedIndicators;
        var used = IndicatorOrder.Where(indicator => selected.HasFlag(indicator)).ToArray();
        _similaritySummary.ToolTip = string.Join(" · ", used.Select(IndicatorName));
        if (run?.Reference is not { } reference)
        {
            _similaritySummary.Text = T("Choose a stock and recent window, set the historical scan range and similarity threshold, then find similar periods.",
                "選定股票與近期日數，設定歷史掃描起訖及總相似度門檻，再按「尋找相似區間」。", "銘柄と直近の日数、検索期間、類似度を設定し、検索してください。");
            return;
        }
        var stale = _similarityStale ? T("Settings changed — previous result\n", "設定已變更，以下為前次結果\n", "条件変更済み・前回結果\n") : "";
        _similaritySummary.Text = stale + $"{Snapshot!.Instrument.Code} {Snapshot.Instrument.Name} · " + T("Reference", "近期基準", "基準") +
            $" {reference.Start:yyyy-MM-dd} → {reference.End:yyyy-MM-dd} ({run.Definition.Lookback} " + T("trading days", "個交易日", "取引日") + ")\n" +
            T("Scan", "掃描區間", "検索期間") + $" {run.Definition.ScanStart:yyyy-MM-dd} → {run.Definition.ScanEnd:yyyy-MM-dd} · " +
            T("Total ≥ ", "總相似度 ≥ ", "総合 ≥ ") + $"{run.Definition.MinimumSimilarity:0.########}% · " + T($"Equal-weight indicators: {used.Length}", $"等權指標：{used.Length} 項", $"等分指標：{used.Length} 項目") + "\n" +
            T("Results / comparable / candidates", "列出／可比較／候選", "結果／比較可能／候補") + $" {run.Matches.Count} / {run.ComparableCount} / {run.CandidateCount} · " +
            T("Below threshold", "低於門檻", "下限未満") + $" {run.BelowThresholdCount} · " + T("Overlaps merged", "合併重疊", "重複除外") + $" {run.OverlapExcludedCount}" +
            (run.Exclusions.Count == 0 ? "" : "\n" + T("Quality exclusions", "品質排除", "品質除外") + ": " + string.Join(" · ", run.Exclusions.Select(item => $"{SimilarityExclusion(item.Reason)} {item.Count}"))) +
            "\n" + T("Raw prices, unadjusted; events are annotations. ", "原始價格，未還原；事件以日期註記。", "未調整の原価格。イベントは日付で注記。") +
            T($"Candidates with warnings: {run.WarningCandidateCount}", $"有提醒的候選：{run.WarningCandidateCount}", $"注記付き候補：{run.WarningCandidateCount}") +
            (reference.Warnings.Count == 0 ? "" : "\n" + T("Reference warnings: ", "近期基準提醒：", "基準の注記：") + WindowWarnings(reference)) +
            (reference.End < TaipeiToday() ? "\n" + T("Data ends on ", "資料截至 ", "データ末日 ") + $"{reference.End:yyyy-MM-dd}" + T("; this is not today's quote.", "，不是今日即時行情。", "。本日のリアルタイム価格ではありません。") : "");
    }

    private string SimilarityExclusion(ExclusionReason reason) => reason switch
    {
        ExclusionReason.MissingBar => T("Missing day", "缺少行情", "日足欠損"),
        ExclusionReason.NonTradingBar => T("Non-trading", "停牌／未成交", "未取引"),
        ExclusionReason.MissingPrice => T("Missing price", "缺少價格", "価格欠損"),
        ExclusionReason.InvalidBar => T("Invalid price", "價格異常", "価格異常"),
        ExclusionReason.MissingVolume => T("Missing volume", "缺少成交量", "出来高欠損"),
        ExclusionReason.UndefinedFeature => T("Unrepresentable value", "指標無法計算", "計算不可"),
        ExclusionReason.CorporateActionInWindow => T("Price boundary", "公司行動／不比價", "価格調整境界"),
        ExclusionReason.CorporateActionCoverageUnknown => T("Unverified coverage", "可比性覆蓋不足", "比較検証不足"),
        _ => reason.ToString()
    };

    private void UpdateSimilarityDetail()
    {
        _comparisonChart.SetLanguage(ResolvedLanguage);
        _comparisonChart.SetHistory(Snapshot);
        if (SimilarityResult?.Reference is not { } reference || _similarities.SelectedItem is not SimilarityRow row)
        {
            _similarityDetail.Text = T("Select a result to see the actual values and individual scores.", "選取結果後可看近期與歷史的實值，以及各項相似度。", "結果を選択すると実値と個別の得点を表示します。");
            _similarityIndicators.ItemsSource = null; _comparisonChart.ClearComparison(); return;
        }
        var value = row.Match.Features;
        _comparisonChart.SetComparison(value.Start, value.End,
            Snapshot!.Bars.Where(bar => bar.Date >= reference.Start && bar.Date <= reference.End).ToArray());
        _similarityDetail.Text = $"{value.Start:yyyy-MM-dd} → {value.End:yyyy-MM-dd} · " +
            T("Close (reference / historical)", "收盤價（近期／歷史）", "終値（直近／過去）") + $" {reference.EndClose:0.##} / {value.EndClose:0.##} {Snapshot!.Instrument.Currency}\n" +
            T("Window change (reference / historical)", "區間漲跌（近期／歷史）", "期間変動（直近／過去）") + $" {Fraction(reference.PriceChange)} / {Fraction(value.PriceChange)}";
        if (reference.Warnings.Count > 0)
            _similarityDetail.Text += "\n" + T("Reference warnings: ", "近期基準提醒：", "基準の注記：") + WindowWarnings(reference);
        if (value.Warnings.Count > 0)
            _similarityDetail.Text += "\n" + T("Historical warnings: ", "歷史區間提醒：", "過去の注記：") + WindowWarnings(value);
        _similarityIndicators.ItemsSource = IndicatorOrder.Where(indicator => SimilarityResult.Definition.SelectedIndicators.HasFlag(indicator))
            .Select(indicator => new IndicatorRow(IndicatorValueLabel(indicator), IndicatorValue(indicator, reference),
                IndicatorValue(indicator, value), row.Match.Scores.GetScore(indicator) is { } score ? Score(score) : "—")).ToArray();
    }
    private string WindowWarnings(SimilarityFeatures features) => string.Join(" · ", features.Warnings.Select(issue => issue.Code switch
    {
        "CorporateActionCoverageUnknown" => T("Event coverage unknown", "公司行動覆蓋未知", "イベントの網羅性は未確認"),
        "PriceComparisonCoverageUnknown" => T("Raw-price comparability unknown", "原價可比性覆蓋未知", "原価格の比較可能性は未確認"),
        _ => $"{issue.Date:yyyy-MM-dd} {issue.Message}"
    }));
    private static string NormalizedEnd(SimilarityFeatures value) => (100d * (1d + (double)value.PriceChange)).ToString("0.00", CultureInfo.InvariantCulture);
    private static string Fraction(decimal value) => ((double)value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
    private static string Score(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private sealed record IndicatorRow(string Name, string Reference, string Historical, string Score);
    private sealed record SimilarityRow(SimilarityMatch Match)
    {
        public string Period => $"{Match.Features.Start:yyyy-MM-dd} → {Match.Features.End:yyyy-MM-dd}";
        public string Total => Score(Match.Scores.Total);
        private string Value(SimilarityIndicator indicator) => Match.Scores.GetScore(indicator) is { } value ? Score(value) : "—";
        public string PricePath => Value(SimilarityIndicator.PricePath);
        public string HighPosition => Value(SimilarityIndicator.HighPosition);
        public string LowPosition => Value(SimilarityIndicator.LowPosition);
        public string Slope => Value(SimilarityIndicator.Slope);
        public string Volatility => Value(SimilarityIndicator.Volatility);
        public string Rsi => Value(SimilarityIndicator.Rsi);
        public string MovingAverageDeviation => Value(SimilarityIndicator.MovingAverageDeviation);
        public string BollingerBandwidth => Value(SimilarityIndicator.BollingerBandwidth);
        public string NormalizedAtr => Value(SimilarityIndicator.NormalizedAtr);
        public string VolumePath => Value(SimilarityIndicator.VolumePath);
    }
}
