namespace HistoLens.Core;

/// <summary>Pure synchronous computation over a fixed snapshot; call from a worker thread in a UI.</summary>
public sealed class ResearchEngine
{
    public const string Version = "0.1.0-m0";
    public const int MaxLookback = 2500;
    public const int MaxHorizon = 2500;

    public ResearchRun Run(DataSnapshot snapshot, ResearchDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDefinition(definition);
        // Records have init-only scalar fields; copy every collection before beginning a calculation.
        definition = definition with { Conditions = definition.Conditions.ToArray(), Horizons = definition.Horizons.ToArray() };
        if (snapshot.Calendar?.TradingDates is null || snapshot.ActionCoverage?.Gaps is null ||
            snapshot.Bars is null || snapshot.CorporateActions is null)
            throw new ArgumentException("資料快照缺少必要集合。", nameof(snapshot));
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.ToArray(), CorporateActions = snapshot.CorporateActions.ToArray(),
            Calendar = snapshot.Calendar with { TradingDates = snapshot.Calendar.TradingDates.ToArray() },
            ActionCoverage = snapshot.ActionCoverage with { Gaps = snapshot.ActionCoverage.Gaps.ToArray() }
        };
        var diagnostics = SnapshotValidator.Validate(snapshot, cancellationToken).ToList();
        if (definition.EventStart < snapshot.Calendar.CoverageStart || definition.EventEnd > snapshot.Calendar.CoverageEnd)
            diagnostics.Add(new DataIssue { Code = "EventRangeOutsideCalendar", Message = "事件範圍超出已確認的市場日曆覆蓋。", BlocksResearch = true });
        if (definition.DataAsOf > snapshot.DataAsOf)
            diagnostics.Add(new DataIssue { Code = "AsOfBeyondSnapshot", Message = "研究截止日晚於此快照的已完成交易日截止日。", BlocksResearch = true });

        var hash = "";
        if (!diagnostics.Any(d => d.BlocksResearch))
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash = SnapshotFingerprint.Compute(snapshot);
            if (!string.IsNullOrEmpty(snapshot.ContentHash) && !string.Equals(snapshot.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                diagnostics.Add(new DataIssue { Code = "SnapshotHashMismatch", Message = "快照內容與保存的內容雜湊不同。", BlocksResearch = true });
        }
        var blocking = diagnostics.Where(d => d.BlocksResearch).Select(d => d.Message).Distinct().ToArray();
        var run = new ResearchRun
        {
            EngineVersion = Version, DataSnapshotId = string.IsNullOrEmpty(snapshot.SnapshotId) ? hash : snapshot.SnapshotId,
            DataContentHash = hash, SourceId = snapshot.SourceId, CalendarVersion = snapshot.Calendar.Version,
            CorporateActionVersion = snapshot.ActionCoverage.Version, Instrument = snapshot.Instrument,
            Definition = definition, IsSynthetic = snapshot.IsSynthetic, IsResearchAllowed = blocking.Length == 0,
            BlockingReasons = blocking, Diagnostics = diagnostics.ToArray()
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (!run.IsResearchAllowed) return run;

        var context = new Calculation(snapshot, definition, cancellationToken);
        var evaluations = new List<EventEvaluation>();
        var cases = new List<ResearchCase>();
        var horizons = definition.Horizons.Order().ToArray();
        var lastSampledIndex = -MaxHorizon - 1;
        var candidates = 0;
        var determinable = 0;
        var rawMatches = 0;
        for (var index = 0; index < context.Dates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var date = context.Dates[index];
            if (date < definition.EventStart || date > definition.EventEnd) continue;
            candidates++;
            var evaluation = context.Evaluate(index);
            if (evaluation.State != MatchState.Unknown) determinable++;
            if (evaluation.State != MatchState.Matched) { evaluations.Add(evaluation); continue; }
            rawMatches++;

            ExclusionReason? samplingExclusion = null;
            if (definition.SamplingPolicy == SamplingPolicy.FirstInRun)
            {
                var previous = context.Evaluate(index - 1).State;
                samplingExclusion = previous switch
                {
                    MatchState.Unknown => ExclusionReason.EventStartUnknown,
                    MatchState.Matched => ExclusionReason.ConsecutiveMatch,
                    _ => null
                };
            }
            else if (definition.SamplingPolicy == SamplingPolicy.NonOverlapping && index <= lastSampledIndex + horizons[^1])
                samplingExclusion = ExclusionReason.OverlappingWindow;

            evaluation = evaluation with { IsSampled = samplingExclusion is null, SamplingExclusion = samplingExclusion };
            evaluations.Add(evaluation);
            if (samplingExclusion is not null) continue;
            // Commit sampling before checking any future outcome, including excluded outcomes.
            lastSampledIndex = index;
            cases.Add(new ResearchCase
            {
                EventDate = date, ConditionWindowStart = context.Dates[index - context.Lookback],
                BaseClose = context.Bar(index)!.Close!.Value, Features = evaluation.Features,
                Outcomes = horizons.Select(h => context.Observe(index, h)).ToArray()
            });
        }
        cancellationToken.ThrowIfCancellationRequested();
        var statistics = horizons.Select(h => Summarize(h, cases, cancellationToken)).ToArray();
        return run with
        {
            Funnel = new ResearchFunnel
            {
                CandidateDates = candidates, DeterminableDates = determinable,
                RawMatchDays = rawMatches, SampledEvents = cases.Count
            },
            Evaluations = evaluations.ToArray(), Cases = cases.ToArray(), Statistics = statistics
        };
    }

    private static void ValidateDefinition(ResearchDefinition definition)
    {
        if (definition.EventStart == default || definition.EventEnd < definition.EventStart || definition.DataAsOf < definition.EventEnd)
            throw new ArgumentException("事件日期須有合法起訖，且不得晚於研究截止日。", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.TemplateId) || string.IsNullOrWhiteSpace(definition.TemplateVersion))
            throw new ArgumentException("研究設定需保存模型 ID 與版本。", nameof(definition));
        if (!Enum.IsDefined(definition.SamplingPolicy) || definition.PriceMode != PriceMode.ConservativeRaw)
            throw new ArgumentException("不支援此採樣或價格口徑。", nameof(definition));
        if (definition.Conditions is null || definition.Conditions.Count is < 1 or > 6)
            throw new ArgumentException("研究需包含 1 至 6 個 AND 條件。", nameof(definition));
        for (var i = 0; i < definition.Conditions.Count; i++)
        {
            var condition = definition.Conditions[i];
            if (condition is null || !Enum.IsDefined(condition.Feature) || !Enum.IsDefined(condition.Operator) ||
                condition.Lookback is < 1 or > MaxLookback || condition.Value != decimal.Round(condition.Value, 8))
                throw new ArgumentException($"第 {i + 1} 個條件不合法：回看須為 1 至 {MaxLookback} 日，數值最多 8 位小數。", nameof(definition));
            if (condition.Feature is ResearchFeature.ConsecutiveDeclines or ResearchFeature.ConsecutiveAdvances &&
                (condition.Value < 0 || condition.Value > condition.Lookback || condition.Value != decimal.Truncate(condition.Value)))
                throw new ArgumentException($"第 {i + 1} 個連續日數條件須為 0 至回看長度的整數。", nameof(definition));
        }
        if (definition.Horizons is null || definition.Horizons.Count is < 1 or > 4 ||
            definition.Horizons.Any(h => h is < 1 or > MaxHorizon) || definition.Horizons.Distinct().Count() != definition.Horizons.Count)
            throw new ArgumentException($"觀察期須為 1 至 4 個不重複的日數，每期介於 1 至 {MaxHorizon}。", nameof(definition));
        if (definition.UpperThreshold <= 0 || definition.LowerThreshold >= 0 || definition.LowerThreshold <= -1 ||
            definition.UpperThreshold != decimal.Round(definition.UpperThreshold, 8) ||
            definition.LowerThreshold != decimal.Round(definition.LowerThreshold, 8))
            throw new ArgumentException("上方門檻須大於 0，下方門檻須介於 -100% 與 0；最多 8 位小數。", nameof(definition));
    }

    private sealed class Calculation
    {
        private readonly DataSnapshot _snapshot;
        private readonly ResearchDefinition _definition;
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<DateOnly, DailyBar> _bars;
        private readonly Dictionary<int, EventEvaluation> _evaluations = [];
        public DateOnly[] Dates { get; }
        public int Lookback { get; }

        public Calculation(DataSnapshot snapshot, ResearchDefinition definition, CancellationToken cancellationToken)
        {
            _snapshot = snapshot;
            _definition = definition;
            _cancellationToken = cancellationToken;
            Dates = snapshot.Calendar.TradingDates.Order().ToArray();
            _bars = snapshot.Bars.ToDictionary(b => b.Date);
            Lookback = definition.Conditions.Max(c => c.Lookback);
        }

        public DailyBar? Bar(int index) => index >= 0 && index < Dates.Length ? _bars.GetValueOrDefault(Dates[index]) : null;

        public EventEvaluation Evaluate(int index)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_evaluations.TryGetValue(index, out var cached)) return cached;
            var reasons = new HashSet<ExclusionReason>();
            if (index < Lookback)
                reasons.Add(ExclusionReason.InsufficientHistory);
            else
            {
                AddActionReasons(Dates[index - Lookback], Dates[index], reasons);
                // Strict quality policy: every bar in the condition window needs valid raw OHLC.
                for (var j = index - Lookback; j <= index; j++)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    foreach (var reason in SnapshotValidator.GetBarReasons(Bar(j), false)) reasons.Add(reason);
                }
                foreach (var condition in _definition.Conditions.Where(c => c.Feature == ResearchFeature.RelativeVolume))
                    for (var j = index - condition.Lookback; j <= index; j++)
                        if (Bar(j)?.Volume is null) reasons.Add(ExclusionReason.MissingVolume);
            }
            FeatureValue[] features;
            MatchState state;
            if (reasons.Count > 0)
            {
                features = _definition.Conditions.Select(c => new FeatureValue { Condition = c, State = MatchState.Unknown }).ToArray();
                state = MatchState.Unknown;
            }
            else
            {
                var values = new Dictionary<(ResearchFeature, int), decimal?>();
                features = _definition.Conditions.Select(condition =>
                {
                    var key = (condition.Feature, condition.Lookback);
                    if (!values.TryGetValue(key, out var value)) values[key] = value = Feature(index, condition);
                    if (value is null) reasons.Add(ExclusionReason.UndefinedFeature);
                    return new FeatureValue
                    {
                        Condition = condition, Value = value,
                        State = value is null ? MatchState.Unknown : Compare(value.Value, condition.Operator, condition.Value)
                            ? MatchState.Matched : MatchState.NotMatched
                    };
                }).ToArray();
                // AND is conclusively false when a computable condition is false; otherwise unknown propagates.
                state = features.Any(f => f.State == MatchState.NotMatched) ? MatchState.NotMatched :
                    features.Any(f => f.State == MatchState.Unknown) ? MatchState.Unknown : MatchState.Matched;
            }
            var result = new EventEvaluation
            {
                Date = index >= 0 ? Dates[index] : default, State = state, Features = features, Reasons = Ordered(reasons)
            };
            _evaluations[index] = result;
            return result;
        }

        private decimal? Feature(int index, ResearchCondition condition)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var close = Bar(index)!.Close!.Value;
            var n = condition.Lookback;
            if (condition.Feature == ResearchFeature.PriceChange) return close / Bar(index - n)!.Close!.Value - 1;
            if (condition.Feature is ResearchFeature.ConsecutiveDeclines or ResearchFeature.ConsecutiveAdvances)
            {
                var count = 0;
                for (var j = index; j > index - n; j--)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var change = Bar(j)!.Close!.Value - Bar(j - 1)!.Close!.Value;
                    if (condition.Feature == ResearchFeature.ConsecutiveDeclines ? change >= 0 : change <= 0) break;
                    count++;
                }
                return count;
            }
            decimal? low = null, high = null;
            var volume = 0m;
            for (var j = index - n; j < index; j++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var bar = Bar(j)!;
                low = low is null ? bar.Low!.Value : Math.Min(low.Value, bar.Low!.Value);
                high = high is null ? bar.High!.Value : Math.Max(high.Value, bar.High!.Value);
                if (condition.Feature == ResearchFeature.RelativeVolume) volume += bar.Volume!.Value;
            }
            return condition.Feature switch
            {
                ResearchFeature.PriorLow => low,
                ResearchFeature.PriorHigh => high,
                ResearchFeature.DistanceFromPriorLow => close / low!.Value - 1,
                ResearchFeature.RelativeToPriorHigh => close / high!.Value - 1,
                ResearchFeature.RangePosition => high == low ? null : (close - low!.Value) / (high!.Value - low.Value),
                ResearchFeature.RelativeVolume => volume == 0 ? null : Bar(index)!.Volume!.Value / (volume / n),
                _ => throw new ArgumentOutOfRangeException(nameof(condition))
            };
        }

        public HorizonOutcome Observe(int index, int horizon)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var endIndex = index + horizon;
            var endDate = endIndex < Dates.Length ? Dates[endIndex] : (DateOnly?)null;
            var reasons = new HashSet<ExclusionReason>();
            if (endDate is null || endDate > _definition.DataAsOf) reasons.Add(ExclusionReason.InsufficientFutureData);
            var knownEnd = Math.Min(endIndex, Dates.Length - 1);
            while (knownEnd > index && Dates[knownEnd] > _definition.DataAsOf) knownEnd--;
            AddActionReasons(Dates[index - Lookback], Dates[knownEnd], reasons);
            for (var j = index + 1; j <= knownEnd; j++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                foreach (var reason in SnapshotValidator.GetBarReasons(Bar(j), false)) reasons.Add(reason);
            }
            if (reasons.Count > 0)
            {
                var ordered = Ordered(reasons);
                return new HorizonOutcome { Horizon = horizon, EndDate = endDate, PrimaryExclusion = ordered[0], Exclusions = ordered };
            }

            var p0 = Bar(index)!.Close!.Value;
            var peak = p0;
            var maxDrawdown = 0m;
            var high = decimal.MinValue;
            var low = decimal.MaxValue;
            int? upper = null, lower = null;
            var upperPrice = p0 * (1 + _definition.UpperThreshold);
            var lowerPrice = p0 * (1 + _definition.LowerThreshold);
            for (var j = index + 1; j <= endIndex; j++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var bar = Bar(j)!;
                high = Math.Max(high, bar.High!.Value);
                low = Math.Min(low, bar.Low!.Value);
                peak = Math.Max(peak, bar.Close!.Value);
                maxDrawdown = Math.Min(maxDrawdown, bar.Close.Value / peak - 1);
                if (upper is null && bar.High.Value >= upperPrice) upper = j - index;
                if (lower is null && bar.Low.Value <= lowerPrice) lower = j - index;
            }
            var order = upper is null && lower is null ? ThresholdOrder.Neither :
                upper == lower ? ThresholdOrder.SameDayUnknown :
                lower is null || upper.HasValue && upper < lower ? ThresholdOrder.UpperFirst : ThresholdOrder.LowerFirst;
            return new HorizonOutcome
            {
                Horizon = horizon, EndDate = endDate, PriceChange = Bar(endIndex)!.Close!.Value / p0 - 1,
                HighestPriceChange = high / p0 - 1, LowestPriceChange = low / p0 - 1, CloseMaxDrawdown = maxDrawdown,
                UpperFirstHitTradingDay = upper, LowerFirstHitTradingDay = lower,
                UpperFirstHitDate = upper.HasValue ? Dates[index + upper.Value] : null,
                LowerFirstHitDate = lower.HasValue ? Dates[index + lower.Value] : null, ThresholdOrder = order
            };
        }

        private void AddActionReasons(DateOnly start, DateOnly end, HashSet<ExclusionReason> reasons)
        {
            var coverage = _snapshot.ActionCoverage;
            if (start < coverage.CoverageStart || end > coverage.CoverageEnd || coverage.Gaps.Any(g => g.Start <= end && g.End >= start))
                reasons.Add(ExclusionReason.CorporateActionCoverageUnknown);
            if (_snapshot.CorporateActions.Any(a => a.AffectsPriceComparison && a.EffectiveDate >= start && a.EffectiveDate <= end))
                reasons.Add(ExclusionReason.CorporateActionInWindow);
        }
    }

    private static bool Compare(decimal value, ComparisonOperator op, decimal target) => op switch
    {
        ComparisonOperator.GreaterThan => value > target,
        ComparisonOperator.GreaterThanOrEqual => value >= target,
        ComparisonOperator.LessThan => value < target,
        ComparisonOperator.LessThanOrEqual => value <= target,
        ComparisonOperator.Equal => value == target,
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };

    // One stable primary reason per excluded horizon; all simultaneous flags remain on each outcome.
    private static ExclusionReason[] Ordered(IEnumerable<ExclusionReason> reasons) => reasons.Distinct()
        .OrderBy(r => r switch
        {
            ExclusionReason.InsufficientFutureData => 0,
            ExclusionReason.CorporateActionCoverageUnknown => 1,
            ExclusionReason.CorporateActionInWindow => 2,
            ExclusionReason.InsufficientHistory => 3,
            ExclusionReason.MissingBar => 4,
            ExclusionReason.NonTradingBar => 5,
            ExclusionReason.MissingPrice => 6,
            ExclusionReason.InvalidBar => 7,
            ExclusionReason.MissingVolume => 8,
            _ => 9 + (int)r
        }).ToArray();

    private static HorizonStatistics Summarize(int horizon, IReadOnlyList<ResearchCase> cases, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var all = cases.Select(c => (Case: c, Outcome: c.Outcomes.Single(o => o.Horizon == horizon))).ToArray();
        var valid = all.Where(c => c.Outcome.IsValid).ToArray();
        var exclusions = all.Where(c => !c.Outcome.IsValid).GroupBy(c => c.Outcome.PrimaryExclusion!.Value)
            .OrderBy(g => g.Key).Select(g => new ExclusionCount { Reason = g.Key, Count = g.Count() }).ToArray();
        var stats = new HorizonStatistics { Horizon = horizon, ValidCount = valid.Length, ExcludedCount = all.Length - valid.Length, Exclusions = exclusions };
        if (valid.Length == 0) return stats;
        var changes = valid.Select(c => c.Outcome.PriceChange!.Value).Order().ToArray();
        var up = changes.Count(r => r > 0);
        var flat = changes.Count(r => r == 0);
        var down = changes.Count(r => r < 0);
        var upper = valid.Count(c => c.Outcome.UpperFirstHitTradingDay.HasValue);
        var lower = valid.Count(c => c.Outcome.LowerFirstHitTradingDay.HasValue);
        cancellationToken.ThrowIfCancellationRequested();
        return stats with
        {
            UpCount = up, FlatCount = flat, DownCount = down,
            UpRate = (decimal)up / valid.Length, FlatRate = (decimal)flat / valid.Length, DownRate = (decimal)down / valid.Length,
            MeanPriceChange = changes.Average(), MedianPriceChange = Percentile(changes, 0.5m),
            P10 = Percentile(changes, 0.1m), P25 = Percentile(changes, 0.25m), P75 = Percentile(changes, 0.75m), P90 = Percentile(changes, 0.9m),
            BestPriceChange = changes[^1], WorstPriceChange = changes[0],
            BestEventDate = valid.First(c => c.Outcome.PriceChange == changes[^1]).Case.EventDate,
            WorstEventDate = valid.First(c => c.Outcome.PriceChange == changes[0]).Case.EventDate,
            MeanHighestPriceChange = valid.Average(c => c.Outcome.HighestPriceChange!.Value),
            MeanLowestPriceChange = valid.Average(c => c.Outcome.LowestPriceChange!.Value),
            MeanCloseMaxDrawdown = valid.Average(c => c.Outcome.CloseMaxDrawdown!.Value),
            UpperHitCount = upper, LowerHitCount = lower, UpperHitRate = (decimal)upper / valid.Length, LowerHitRate = (decimal)lower / valid.Length,
            UpperFirstCount = valid.Count(c => c.Outcome.ThresholdOrder == ThresholdOrder.UpperFirst),
            LowerFirstCount = valid.Count(c => c.Outcome.ThresholdOrder == ThresholdOrder.LowerFirst),
            SameDayUnknownCount = valid.Count(c => c.Outcome.ThresholdOrder == ThresholdOrder.SameDayUnknown),
            NeitherHitCount = valid.Count(c => c.Outcome.ThresholdOrder == ThresholdOrder.Neither)
        };
    }

    private static decimal Percentile(decimal[] sorted, decimal probability)
    {
        var position = (sorted.Length - 1) * probability;
        var lower = (int)decimal.Floor(position);
        var upper = (int)decimal.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
