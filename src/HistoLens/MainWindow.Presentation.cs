using System.Globalization;
using HistoLens.Core;

namespace HistoLens;

public sealed partial class MainWindow
{
    private static string Percent(decimal? value) => value is null ? "—" : value.Value.ToString("+0.00%;-0.00%;0.00%", CultureInfo.InvariantCulture);
    private static string Rate(decimal? value) => value is null ? "—" : value.Value.ToString("0.0%", CultureInfo.InvariantCulture);

    private void ConfigureColumns()
    {
        Column(_stats, "Horizon", "Days", "交易日", "取引日");
        Column(_stats, "N", "Valid N", "有效 N", "有効 N");
        Column(_stats, "Excluded", "Excluded", "排除", "除外");
        Column(_stats, "Mean", "Mean", "平均", "平均", "Statistics.MeanPriceChange");
        Column(_stats, "Median", "Median", "中位數", "中央値", "Statistics.MedianPriceChange");
        Column(_stats, "Up", "Up count / rate", "上漲次數／比例", "上昇数／割合", "Statistics.UpRate");
        Column(_stats, "Flat", "Flat count / rate", "平盤次數／比例", "横ばい数／割合", "Statistics.FlatRate");
        Column(_stats, "Down", "Down count / rate", "下跌次數／比例", "下落数／割合", "Statistics.DownRate");
        Column(_stats, "P10", "P10", "P10", "P10", "Statistics.P10"); Column(_stats, "P25", "P25", "P25", "P25", "Statistics.P25");
        Column(_stats, "P75", "P75", "P75", "P75", "Statistics.P75"); Column(_stats, "P90", "P90", "P90", "P90", "Statistics.P90");
        Column(_stats, "Best", "Best / date", "最佳／日期", "最高／日付", "Statistics.BestPriceChange");
        Column(_stats, "Worst", "Worst / date", "最差／日期", "最低／日付", "Statistics.WorstPriceChange");
        Column(_stats, "High", "Mean period high", "平均期間最高變動", "期間高値変動の平均", "Statistics.MeanHighestPriceChange");
        Column(_stats, "Low", "Mean period low", "平均期間最低變動", "期間安値変動の平均", "Statistics.MeanLowestPriceChange");
        Column(_stats, "Mdd", "Mean close max drawdown", "平均收盤最大回撤", "終値最大ドローダウンの平均", "Statistics.MeanCloseMaxDrawdown");
        Column(_stats, "Upper", "Upper hit count / rate", "上方觸及次數／比例", "上方到達数／割合", "Statistics.UpperHitRate");
        Column(_stats, "Lower", "Lower hit count / rate", "下方觸及次數／比例", "下方到達数／割合", "Statistics.LowerHitRate");
        Column(_stats, "Order", "Upper / lower first / same day / neither", "上先／下先／同日不明／皆無", "上先／下先／同日不明／なし", "");
        Column(_stats, "Reasons", "Exclusion reasons", "排除原因", "除外理由");

        Column(_cases, "Date", "Event date", "事件日期", "イベント日", "Case.EventDate");
        Column(_cases, "Horizon", "Days", "交易日", "取引日");
        Column(_cases, "Status", "Status", "狀態", "状態");
        Column(_cases, "Change", "End price change", "期末價格變動", "期末価格変動", "Outcome.PriceChange");
        Column(_cases, "High", "Period high change", "期間最高價變動", "期間高値変動", "Outcome.HighestPriceChange");
        Column(_cases, "Low", "Period low change", "期間最低價變動", "期間安値変動", "Outcome.LowestPriceChange");
        Column(_cases, "Mdd", "Close max drawdown", "收盤最大回撤", "終値最大ドローダウン", "Outcome.CloseMaxDrawdown");
        Column(_cases, "Upper", "Upper first hit day", "上方首次觸及交易日", "上方初回到達日数", "Outcome.UpperFirstHitTradingDay");
        Column(_cases, "Lower", "Lower first hit day", "下方首次觸及交易日", "下方初回到達日数", "Outcome.LowerFirstHitTradingDay");
        Column(_cases, "Order", "Threshold order", "觸及先後", "到達順序");
        Column(_cases, "Features", "Features (unrounded)", "事件特徵（原精度）", "特徴量（元の精度）");

        Column(_evaluations, "Date", "Date", "日期", "日付");
        Column(_evaluations, "State", "Condition state", "條件狀態", "条件状態");
        Column(_evaluations, "Sampled", "Sampled", "已採樣", "採用");
        Column(_evaluations, "Reasons", "Reasons", "原因", "理由");
        Column(_evaluations, "Features", "Features", "特徵", "特徴量");
        Column(_quality, "Date", "Date", "日期", "日付");
        Column(_quality, "Code", "Code", "問題代碼", "コード");
        Column(_quality, "Message", "Details", "說明", "説明");
        Column(_quality, "BlocksResearch", "Blocks research", "阻擋研究", "研究不可");
        foreach (var (key, en, zh, ja) in new[]
        {
            ("Date", "Date", "日期", "日付"), ("Open", "Open", "開盤", "始値"), ("High", "High", "最高", "高値"),
            ("Low", "Low", "最低", "安値"), ("Close", "Close", "收盤", "終値"), ("Volume", "Volume (shares)", "成交量（股）", "出来高（株）"),
            ("Status", "Trading status", "交易狀態", "取引状態")
        }) Column(_bars, key, en, zh, ja);
    }

    private static ResultPresentation PrepareResult(ResearchRun? result, DataSnapshot? snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Materialize display values before replacing the current research.
        var statistics = result?.Statistics.Select(s =>
        { cancellationToken.ThrowIfCancellationRequested(); return new StatisticsRow(s); }).ToArray() ?? [];
        var cases = result?.Cases.SelectMany(c => c.Outcomes.Select(o =>
        { cancellationToken.ThrowIfCancellationRequested(); return new CaseRow(c, o); })).ToArray() ?? [];
        var evaluations = result?.Evaluations.Select(e =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new
            {
                Date = e.Date.ToString("yyyy-MM-dd"), e.State, Sampled = e.IsSampled,
                Reasons = string.Join(" · ", e.Reasons.Select(x => x.ToString()).Concat(e.SamplingExclusion is { } reason ? [reason.ToString()] : [])),
                Features = FeatureText(e.Features)
            };
        }).ToArray();
        return new(statistics, cases, evaluations, result?.Diagnostics ?? (snapshot is null ? [] : SnapshotValidator.Validate(snapshot, cancellationToken)));
    }

    private void RenderResult(ResultPresentation? presentation = null)
    {
        presentation ??= PrepareResult(Result, Snapshot);
        _stats.ItemsSource = presentation.Statistics;
        _cases.ItemsSource = presentation.Cases;
        _evaluations.ItemsSource = presentation.Evaluations;
        _quality.ItemsSource = _attemptIssues ?? presentation.Quality;
        _cases.SelectedIndex = _cases.Items.Count > 0 ? 0 : -1;
        UpdateCaseDetail(); UpdateSummary();
    }

    private void UpdateDataInfo()
    {
        _dataInfo.Text = Snapshot is null ? T("No data loaded.", "尚未載入資料。", "データ未読込。")
            : $"{Snapshot.Instrument.Code} · {Snapshot.Instrument.Name}\n{Snapshot.Instrument.Market} · {Snapshot.Bars.Count:N0} · {Snapshot.Instrument.Currency}\n{Snapshot.Calendar.CoverageStart:yyyy-MM-dd} → {Snapshot.DataAsOf:yyyy-MM-dd}\n{Snapshot.SourceId}";
    }

    private void UpdateSummary()
    {
        if (Result is null)
        {
            _summary.Text = T("Load the sample, choose a template, then run research.\nResults and individual cases will appear here.",
                "載入測試資料、選擇模板並執行研究，這裡會顯示統計與可核對的案例。", "合成データとテンプレートを選んで実行すると統計とケースを表示します。"); return;
        }
        var r = Result; var d = r.Definition; var f = r.Funnel;
        _summary.Text = (_stale ? T("SETTINGS CHANGED · Previous result\n", "設定已變更，尚未重新執行 · 以下為前次結果\n", "設定変更済み・前回の結果\n") : "") +
            $"{r.Instrument.Code} · {r.SourceId} · {d.TemplateId} v{d.TemplateVersion}\n" +
            $"{d.EventStart:yyyy-MM-dd} → {d.EventEnd:yyyy-MM-dd} · DataAsOf {d.DataAsOf:yyyy-MM-dd}\n" +
            $"{d.SamplingPolicy} · ConservativeRaw · Engine {r.EngineVersion}\n" +
            T($"Candidates {f.CandidateDates} → determinable {f.DeterminableDates} → matching days {f.RawMatchDays} → events {f.SampledEvents}",
              $"候選日 {f.CandidateDates} → 可判定 {f.DeterminableDates} → 符合日 {f.RawMatchDays} → 採樣事件 {f.SampledEvents}",
              $"候補日 {f.CandidateDates} → 判定可能 {f.DeterminableDates} → 一致日 {f.RawMatchDays} → 採用 {f.SampledEvents}") +
            "\n" + (r.Statistics.Any(s => s.IsSmallSample)
                ? T("Small sample: fewer than 30 valid cases in one or more horizons.\n", "部分觀察期少於 30 個有效樣本，容易受個別案例影響。\n", "一部の期間は有効ケースが30件未満です。\n") : "") +
            T("Each horizon uses its own N. Upper/lower hits may overlap. — means not applicable.\n", "各期分母不同；上下門檻可能同時觸及；— 表示不適用。\n", "分母は期間ごとに異なります。上下到達は重複可能。— は該当なし。\n") +
            $"Snapshot: {r.DataContentHash}\n" + string.Join(" · ", r.BlockingReasons);
    }

    private void UpdateCaseDetail()
    {
        if (_cases.SelectedItem is not CaseRow row || Snapshot is null)
        { _bars.ItemsSource = null; _chart.SetData([], null); _detail.Text = ""; return; }
        var cutoff = Result!.Definition.DataAsOf;
        var end = row.Outcome.EndDate is { } endDate && endDate < cutoff ? endDate : cutoff;
        var bars = Snapshot.Bars.Where(bar => bar.Date >= row.Case.ConditionWindowStart && bar.Date <= end).OrderBy(bar => bar.Date).ToArray();
        _bars.ItemsSource = bars;
        _chart.SetData(bars, row.Case.EventDate);
        _detail.Text = $"{row.Case.ConditionWindowStart:yyyy-MM-dd} → [{row.Case.EventDate:yyyy-MM-dd}] → {end:yyyy-MM-dd} · P0 = {row.Case.BaseClose}\n" +
            T("Close-price path · dashed marker = event date. Raw OHLC below.", "收盤價格路徑 · 虛線為事件日；下表提供原始 OHLC。", "終値の推移・破線はイベント日。下表は元の OHLC です。");
    }

    private static string FeatureText(IReadOnlyList<FeatureValue> features) => string.Join(" AND ", features.Select(f =>
        $"{f.Condition.Feature}[{f.Condition.Lookback}]={f.Value?.ToString(CultureInfo.InvariantCulture) ?? "?"} ({f.State})"));

    private sealed record CaseRow(ResearchCase Case, HorizonOutcome Outcome)
    {
        public string Date { get; } = Case.EventDate.ToString("yyyy-MM-dd");
        public int Horizon => Outcome.Horizon;
        public string Status { get; } = Outcome.PrimaryExclusion?.ToString() ?? "Valid";
        public string Change { get; } = Percent(Outcome.PriceChange);
        public string High { get; } = Percent(Outcome.HighestPriceChange);
        public string Low { get; } = Percent(Outcome.LowestPriceChange);
        public string Mdd { get; } = Percent(Outcome.CloseMaxDrawdown);
        public string Upper { get; } = Outcome.UpperFirstHitTradingDay?.ToString(CultureInfo.InvariantCulture) ?? "—";
        public string Lower { get; } = Outcome.LowerFirstHitTradingDay?.ToString(CultureInfo.InvariantCulture) ?? "—";
        public string Order { get; } = Outcome.ThresholdOrder?.ToString() ?? "—";
        public string Features { get; } = FeatureText(Case.Features);
    }

    private sealed record StatisticsRow(HorizonStatistics Statistics)
    {
        public int Horizon => Statistics.Horizon;
        public int N => Statistics.ValidCount;
        public int Excluded => Statistics.ExcludedCount;
        public string Mean { get; } = Percent(Statistics.MeanPriceChange);
        public string Median { get; } = Percent(Statistics.MedianPriceChange);
        public string Up { get; } = $"{Statistics.UpCount}/{Statistics.ValidCount} · {Rate(Statistics.UpRate)}";
        public string Flat { get; } = $"{Statistics.FlatCount}/{Statistics.ValidCount} · {Rate(Statistics.FlatRate)}";
        public string Down { get; } = $"{Statistics.DownCount}/{Statistics.ValidCount} · {Rate(Statistics.DownRate)}";
        public string P10 { get; } = Percent(Statistics.P10);
        public string P25 { get; } = Percent(Statistics.P25);
        public string P75 { get; } = Percent(Statistics.P75);
        public string P90 { get; } = Percent(Statistics.P90);
        public string Best { get; } = $"{Percent(Statistics.BestPriceChange)} · {Statistics.BestEventDate:yyyy-MM-dd}";
        public string Worst { get; } = $"{Percent(Statistics.WorstPriceChange)} · {Statistics.WorstEventDate:yyyy-MM-dd}";
        public string High { get; } = Percent(Statistics.MeanHighestPriceChange);
        public string Low { get; } = Percent(Statistics.MeanLowestPriceChange);
        public string Mdd { get; } = Percent(Statistics.MeanCloseMaxDrawdown);
        public string Upper { get; } = $"{Statistics.UpperHitCount}/{Statistics.ValidCount} · {Rate(Statistics.UpperHitRate)}";
        public string Lower { get; } = $"{Statistics.LowerHitCount}/{Statistics.ValidCount} · {Rate(Statistics.LowerHitRate)}";
        public string Order { get; } = $"{Statistics.UpperFirstCount} / {Statistics.LowerFirstCount} / {Statistics.SameDayUnknownCount} / {Statistics.NeitherHitCount}";
        public string Reasons { get; } = string.Join(" · ", Statistics.Exclusions.Select(x => $"{x.Reason}: {x.Count}"));
    }

    private sealed record ResultPresentation(StatisticsRow[] Statistics, CaseRow[] Cases, System.Collections.IEnumerable? Evaluations, IReadOnlyList<DataIssue> Quality);
}
