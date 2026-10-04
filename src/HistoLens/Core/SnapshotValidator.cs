namespace HistoLens.Core;

/// <summary>Reports original data problems without filling, adjusting, or deleting values.</summary>
public static class SnapshotValidator
{
    public static IReadOnlyList<DataIssue> Validate(DataSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = new List<DataIssue>();
        void Add(string code, string message, bool blocking = false, DateOnly? date = null) =>
            issues.Add(new DataIssue { Code = code, Message = message, BlocksResearch = blocking, Date = date });
        if (snapshot.Instrument is null || snapshot.Calendar is null || snapshot.ActionCoverage is null ||
            snapshot.Bars is null || snapshot.CorporateActions is null)
            throw new ArgumentException("資料快照缺少必要結構。", nameof(snapshot));
        var instrument = snapshot.Instrument;
        var calendar = snapshot.Calendar;
        var coverage = snapshot.ActionCoverage;
        var comparison = snapshot.ComparabilityCoverage;
        var comparisonVerified = comparison is { IsVerified: true } &&
            snapshot.SourceId == "TWSE" && instrument.Market == "TWSE" && !snapshot.IsSynthetic &&
            instrument.SecurityType == SecurityType.CommonStock && comparison.SourceId == "TWSE/STOCK_DAY" &&
            !string.IsNullOrWhiteSpace(comparison.Version) && comparison.CoverageStart <= comparison.CoverageEnd;
        if (comparison is not null && (!comparisonVerified || comparison.Gaps is null ||
            comparison.Gaps.Any(g => g is null || g.Start > g.End ||
                g.Start < comparison.CoverageStart || g.End > comparison.CoverageEnd)))
            Add("PriceComparisonCoverageInvalid", "原價可比性覆蓋的來源、版本或缺口格式不正確。", true);
        if (string.IsNullOrWhiteSpace(instrument.InstrumentId) || string.IsNullOrWhiteSpace(instrument.Market) ||
            string.IsNullOrWhiteSpace(instrument.Code) || string.IsNullOrWhiteSpace(snapshot.SourceId) ||
            string.IsNullOrWhiteSpace(snapshot.DataVersion))
            Add("IdentityMissing", "股票穩定識別、代號、市場、來源或資料版本不足。", true);
        if (!Enum.IsDefined(instrument.SecurityType))
            Add("UnsupportedSecurity", "不支援此證券類型。", true);
        if (snapshot.IsSynthetic != (instrument.SecurityType == SecurityType.Synthetic))
            Add("SyntheticIdentityMismatch", "合成資料與證券類型標示不一致。", true);
        if (!calendar.IsVerified || string.IsNullOrWhiteSpace(calendar.Version) ||
            calendar.Market != instrument.Market || calendar.CoverageStart > calendar.CoverageEnd)
            Add("CalendarUnverified", "交易日曆來源、市場、版本或覆蓋範圍尚未確認。", true);
        if (calendar.TradingDates is null || calendar.TradingDates.Count == 0)
            Add("CalendarEmpty", "缺少明確的市場交易日序列。", true);
        if (!coverage.IsVerified || string.IsNullOrWhiteSpace(coverage.Version) ||
            string.IsNullOrWhiteSpace(coverage.SourceId) || coverage.CoverageStart > coverage.CoverageEnd)
            Add("CorporateActionCoverageUnknown", comparisonVerified
                ? "公司行動目錄未確認完整；僅使用官方價差核對的原價可比區段，不跨越不比價或公司行動邊界。"
                : "公司行動覆蓋未經確認，且沒有已確認的原價可比性覆蓋，不能把查不到資料視為沒有行動。", !comparisonVerified);
        if (coverage.Gaps is null || coverage.Gaps.Any(g => g is null || g.Start > g.End ||
            g.Start < coverage.CoverageStart || g.End > coverage.CoverageEnd))
            Add("CorporateActionCoverageInvalid", "公司行動覆蓋缺口格式不正確。", true);
        if (snapshot.DataAsOf < calendar.CoverageStart || snapshot.DataAsOf > calendar.CoverageEnd)
            Add("DataAsOfOutsideCalendar", "資料截止日不在已確認的日曆覆蓋範圍內。", true);

        var dates = new HashSet<DateOnly>();
        foreach (var date in calendar.TradingDates ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!dates.Add(date)) Add("DuplicateTradingDate", "交易日曆有未處理的重複日期。", true, date);
            if (date < calendar.CoverageStart || date > calendar.CoverageEnd)
                Add("TradingDateOutsideCoverage", "交易日超出日曆已確認範圍。", true, date);
        }
        var barDates = new HashSet<DateOnly>();
        foreach (var bar in snapshot.Bars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bar is null) { Add("NullBar", "行情包含空白記錄。", true); continue; }
            if (!barDates.Add(bar.Date)) Add("DuplicateBar", "行情有未處理的重複交易日。", true, bar.Date);
            if (!dates.Contains(bar.Date)) Add("BarOutsideCalendar", "行情日期不在市場交易日曆中。", true, bar.Date);
            if (bar.Date > snapshot.DataAsOf) Add("BarAfterDataAsOf", "行情日期晚於快照的已完成交易日截止日。", true, bar.Date);
            if (bar.SourceId != snapshot.SourceId)
                Add("BarSourceMismatch", "行情來源與快照來源不一致。", true, bar.Date);
            foreach (var reason in GetBarReasons(bar, false))
                Add(reason.ToString(), Describe(reason), false, bar.Date);
        }
        foreach (var date in dates.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (date <= snapshot.DataAsOf && !barDates.Contains(date))
                Add("MissingBar", "市場交易日缺少此股票的行情。", false, date);
        }
        foreach (var action in snapshot.CorporateActions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action is null || string.IsNullOrWhiteSpace(action.Kind) || string.IsNullOrWhiteSpace(action.SourceId))
                Add("CorporateActionInvalid", "公司行動缺少類型或来源。", true, action?.EffectiveDate);
        }
        return issues;
    }

    internal static IReadOnlyList<ExclusionReason> GetBarReasons(DailyBar? bar, bool requireVolume)
    {
        if (bar is null) return [ExclusionReason.MissingBar];
        var reasons = new List<ExclusionReason>();
        if (!Enum.IsDefined(bar.Status)) reasons.Add(ExclusionReason.InvalidBar);
        else if (bar.Status != TradingStatus.Traded) reasons.Add(ExclusionReason.NonTradingBar);
        if (bar.Open is null || bar.High is null || bar.Low is null || bar.Close is null)
            reasons.Add(ExclusionReason.MissingPrice);
        if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0 ||
            bar.High < bar.Open || bar.High < bar.Close || bar.Low > bar.Open || bar.Low > bar.Close ||
            bar.High < bar.Low || bar.Volume < 0 || bar.Turnover < 0)
            reasons.Add(ExclusionReason.InvalidBar);
        if (requireVolume && bar.Volume is null) reasons.Add(ExclusionReason.MissingVolume);
        return reasons.Distinct().ToArray();
    }

    public static string Describe(ExclusionReason reason) => reason switch
    {
        ExclusionReason.InsufficientHistory => "前置交易日資料不足",
        ExclusionReason.MissingBar => "市場交易日缺少行情",
        ExclusionReason.NonTradingBar => "停牌、無成交或僅參考價",
        ExclusionReason.MissingPrice => "必要 OHLC 缺值",
        ExclusionReason.InvalidBar => "價格、OHLC 關係或數量異常",
        ExclusionReason.MissingVolume => "條件所需成交量缺值",
        ExclusionReason.UndefinedFeature => "特徵分母為零，無法計算",
        ExclusionReason.CorporateActionCoverageUnknown => "公司行動或原價可比性覆蓋不足或有缺口",
        ExclusionReason.CorporateActionInWindow => "研究窗口跨越公司行動或不比價邊界",
        ExclusionReason.InsufficientFutureData => "截止日前後續交易日資料不足",
        ExclusionReason.EventStartUnknown => "前一交易日不可判定，事件起點不確定",
        ExclusionReason.ConsecutiveMatch => "同一連續成立期間已合併",
        ExclusionReason.OverlappingWindow => "與已保留事件的最長觀察窗重疊",
        _ => reason.ToString()
    };
}
