namespace HistoLens.Core;

[Flags]
public enum SimilarityIndicator
{
    None = 0,
    PricePath = 1,
    HighPosition = 2,
    LowPosition = 4,
    Slope = 8,
    Volatility = 16,
    Rsi = 32,
    MovingAverageDeviation = 64,
    BollingerBandwidth = 128,
    NormalizedAtr = 256,
    VolumePath = 512,
    Default = PricePath | HighPosition | LowPosition | Slope | Volatility,
    All = Default | Rsi | MovingAverageDeviation | BollingerBandwidth | NormalizedAtr | VolumePath
}

/// <summary>Lookback is the number of market trading days, including the latest completed day.</summary>
public sealed record SimilarityDefinition
{
    public int Lookback { get; init; } = 20;
    public DateOnly ScanStart { get; init; }
    public DateOnly ScanEnd { get; init; }
    public DateOnly DataAsOf { get; init; }
    public decimal MinimumSimilarity { get; init; } = 60m;
    public SimilarityIndicator SelectedIndicators { get; init; } = SimilarityIndicator.Default;
}

/// <summary>Price changes, positions and volatility are fractions; slope is a fraction per trading day.</summary>
public sealed record SimilarityFeatures
{
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public decimal BaseClose { get; init; }
    public decimal EndClose { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal PriceChange { get; init; }
    public decimal DistanceFromHigh { get; init; }
    public decimal DistanceFromLow { get; init; }
    public decimal Slope { get; init; }
    public decimal Volatility { get; init; }
    public IReadOnlyList<decimal> NormalizedCloses { get; init; } = [];
    public decimal? Rsi { get; init; }
    public decimal? MovingAverageDeviation { get; init; }
    public decimal? BollingerBandwidth { get; init; }
    public decimal? NormalizedAtr { get; init; }
    public decimal? EndRelativeVolume { get; init; }
    public IReadOnlyList<decimal> NormalizedVolumes { get; init; } = [];
    /// <summary>Known events and unverified coverage annotate a raw-price window without excluding it.</summary>
    public IReadOnlyList<DataIssue> Warnings { get; init; } = [];
}

/// <summary>Each selected indicator contributes one equally weighted score between 0 and 100.</summary>
public sealed record SimilarityScores
{
    public SimilarityIndicator SelectedIndicators { get; init; } = SimilarityIndicator.Default;
    public decimal PricePath { get; init; }
    public decimal HighPosition { get; init; }
    public decimal LowPosition { get; init; }
    public decimal Slope { get; init; }
    public decimal Volatility { get; init; }
    public decimal Rsi { get; init; }
    public decimal MovingAverageDeviation { get; init; }
    public decimal BollingerBandwidth { get; init; }
    public decimal NormalizedAtr { get; init; }
    public decimal VolumePath { get; init; }

    public decimal Total
    {
        get
        {
            if (SelectedIndicators == SimilarityIndicator.None || (SelectedIndicators & ~SimilarityIndicator.All) != 0)
                throw new InvalidOperationException("總相似度須有至少一個已知的比較指標。");
            var total = 0m;
            var count = 0;
            for (var bit = 1; bit <= (int)SimilarityIndicator.VolumePath; bit <<= 1)
                if (GetScore((SimilarityIndicator)bit) is { } score) { total += score; count++; }
            return total / count;
        }
    }

    /// <summary>Unselected indicators have no score; only a single known indicator is accepted.</summary>
    public decimal? GetScore(SimilarityIndicator indicator)
    {
        var score = indicator switch
        {
            SimilarityIndicator.PricePath => PricePath,
            SimilarityIndicator.HighPosition => HighPosition,
            SimilarityIndicator.LowPosition => LowPosition,
            SimilarityIndicator.Slope => Slope,
            SimilarityIndicator.Volatility => Volatility,
            SimilarityIndicator.Rsi => Rsi,
            SimilarityIndicator.MovingAverageDeviation => MovingAverageDeviation,
            SimilarityIndicator.BollingerBandwidth => BollingerBandwidth,
            SimilarityIndicator.NormalizedAtr => NormalizedAtr,
            SimilarityIndicator.VolumePath => VolumePath,
            _ => throw new ArgumentOutOfRangeException(nameof(indicator), "須指定單一已知指標。")
        };
        return (SelectedIndicators & indicator) != 0 ? score : null;
    }
}

public sealed record SimilarityMatch
{
    public SimilarityFeatures Features { get; init; } = new();
    public SimilarityScores Scores { get; init; } = new();
}

public sealed record SimilarityRun
{
    public string EngineVersion { get; init; } = "";
    public SimilarityDefinition Definition { get; init; } = new();
    public SimilarityFeatures? Reference { get; init; }
    public bool IsAllowed { get; init; }
    public IReadOnlyList<string> BlockingReasons { get; init; } = [];
    public IReadOnlyList<DataIssue> Diagnostics { get; init; } = [];
    public IReadOnlyList<SimilarityMatch> Matches { get; init; } = [];
    /// <summary>Complete calendar windows within the scan range and strictly before the reference.</summary>
    public int CandidateCount { get; init; }
    public int ComparableCount { get; init; }
    /// <summary>Valid candidate windows carrying event or coverage warnings, before score filtering.</summary>
    public int WarningCandidateCount { get; init; }
    public int BelowThresholdCount { get; init; }
    public int OverlapExcludedCount { get; init; }
    /// <summary>One primary quality exclusion per noncomparable candidate, in stable reason order.</summary>
    public IReadOnlyList<ExclusionCount> Exclusions { get; init; } = [];
}
