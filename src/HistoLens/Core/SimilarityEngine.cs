namespace HistoLens.Core;

/// <summary>Pure similarity calculation over one validated, fixed raw-price snapshot.</summary>
public sealed class SimilarityEngine
{
    public const string Version = "1.2.1-similarity-v2-raw-events";
    public const int MinLookback = 5;
    public const int MaxLookback = 250;

    public SimilarityRun Run(DataSnapshot snapshot, SimilarityDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateDefinition(definition);
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot.Calendar?.TradingDates is null || snapshot.ActionCoverage?.Gaps is null ||
            snapshot.Bars is null || snapshot.CorporateActions is null ||
            snapshot.ComparabilityCoverage is { Gaps: null })
            throw new ArgumentException("資料快照缺少必要集合。", nameof(snapshot));
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.ToArray(), CorporateActions = snapshot.CorporateActions.ToArray(),
            Calendar = snapshot.Calendar with { TradingDates = snapshot.Calendar.TradingDates.ToArray() },
            ActionCoverage = snapshot.ActionCoverage with { Gaps = snapshot.ActionCoverage.Gaps.ToArray() },
            ComparabilityCoverage = snapshot.ComparabilityCoverage is { } comparison
                ? comparison with { Gaps = comparison.Gaps.ToArray() } : null
        };
        var diagnostics = SnapshotValidator.Validate(snapshot, cancellationToken).Select(ForScan).ToList();
        if (definition.ScanStart < snapshot.Calendar.CoverageStart || definition.ScanEnd > snapshot.Calendar.CoverageEnd)
            diagnostics.Add(new DataIssue { Code = "ScanRangeOutsideCalendar", Message = "掃描範圍超出已確認的市場日曆覆蓋。", BlocksResearch = true });
        if (definition.DataAsOf > snapshot.DataAsOf)
            diagnostics.Add(new DataIssue { Code = "AsOfBeyondSnapshot", Message = "比較截止日晚於此快照的已完成交易日截止日。", BlocksResearch = true });
        if (!diagnostics.Any(issue => issue.BlocksResearch))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = SnapshotFingerprint.Compute(snapshot);
            if (!string.IsNullOrEmpty(snapshot.ContentHash) && !string.Equals(snapshot.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new DataIssue { Code = "SnapshotHashMismatch", Message = "快照內容與保存的內容雜湊不同。", BlocksResearch = true });
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (diagnostics.Any(issue => issue.BlocksResearch)) return Blocked(definition, diagnostics);

        var context = new Calculation(snapshot, definition.Lookback, definition.SelectedIndicators, cancellationToken);
        // Use the calendar, never the last available price, to locate the reference endpoint.
        var endIndex = Array.FindLastIndex(context.Dates, date => date <= definition.DataAsOf);
        if (endIndex < definition.Lookback - 1)
        {
            diagnostics.Add(new DataIssue { Code = "ReferenceInsufficientHistory", Message = "截止日前市場交易日不足，無法建立完整參考區間。", BlocksResearch = true });
            return Blocked(definition, diagnostics);
        }
        var referenceIndex = endIndex - definition.Lookback + 1;
        var reference = context.Features(referenceIndex, out var referenceReasons);
        if (reference is null)
        {
            diagnostics.AddRange(referenceReasons.Select(reason => new DataIssue
            {
                Code = "Reference" + reason, Date = context.Dates[endIndex], BlocksResearch = true,
                Message = "參考區間不可比較：" + SnapshotValidator.Describe(reason) + "。"
            }));
            return Blocked(definition, diagnostics);
        }

        var candidates = 0;
        var comparable = 0;
        var warned = 0;
        var belowThreshold = 0;
        var exclusions = new Dictionary<ExclusionReason, int>();
        var qualifying = new List<(int StartIndex, SimilarityMatch Match)>();
        for (var start = 0; start + definition.Lookback - 1 < referenceIndex; start++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = start + definition.Lookback - 1;
            if (context.Dates[start] < definition.ScanStart || context.Dates[end] > definition.ScanEnd) continue;
            candidates++;
            var features = context.Features(start, out var reasons);
            if (features is null)
            {
                var primary = reasons[0];
                exclusions[primary] = exclusions.GetValueOrDefault(primary) + 1;
                continue;
            }
            comparable++;
            if (features.Warnings.Count > 0) warned++;
            var scores = context.Scores(reference, features);
            // The inclusive minimum applies to the unrounded total.
            if (scores.Total < definition.MinimumSimilarity) { belowThreshold++; continue; }
            qualifying.Add((start, new SimilarityMatch { Features = features, Scores = scores }));
        }

        var matches = new List<SimilarityMatch>();
        var occupied = new bool[context.Dates.Length];
        var overlapExcluded = 0;
        foreach (var candidate in qualifying.OrderByDescending(item => item.Match.Scores.Total).ThenBy(item => item.Match.Features.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var overlaps = false;
            for (var index = candidate.StartIndex; index < candidate.StartIndex + definition.Lookback; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (occupied[index]) { overlaps = true; break; }
            }
            if (overlaps) { overlapExcluded++; continue; }
            matches.Add(candidate.Match);
            for (var index = candidate.StartIndex; index < candidate.StartIndex + definition.Lookback; index++) occupied[index] = true;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new SimilarityRun
        {
            EngineVersion = Version, Definition = definition, Reference = reference, IsAllowed = true,
            Diagnostics = diagnostics.ToArray(), Matches = matches.ToArray(), CandidateCount = candidates,
            ComparableCount = comparable, WarningCandidateCount = warned,
            BelowThresholdCount = belowThreshold, OverlapExcludedCount = overlapExcluded,
            Exclusions = exclusions.OrderBy(item => item.Key).Select(item => new ExclusionCount { Reason = item.Key, Count = item.Value }).ToArray()
        };
    }

    // Similarity describes the supplied raw prices; unknown event coverage remains explicit.
    // Other source, calendar, structure and fingerprint failures keep their original severity.
    internal static DataIssue ForScan(DataIssue issue) => issue.Code == "CorporateActionCoverageUnknown"
        ? issue with { BlocksResearch = false, Message = "公司行動覆蓋尚未完整確認；使用原始價格完成掃描，未知不代表沒有事件。" }
        : issue;

    public static void ValidateDefinition(SimilarityDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Lookback is < MinLookback or > MaxLookback)
            throw new ArgumentException($"參考區間須為 {MinLookback} 至 {MaxLookback} 個市場交易日。", nameof(definition));
        if (definition.ScanStart == default || definition.ScanEnd < definition.ScanStart || definition.DataAsOf < definition.ScanEnd)
            throw new ArgumentException("掃描日期須有合法起訖，且不得晚於資料截止日。", nameof(definition));
        if (definition.MinimumSimilarity is < 0m or > 100m)
            throw new ArgumentException("最低總相似度須介於 0 與 100。", nameof(definition));
        if (definition.SelectedIndicators == SimilarityIndicator.None || (definition.SelectedIndicators & ~SimilarityIndicator.All) != 0)
            throw new ArgumentException("須選擇至少一個已知的比較指標。", nameof(definition));
    }

    private static SimilarityRun Blocked(SimilarityDefinition definition, IReadOnlyList<DataIssue> diagnostics) => new()
    {
        EngineVersion = Version, Definition = definition, IsAllowed = false,
        BlockingReasons = diagnostics.Where(issue => issue.BlocksResearch).Select(issue => issue.Message).Distinct().ToArray(),
        Diagnostics = diagnostics.ToArray()
    };

    private sealed class Calculation
    {
        private readonly DataSnapshot _snapshot;
        private readonly int _lookback;
        private readonly SimilarityIndicator _selected;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<DateOnly, DailyBar> _bars;
        public DateOnly[] Dates { get; }

        public Calculation(DataSnapshot snapshot, int lookback, SimilarityIndicator selected, CancellationToken cancellationToken)
        {
            _snapshot = snapshot;
            _lookback = lookback;
            _selected = selected;
            _cancellationToken = cancellationToken;
            Dates = snapshot.Calendar.TradingDates.Order().ToArray();
            _bars = snapshot.Bars.ToDictionary(bar => bar.Date);
        }

        public SimilarityFeatures? Features(int startIndex, out ExclusionReason[] exclusions)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var endIndex = startIndex + _lookback - 1;
            var reasons = new HashSet<ExclusionReason>();
            var bars = new DailyBar[_lookback];
            for (var index = startIndex; index <= endIndex; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var bar = _bars.GetValueOrDefault(Dates[index]);
                foreach (var reason in SnapshotValidator.GetBarReasons(bar, Selected(SimilarityIndicator.VolumePath))) reasons.Add(reason);
                bars[index - startIndex] = bar!;
            }
            if (reasons.Count > 0) { exclusions = Ordered(reasons); return null; }
            try
            {
                var baseClose = bars[0].Close!.Value;
                var normalized = new decimal[_lookback];
                var returns = new double[_lookback - 1];
                var high = bars[0].High!.Value;
                var low = bars[0].Low!.Value;
                var meanIndex = (_lookback - 1) / 2m;
                var indexVariance = _lookback * (_lookback * _lookback - 1) / 12m;
                var slope = 0m;
                for (var index = 0; index < _lookback; index++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var bar = bars[index];
                    normalized[index] = bar.Close!.Value / baseClose - 1m;
                    high = Math.Max(high, bar.High!.Value);
                    low = Math.Min(low, bar.Low!.Value);
                    // Each coefficient is below one; dividing first avoids intermediate decimal overflow.
                    slope += normalized[index] * ((index - meanIndex) / indexVariance);
                    if (index > 0) returns[index - 1] = (double)(bar.Close.Value / bars[index - 1].Close!.Value - 1m);
                }
                var meanReturn = returns.Average();
                var variance = returns.Average(value => (value - meanReturn) * (value - meanReturn));
                var volatility = (decimal)Math.Sqrt(variance);
                decimal? rsi = null, movingAverageDeviation = null, bandwidth = null, normalizedAtr = null;
                decimal[] normalizedVolumes = [];
                if (Selected(SimilarityIndicator.Rsi)) rsi = Rsi(bars);
                if (Selected(SimilarityIndicator.MovingAverageDeviation) || Selected(SimilarityIndicator.BollingerBandwidth))
                {
                    // Work in units of the largest close: even a mean of decimal.MaxValue prices is safe.
                    var maximumClose = bars.Max(bar => bar.Close!.Value);
                    var scaledCloses = bars.Select(bar => bar.Close!.Value / maximumClose).ToArray();
                    var meanClose = scaledCloses.Sum() / _lookback;
                    if (Selected(SimilarityIndicator.MovingAverageDeviation))
                        movingAverageDeviation = scaledCloses[^1] / meanClose - 1m;
                    if (Selected(SimilarityIndicator.BollingerBandwidth))
                    {
                        var closeVariance = scaledCloses.Average(value => Math.Pow((double)(value - meanClose), 2));
                        bandwidth = 4m * (decimal)Math.Sqrt(closeVariance) / meanClose;
                    }
                }
                if (Selected(SimilarityIndicator.NormalizedAtr)) normalizedAtr = NormalizedAtr(bars);
                if (Selected(SimilarityIndicator.VolumePath))
                {
                    var maximumVolume = bars.Max(bar => bar.Volume!.Value);
                    if (maximumVolume == 0m) { exclusions = [ExclusionReason.UndefinedFeature]; return null; }
                    var scaledVolumes = bars.Select(bar => bar.Volume!.Value / maximumVolume).ToArray();
                    var meanVolume = scaledVolumes.Sum() / _lookback;
                    normalizedVolumes = scaledVolumes.Select(value => value / meanVolume).ToArray();
                }
                _cancellationToken.ThrowIfCancellationRequested();
                exclusions = [];
                return new SimilarityFeatures
                {
                    Start = Dates[startIndex], End = Dates[endIndex], BaseClose = baseClose, EndClose = bars[^1].Close!.Value,
                    High = high, Low = low, PriceChange = normalized[^1],
                    DistanceFromHigh = bars[^1].Close!.Value / high - 1m,
                    DistanceFromLow = bars[^1].Close!.Value / low - 1m,
                    Slope = slope, Volatility = volatility, NormalizedCloses = normalized,
                    Rsi = rsi, MovingAverageDeviation = movingAverageDeviation, BollingerBandwidth = bandwidth,
                    NormalizedAtr = normalizedAtr, NormalizedVolumes = normalizedVolumes,
                    EndRelativeVolume = normalizedVolumes.Length > 0 ? normalizedVolumes[^1] : null,
                    Warnings = ActionWarnings(Dates[startIndex], Dates[endIndex])
                };
            }
            catch (OverflowException)
            {
                // A representability failure is explicit missing comparison data, never a fabricated score.
                exclusions = [ExclusionReason.UndefinedFeature];
                return null;
            }
        }

        public SimilarityScores Scores(SimilarityFeatures reference, SimilarityFeatures candidate)
        {
            return new SimilarityScores
            {
                SelectedIndicators = _selected,
                PricePath = Selected(SimilarityIndicator.PricePath) ? Score(PathDistance(reference.NormalizedCloses, candidate.NormalizedCloses), 0.05m) : 0m,
                HighPosition = Selected(SimilarityIndicator.HighPosition) ? ScoreDifference(reference.DistanceFromHigh, candidate.DistanceFromHigh, 0.05m) : 0m,
                LowPosition = Selected(SimilarityIndicator.LowPosition) ? ScoreDifference(reference.DistanceFromLow, candidate.DistanceFromLow, 0.05m) : 0m,
                Slope = Selected(SimilarityIndicator.Slope) ? ScoreDifference(reference.Slope, candidate.Slope, 0.05m / (_lookback - 1)) : 0m,
                Volatility = Selected(SimilarityIndicator.Volatility) ? ScoreDifference(reference.Volatility, candidate.Volatility, 0.02m) : 0m,
                Rsi = Selected(SimilarityIndicator.Rsi) ? ScoreDifference(Required(reference.Rsi), Required(candidate.Rsi), 20m) : 0m,
                MovingAverageDeviation = Selected(SimilarityIndicator.MovingAverageDeviation) ? ScoreDifference(Required(reference.MovingAverageDeviation), Required(candidate.MovingAverageDeviation), 0.05m) : 0m,
                BollingerBandwidth = Selected(SimilarityIndicator.BollingerBandwidth) ? ScoreDifference(Required(reference.BollingerBandwidth), Required(candidate.BollingerBandwidth), 0.10m) : 0m,
                NormalizedAtr = Selected(SimilarityIndicator.NormalizedAtr) ? ScoreDifference(Required(reference.NormalizedAtr), Required(candidate.NormalizedAtr), 0.02m) : 0m,
                VolumePath = Selected(SimilarityIndicator.VolumePath) ? Score(PathDistance(reference.NormalizedVolumes, candidate.NormalizedVolumes), 0.5m) : 0m
            };
        }

        private bool Selected(SimilarityIndicator indicator) => (_selected & indicator) != 0;

        private static decimal Required(decimal? feature) => feature ?? throw new InvalidOperationException("已選指標缺少必要特徵。");

        private double PathDistance(IReadOnlyList<decimal> reference, IReadOnlyList<decimal> candidate)
        {
            var squaredDistance = 0d;
            for (var index = 0; index < _lookback; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var difference = Distance(reference[index], candidate[index]);
                squaredDistance += difference * difference;
            }
            // Decimal inputs fit comfortably in double squared-distance range; sqrt is finite.
            return Math.Sqrt(squaredDistance / _lookback);
        }

        private decimal Rsi(IReadOnlyList<DailyBar> bars)
        {
            var changes = new decimal[_lookback - 1];
            var largestChange = 0m;
            for (var index = 1; index < _lookback; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                changes[index - 1] = bars[index].Close!.Value - bars[index - 1].Close!.Value;
                largestChange = Math.Max(largestChange, Math.Abs(changes[index - 1]));
            }
            if (largestChange == 0m) return 50m;
            // The common scale and the N-1 averaging divisor cancel in averageGain/(averageGain+averageLoss).
            var gains = 0m;
            var losses = 0m;
            foreach (var change in changes)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (change > 0m) gains += change / largestChange;
                else losses -= change / largestChange;
            }
            return gains / (gains + losses) * 100m;
        }

        private decimal NormalizedAtr(IReadOnlyList<DailyBar> bars)
        {
            var ranges = new decimal[_lookback - 1];
            var maximumRange = 0m;
            for (var index = 1; index < _lookback; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var high = bars[index].High!.Value;
                var low = bars[index].Low!.Value;
                var previousClose = bars[index - 1].Close!.Value;
                ranges[index - 1] = Math.Max(high - low, Math.Max(Math.Abs(high - previousClose), Math.Abs(low - previousClose)));
                maximumRange = Math.Max(maximumRange, ranges[index - 1]);
            }
            if (maximumRange == 0m) return 0m;
            var scaledMean = ranges.Sum(value => value / maximumRange) / ranges.Length;
            return scaledMean * maximumRange / bars[^1].Close!.Value;
        }

        private static decimal Score(double distance, decimal scale)
        {
            // Equivalent to 100 / (1 + distance / scale), without the large intermediate ratio.
            try { return 100m * scale / (scale + (decimal)distance); }
            catch (OverflowException) { return (decimal)(100d * (double)scale / ((double)scale + distance)); }
        }

        private static decimal ScoreDifference(decimal first, decimal second, decimal scale)
        {
            try { return 100m * scale / (scale + Math.Abs(first - second)); }
            catch (OverflowException) { return Score(Math.Abs((double)first - (double)second), scale); }
        }

        private static double Distance(decimal first, decimal second)
        {
            try { return (double)Math.Abs(first - second); }
            catch (OverflowException) { return Math.Abs((double)first - (double)second); }
        }

        private IReadOnlyList<DataIssue> ActionWarnings(DateOnly start, DateOnly end)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var coverage = _snapshot.ActionCoverage;
            var actionsKnown = coverage.IsVerified && !string.IsNullOrWhiteSpace(coverage.Version) &&
                !string.IsNullOrWhiteSpace(coverage.SourceId) && start >= coverage.CoverageStart && end <= coverage.CoverageEnd &&
                !coverage.Gaps.Any(gap => gap.Start <= end && gap.End >= start);
            var comparison = _snapshot.ComparabilityCoverage;
            var pricesComparable = comparison is { IsVerified: true } && start >= comparison.CoverageStart && end <= comparison.CoverageEnd &&
                !comparison.Gaps.Any(gap => gap.Start <= end && gap.End >= start);
            var warnings = new List<DataIssue>();
            if (!actionsKnown)
                warnings.Add(new() { Code = "CorporateActionCoverageUnknown",
                    Message = "此區間公司行動覆蓋未完整確認；查不到事件不代表沒有事件。" });
            if (comparison is not null && !pricesComparable)
                warnings.Add(new() { Code = "PriceComparisonCoverageUnknown",
                    Message = "此區間原價可比性覆蓋有缺口或未完整確認；仍以原始價格掃描。" });
            foreach (var action in _snapshot.CorporateActions.Where(action => action.EffectiveDate >= start && action.EffectiveDate <= end)
                .OrderBy(action => action.EffectiveDate).ThenBy(action => action.Kind, StringComparer.Ordinal).ThenBy(action => action.SourceId, StringComparer.Ordinal))
                warnings.Add(new() { Code = action.AffectsPriceComparison ? "CorporateActionInWindow" : "CorporateActionAnnotation",
                    Date = action.EffectiveDate, Message = $"{action.Kind} · {action.SourceId}" });
            return warnings.ToArray();
        }
    }

    private static ExclusionReason[] Ordered(IEnumerable<ExclusionReason> reasons) => reasons.OrderBy(reason => reason switch
    {
        ExclusionReason.CorporateActionCoverageUnknown => 0,
        ExclusionReason.CorporateActionInWindow => 1,
        ExclusionReason.MissingBar => 2,
        ExclusionReason.NonTradingBar => 3,
        ExclusionReason.MissingPrice => 4,
        ExclusionReason.InvalidBar => 5,
        _ => 6 + (int)reason
    }).ToArray();
}
