namespace HistoLens.Core;

public enum SecurityType { CommonStock, Synthetic }
public enum TradingStatus { Traded, Suspended, NoTrading, ReferenceOnly }
public enum SamplingPolicy { FirstInRun, EveryMatch, NonOverlapping }
public enum PriceMode { ConservativeRaw }
public enum MatchState { Unknown, NotMatched, Matched }
public enum ComparisonOperator { GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal }
public enum ResearchFeature
{
    PriceChange, PriorHigh, PriorLow, DistanceFromPriorLow, RelativeToPriorHigh,
    RangePosition, RelativeVolume, ConsecutiveDeclines, ConsecutiveAdvances
}
public enum ThresholdOrder { UpperFirst, LowerFirst, SameDayUnknown, Neither }
public enum ExclusionReason
{
    InsufficientHistory, MissingBar, NonTradingBar, MissingPrice, InvalidBar,
    MissingVolume, UndefinedFeature, CorporateActionCoverageUnknown, CorporateActionInWindow,
    InsufficientFutureData, EventStartUnknown, ConsecutiveMatch, OverlappingWindow
}

public sealed record Instrument
{
    public string InstrumentId { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Market { get; init; } = "";
    public string Currency { get; init; } = "TWD";
    public SecurityType SecurityType { get; init; } = SecurityType.CommonStock;
    public DateOnly? ListedFrom { get; init; }
    public DateOnly? ListedUntil { get; init; }
}

public sealed record DailyBar
{
    public DateOnly Date { get; init; }
    public decimal? Open { get; init; }
    public decimal? High { get; init; }
    public decimal? Low { get; init; }
    public decimal? Close { get; init; }
    public decimal? Volume { get; init; }
    public decimal? Turnover { get; init; }
    public TradingStatus Status { get; init; } = TradingStatus.Traded;
    public string SourceId { get; init; } = "";
    public string OriginalVolumeUnit { get; init; } = "shares";
}

/// <summary>An explicit market calendar. Dates with a missing/suspended bar remain trading dates.</summary>
public sealed record TradingCalendar
{
    public string Market { get; init; } = "";
    public string Version { get; init; } = "";
    public bool IsVerified { get; init; }
    public DateOnly CoverageStart { get; init; }
    public DateOnly CoverageEnd { get; init; }
    public IReadOnlyList<DateOnly> TradingDates { get; init; } = [];
}

public sealed record DateRange
{
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
}

public sealed record CorporateAction
{
    public DateOnly EffectiveDate { get; init; }
    public string Kind { get; init; } = "";
    public string SourceId { get; init; } = "";
    public bool AffectsPriceComparison { get; init; } = true;
}

/// <summary>Verified absence of an action is different from an unavailable action feed.</summary>
public sealed record CorporateActionCoverage
{
    public bool IsVerified { get; init; }
    public DateOnly CoverageStart { get; init; }
    public DateOnly CoverageEnd { get; init; }
    public string Version { get; init; } = "";
    public string SourceId { get; init; } = "";
    public IReadOnlyList<DateRange> Gaps { get; init; } = [];
}

public sealed record DataSnapshot
{
    public string SnapshotId { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public Instrument Instrument { get; init; } = new();
    public string SourceId { get; init; } = "";
    public string DataVersion { get; init; } = "1";
    public DateTimeOffset RetrievedAtUtc { get; init; }
    public DateOnly DataAsOf { get; init; }
    public bool IsSynthetic { get; init; }
    public TradingCalendar Calendar { get; init; } = new();
    public CorporateActionCoverage ActionCoverage { get; init; } = new();
    public IReadOnlyList<CorporateAction> CorporateActions { get; init; } = [];
    public IReadOnlyList<DailyBar> Bars { get; init; } = [];
}

/// <summary>Percentage inputs use decimal fractions: 0.05 means 5%; relative volume uses a multiplier.</summary>
public sealed record ResearchCondition
{
    public ResearchFeature Feature { get; init; }
    public int Lookback { get; init; } = 1;
    public ComparisonOperator Operator { get; init; } = ComparisonOperator.GreaterThanOrEqual;
    public decimal Value { get; init; }
}

public sealed record ResearchDefinition
{
    public string TemplateId { get; init; } = "custom";
    public string TemplateVersion { get; init; } = "1.0";
    public string Name { get; init; } = "自訂研究";
    public DateOnly EventStart { get; init; }
    public DateOnly EventEnd { get; init; }
    public DateOnly DataAsOf { get; init; }
    public IReadOnlyList<ResearchCondition> Conditions { get; init; } = [];
    public IReadOnlyList<int> Horizons { get; init; } = [5, 10, 20, 60];
    public SamplingPolicy SamplingPolicy { get; init; } = SamplingPolicy.FirstInRun;
    public PriceMode PriceMode { get; init; } = PriceMode.ConservativeRaw;
    public decimal UpperThreshold { get; init; } = 0.05m;
    public decimal LowerThreshold { get; init; } = -0.03m;
}

public sealed record ResearchTemplate
{
    public string Id { get; init; } = "";
    public string Version { get; init; } = "1.0";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Formula { get; init; } = "";
    public int DefaultLookback { get; init; }
    public decimal? DefaultThreshold { get; init; }
}

public sealed record DataIssue
{
    public DateOnly? Date { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public bool BlocksResearch { get; init; }
}

public sealed record FeatureValue
{
    public ResearchCondition Condition { get; init; } = new();
    public decimal? Value { get; init; }
    public MatchState State { get; init; }
}

public sealed record EventEvaluation
{
    public DateOnly Date { get; init; }
    public MatchState State { get; init; }
    public IReadOnlyList<FeatureValue> Features { get; init; } = [];
    public IReadOnlyList<ExclusionReason> Reasons { get; init; } = [];
    public bool IsSampled { get; init; }
    public ExclusionReason? SamplingExclusion { get; init; }
}

public sealed record ResearchFunnel
{
    public int CandidateDates { get; init; }
    public int DeterminableDates { get; init; }
    public int RawMatchDays { get; init; }
    public int SampledEvents { get; init; }
}

public sealed record HorizonOutcome
{
    public int Horizon { get; init; }
    public DateOnly? EndDate { get; init; }
    public ExclusionReason? PrimaryExclusion { get; init; }
    public IReadOnlyList<ExclusionReason> Exclusions { get; init; } = [];
    public bool IsValid => PrimaryExclusion is null;
    public decimal? PriceChange { get; init; }
    public decimal? HighestPriceChange { get; init; }
    public decimal? LowestPriceChange { get; init; }
    public decimal? CloseMaxDrawdown { get; init; }
    public int? UpperFirstHitTradingDay { get; init; }
    public int? LowerFirstHitTradingDay { get; init; }
    public DateOnly? UpperFirstHitDate { get; init; }
    public DateOnly? LowerFirstHitDate { get; init; }
    public ThresholdOrder? ThresholdOrder { get; init; }
}

public sealed record ResearchCase
{
    public DateOnly EventDate { get; init; }
    public DateOnly ConditionWindowStart { get; init; }
    public decimal BaseClose { get; init; }
    public IReadOnlyList<FeatureValue> Features { get; init; } = [];
    public IReadOnlyList<HorizonOutcome> Outcomes { get; init; } = [];
}

public sealed record ExclusionCount
{
    public ExclusionReason Reason { get; init; }
    public int Count { get; init; }
}

public sealed record HorizonStatistics
{
    public int Horizon { get; init; }
    public int ValidCount { get; init; }
    public int ExcludedCount { get; init; }
    public IReadOnlyList<ExclusionCount> Exclusions { get; init; } = [];
    public int UpCount { get; init; }
    public int FlatCount { get; init; }
    public int DownCount { get; init; }
    public decimal? UpRate { get; init; }
    public decimal? FlatRate { get; init; }
    public decimal? DownRate { get; init; }
    public decimal? MeanPriceChange { get; init; }
    public decimal? MedianPriceChange { get; init; }
    public decimal? P10 { get; init; }
    public decimal? P25 { get; init; }
    public decimal? P75 { get; init; }
    public decimal? P90 { get; init; }
    public decimal? BestPriceChange { get; init; }
    public decimal? WorstPriceChange { get; init; }
    public DateOnly? BestEventDate { get; init; }
    public DateOnly? WorstEventDate { get; init; }
    public decimal? MeanHighestPriceChange { get; init; }
    public decimal? MeanLowestPriceChange { get; init; }
    public decimal? MeanCloseMaxDrawdown { get; init; }
    public int UpperHitCount { get; init; }
    public int LowerHitCount { get; init; }
    public decimal? UpperHitRate { get; init; }
    public decimal? LowerHitRate { get; init; }
    public int UpperFirstCount { get; init; }
    public int LowerFirstCount { get; init; }
    public int SameDayUnknownCount { get; init; }
    public int NeitherHitCount { get; init; }
    public bool IsSmallSample => ValidCount is > 0 and < 30;
}

public sealed record ResearchRun
{
    public string EngineVersion { get; init; } = "";
    public string DataSnapshotId { get; init; } = "";
    public string DataContentHash { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string CalendarVersion { get; init; } = "";
    public string CorporateActionVersion { get; init; } = "";
    public Instrument Instrument { get; init; } = new();
    public ResearchDefinition Definition { get; init; } = new();
    public bool IsSynthetic { get; init; }
    public bool IsResearchAllowed { get; init; }
    public IReadOnlyList<string> BlockingReasons { get; init; } = [];
    public IReadOnlyList<DataIssue> Diagnostics { get; init; } = [];
    public ResearchFunnel Funnel { get; init; } = new();
    public IReadOnlyList<EventEvaluation> Evaluations { get; init; } = [];
    public IReadOnlyList<ResearchCase> Cases { get; init; } = [];
    public IReadOnlyList<HorizonStatistics> Statistics { get; init; } = [];
}
