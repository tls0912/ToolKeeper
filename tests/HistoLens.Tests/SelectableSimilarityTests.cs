using System.Text.Json;
using HistoLens.Core;
using Xunit;

namespace HistoLens.Tests;

public sealed class SelectableSimilarityTests
{
    private const string Source = "Tests.SelectableSimilarity";
    private readonly SimilarityEngine _engine = new();

    [Fact]
    public void OptionalFeaturesMatchIndependentWindowCalculationsIncludingPriceGaps()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 10m, 12m, 11m, 13m, 10m);
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.Select((bar, index) => index < 5 ? bar : bar with
            {
                High = bar.Close + 1m, Low = bar.Close - 1m, Volume = (index - 5) * 100m
            }).ToArray()
        };
        var run = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.All));
        var reference = run.Reference!;
        Assert.True(run.IsAllowed);
        // Changes +2, -1, +2, -3 give average gain=1, average loss=1.
        Assert.Equal(50m, reference.Rsi);
        AssertClose(10m / 11.2m - 1m, reference.MovingAverageDeviation!.Value);
        // Population close variance is 1.36; bands at +/-2 standard deviations have width 4 sigma / SMA.
        AssertClose(4m * (decimal)Math.Sqrt(1.36d) / 11.2m, reference.BollingerBandwidth!.Value);
        // TR = 3, 2, 3, 4, including overnight gaps; only bars 1..4 contribute.
        Assert.Equal(0.3m, reference.NormalizedAtr);
        Assert.Equal(new[] { 0m, 0.5m, 1m, 1.5m, 2m }, reference.NormalizedVolumes);
        Assert.Equal(2m, reference.EndRelativeVolume);
        var scores = Assert.Single(run.Matches).Scores;
        Assert.Equal(100m, scores.Rsi);
        AssertClose(100m / (1m + (decimal)Math.Sqrt(0.5d) / 0.5m), scores.VolumePath);
    }

    [Theory]
    [InlineData("flat", 50)]
    [InlineData("rising", 100)]
    [InlineData("falling", 0)]
    public void RsiHandlesFlatAndOneDirectionWindows(string kind, int expected)
    {
        var reference = kind switch
        {
            "flat" => new[] { 10m, 10m, 10m, 10m, 10m },
            "rising" => new[] { 10m, 11m, 12m, 13m, 14m },
            _ => new[] { 14m, 13m, 12m, 11m, 10m }
        };
        var snapshot = Snapshot(Enumerable.Repeat(100m, 5).Concat(reference).ToArray());
        Assert.Equal((decimal)expected, _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.Rsi)).Reference!.Rsi);
    }

    [Fact]
    public void RsiUsesInitialAverageChangesRatherThanReturnsOrContinuedSmoothing()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 10m, 12m, 11m, 13m, 12m);
        var run = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.Rsi));
        // Four close changes have total gain=4 and total loss=2; averaging cancels in the ratio.
        AssertClose(100m * 4m / 6m, run.Reference!.Rsi!.Value);
        AssertClose(100m / (1m + (100m * 4m / 6m - 50m) / 20m), Assert.Single(run.Matches).Scores.Rsi);
    }

    [Fact]
    public void AllIndicatorsAreInvariantToUniformPriceAndVolumeScaling()
    {
        var closes = new[] { 50m, 55m, 49.5m, 54.45m, 49.005m, 100m, 110m, 99m, 108.9m, 98.01m };
        var snapshot = Snapshot(closes);
        var volumes = new[] { 0m, 100m, 200m, 50m, 150m };
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.Select((bar, index) => bar with
            {
                High = bar.Close * 1.02m, Low = bar.Close * 0.98m,
                Volume = volumes[index % 5] * (index < 5 ? 1m : 100m)
            }).ToArray()
        };
        var run = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.All));
        var match = Assert.Single(run.Matches);
        Assert.Equal(100m, match.Scores.Total);
        foreach (var indicator in Indicators()) Assert.Equal(100m, match.Scores.GetScore(indicator));
        Assert.Equal(run.Reference!.NormalizedVolumes, match.Features.NormalizedVolumes);
        Assert.Equal(run.Reference.Rsi, match.Features.Rsi);
        Assert.Equal(run.Reference.MovingAverageDeviation, match.Features.MovingAverageDeviation);
        Assert.Equal(run.Reference.BollingerBandwidth, match.Features.BollingerBandwidth);
        Assert.Equal(run.Reference.NormalizedAtr, match.Features.NormalizedAtr);
    }

    [Theory]
    [InlineData(SimilarityIndicator.PricePath)]
    [InlineData(SimilarityIndicator.HighPosition)]
    [InlineData(SimilarityIndicator.LowPosition)]
    [InlineData(SimilarityIndicator.Slope)]
    [InlineData(SimilarityIndicator.Volatility)]
    [InlineData(SimilarityIndicator.Rsi)]
    [InlineData(SimilarityIndicator.MovingAverageDeviation)]
    [InlineData(SimilarityIndicator.BollingerBandwidth)]
    [InlineData(SimilarityIndicator.NormalizedAtr)]
    [InlineData(SimilarityIndicator.VolumePath)]
    public void AOneIndicatorTotalUsesOnlyThatScore(SimilarityIndicator selected)
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 100m, 105m, 110m, 115m, 120m);
        snapshot = snapshot with { Bars = snapshot.Bars.Select((bar, index) => bar with { Volume = index + 1m }).ToArray() };
        var run = _engine.Run(snapshot, Definition(snapshot, selected));
        var scores = Assert.Single(run.Matches).Scores;
        Assert.Equal(selected, scores.SelectedIndicators);
        Assert.Equal(scores.GetScore(selected), scores.Total);
        foreach (var unselected in Indicators().Where(indicator => indicator != selected)) Assert.Null(scores.GetScore(unselected));
        Assert.Equal(selected == SimilarityIndicator.Rsi, run.Reference!.Rsi.HasValue);
        Assert.Equal(selected == SimilarityIndicator.MovingAverageDeviation, run.Reference.MovingAverageDeviation.HasValue);
        Assert.Equal(selected == SimilarityIndicator.BollingerBandwidth, run.Reference.BollingerBandwidth.HasValue);
        Assert.Equal(selected == SimilarityIndicator.NormalizedAtr, run.Reference.NormalizedAtr.HasValue);
        Assert.Equal(selected == SimilarityIndicator.VolumePath, run.Reference.EndRelativeVolume.HasValue);
        if (selected != SimilarityIndicator.VolumePath) Assert.Empty(run.Reference.NormalizedVolumes);
    }

    [Fact]
    public void MixedSelectionTotalEquallyWeightsExactlyTheSelectedIndicators()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 100m, 105m, 110m, 115m, 120m);
        snapshot = snapshot with { Bars = snapshot.Bars.Select((bar, index) => bar with { Volume = index + 1m }).ToArray() };
        var selected = SimilarityIndicator.PricePath | SimilarityIndicator.Rsi | SimilarityIndicator.VolumePath;
        var scores = Assert.Single(_engine.Run(snapshot, Definition(snapshot, selected)).Matches).Scores;
        Assert.Equal((scores.PricePath + scores.Rsi + scores.VolumePath) / 3m, scores.Total);
        Assert.Null(scores.GetScore(SimilarityIndicator.HighPosition));
        Assert.Null(scores.GetScore(SimilarityIndicator.NormalizedAtr));
        var allScores = Assert.Single(_engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.All)).Matches).Scores;
        Assert.Equal(Indicators().Sum(indicator => allScores.GetScore(indicator)!.Value) / 10m, allScores.Total);
    }

    [Fact]
    public void SelectedScoreControlsInclusiveUnroundedThreshold()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 10m, 12m, 11m, 13m, 12m);
        var definition = Definition(snapshot, SimilarityIndicator.Rsi);
        var score = Assert.Single(_engine.Run(snapshot, definition).Matches).Scores.Total;
        Assert.NotEqual(decimal.Round(score, 2), score);
        Assert.Single(_engine.Run(snapshot, definition with { MinimumSimilarity = score }).Matches);
        var excluded = _engine.Run(snapshot, definition with { MinimumSimilarity = score + 0.000000000001m });
        Assert.Empty(excluded.Matches);
        Assert.Equal(1, excluded.BelowThresholdCount);
    }

    [Fact]
    public void DefaultSelectionPreservesLegacyScoresAndHasNoOptionalFeatures()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 100m, 105m, 110m, 115m, 120m);
        snapshot = snapshot with { Bars = snapshot.Bars.Select(bar => bar with { Volume = null }).ToArray() };
        var definition = new SimilarityDefinition
        {
            Lookback = 5, ScanStart = snapshot.Calendar.TradingDates[0], ScanEnd = snapshot.Calendar.TradingDates[4],
            DataAsOf = snapshot.DataAsOf, MinimumSimilarity = 0m
        };
        var run = _engine.Run(snapshot, definition);
        var scores = Assert.Single(run.Matches).Scores;
        Assert.Equal(SimilarityIndicator.Default, definition.SelectedIndicators);
        Assert.Equal(SimilarityIndicator.Default, scores.SelectedIndicators);
        AssertClose(100m / (1m + (decimal)Math.Sqrt(0.015d) / 0.05m), scores.PricePath);
        Assert.Equal(100m, scores.HighPosition);
        Assert.Equal(20m, scores.LowPosition);
        Assert.Equal(20m, scores.Slope);
        var returns = new[] { 0.05d, 110d / 105d - 1d, 115d / 110d - 1d, 120d / 115d - 1d };
        var mean = returns.Average();
        var volatility = (decimal)Math.Sqrt(returns.Average(value => Math.Pow(value - mean, 2)));
        AssertClose(100m / (1m + volatility / 0.02m), scores.Volatility);
        Assert.Equal((scores.PricePath + scores.HighPosition + scores.LowPosition + scores.Slope + scores.Volatility) / 5m, scores.Total);
        Assert.Null(run.Reference!.Rsi);
        Assert.Null(run.Reference.MovingAverageDeviation);
        Assert.Null(run.Reference.BollingerBandwidth);
        Assert.Null(run.Reference.NormalizedAtr);
        Assert.Null(run.Reference.EndRelativeVolume);
        Assert.Empty(run.Reference.NormalizedVolumes);
        Assert.Equal(JsonSerializer.Serialize(run), JsonSerializer.Serialize(_engine.Run(snapshot, definition with { SelectedIndicators = SimilarityIndicator.Default })));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(1055)]
    [InlineData(-1)]
    public void EmptyOrUnknownSelectionIsRejected(int mask)
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10).ToArray());
        var definition = Definition(snapshot, (SimilarityIndicator)mask);
        Assert.Throws<ArgumentException>(() => SimilarityEngine.ValidateDefinition(definition));
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot, definition));
        Assert.Throws<InvalidOperationException>(() => new SimilarityScores { SelectedIndicators = (SimilarityIndicator)mask }.Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(31)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(-1)]
    public void ScoreLookupRejectsUnknownOrCompositeIndicators(int indicator) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimilarityScores().GetScore((SimilarityIndicator)indicator));

    [Theory]
    [InlineData("missing", ExclusionReason.MissingVolume)]
    [InlineData("zero", ExclusionReason.UndefinedFeature)]
    public void HistoricalVolumeProblemsMatterOnlyWhenVolumePathIsSelected(string mutation, ExclusionReason reason)
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10).ToArray());
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.Select((bar, index) => index < 5 ? bar with { Volume = mutation == "missing" ? null : 0m } : bar).ToArray()
        };
        var unselected = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.Default));
        Assert.Single(unselected.Matches);
        Assert.Empty(unselected.Exclusions);
        var selected = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.VolumePath));
        Assert.True(selected.IsAllowed);
        Assert.Equal(1, selected.CandidateCount);
        Assert.Equal(0, selected.ComparableCount);
        Assert.Empty(selected.Matches);
        Assert.Equal(new ExclusionCount { Reason = reason, Count = 1 }, Assert.Single(selected.Exclusions));
    }

    [Theory]
    [InlineData("missing", "ReferenceMissingVolume")]
    [InlineData("zero", "ReferenceUndefinedFeature")]
    [InlineData("negative", "ReferenceInvalidBar")]
    public void ReferenceVolumeProblemsBlockWithAnExplicitReason(string mutation, string code)
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10).ToArray());
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.Select((bar, index) => index >= 5 ? bar with { Volume = mutation == "missing" ? null : mutation == "zero" ? 0m : -1m } : bar).ToArray()
        };
        var run = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.VolumePath));
        Assert.False(run.IsAllowed);
        Assert.Null(run.Reference);
        Assert.Contains(run.Diagnostics, issue => issue.Code == code && issue.BlocksResearch);
    }

    [Fact]
    public void ZeroIndividualVolumeIsAllowedWhenTheWindowMeanIsPositive()
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10).ToArray());
        snapshot = snapshot with { Bars = snapshot.Bars.Select((bar, index) => bar with { Volume = index % 5 == 0 ? 0m : 100m }).ToArray() };
        var run = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.VolumePath));
        Assert.True(run.IsAllowed);
        Assert.Equal(new[] { 0m, 1.25m, 1.25m, 1.25m, 1.25m }, run.Reference!.NormalizedVolumes);
        Assert.Equal(100m, Assert.Single(run.Matches).Scores.Total);
    }

    [Fact]
    public void ExtremeVolumesDoNotOverflowTheWindowMean()
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10).ToArray());
        snapshot = snapshot with { Bars = snapshot.Bars.Select(bar => bar with { Volume = decimal.MaxValue }).ToArray() };
        var run = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.VolumePath));
        Assert.True(run.IsAllowed);
        Assert.Equal(Enumerable.Repeat(1m, 5), run.Reference!.NormalizedVolumes);
        Assert.Equal(100m, Assert.Single(run.Matches).Scores.Total);
    }

    [Fact]
    public void ExtremeFlatPricesDoNotOverflowRsiOrBollingerOrMovingAverageMeans()
    {
        var snapshot = Snapshot(Enumerable.Repeat(decimal.MaxValue, 10).ToArray());
        var run = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.All));
        Assert.True(run.IsAllowed);
        Assert.Equal(50m, run.Reference!.Rsi);
        Assert.Equal(0m, run.Reference.MovingAverageDeviation);
        Assert.Equal(0m, run.Reference.BollingerBandwidth);
        Assert.Equal(0m, run.Reference.NormalizedAtr);
        Assert.Equal(100m, Assert.Single(run.Matches).Scores.Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrepresentableAtrIsIsolatedToSelectedHistoricalOrReferenceWindows(bool reference)
    {
        var snapshot = Snapshot(Enumerable.Repeat(0.1m, 10).ToArray());
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.Select((bar, index) => (index >= 5) == reference ? bar with { High = decimal.MaxValue } : bar).ToArray()
        };
        var disabled = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.Default));
        Assert.True(disabled.IsAllowed);
        Assert.Single(disabled.Matches);
        var enabled = _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.NormalizedAtr));
        Assert.Empty(enabled.Matches);
        if (reference)
        {
            Assert.False(enabled.IsAllowed);
            Assert.Contains(enabled.Diagnostics, issue => issue.Code == "ReferenceUndefinedFeature" && issue.BlocksResearch);
        }
        else
        {
            Assert.True(enabled.IsAllowed);
            Assert.Equal(ExclusionReason.UndefinedFeature, Assert.Single(enabled.Exclusions).Reason);
        }
    }

    [Fact]
    public void NewIndicatorsNeverReadBeforeOrAfterTheirOwnWindowsIncludingFutureBars()
    {
        var closes = Enumerable.Repeat(100m, 23).ToArray();
        var pattern = new[] { 100m, 105m, 102m, 108m, 103m };
        Array.Copy(pattern, 0, closes, 4, 5);
        Array.Copy(pattern, 0, closes, 13, 5);
        var snapshot = Snapshot(closes);
        var definition = Definition(snapshot, SimilarityIndicator.All) with
        {
            ScanStart = snapshot.Calendar.TradingDates[4], ScanEnd = snapshot.Calendar.TradingDates[8],
            DataAsOf = snapshot.Calendar.TradingDates[17]
        };
        var run = _engine.Run(snapshot, definition);
        var changed = snapshot with
        {
            Bars = snapshot.Bars.Select((bar, index) => index is >= 4 and <= 8 or >= 13 and <= 17 ? bar : bar with
            {
                Open = 10000m, High = 20000m, Low = 5000m, Close = 10000m, Volume = 999999m
            }).ToArray()
        };
        var changedRun = _engine.Run(changed, definition);
        Assert.Single(run.Matches);
        Assert.Equal(100m, run.Matches[0].Scores.Total);
        Assert.Equal(JsonSerializer.Serialize(run), JsonSerializer.Serialize(changedRun));
    }

    [Fact]
    public void TheIndicatorMaskIsCapturedAsAnImmutableDefinitionValue()
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10).ToArray());
        var definition = Definition(snapshot, SimilarityIndicator.Rsi);
        var run = _engine.Run(snapshot, definition);
        definition = definition with { SelectedIndicators = SimilarityIndicator.VolumePath };
        Assert.Equal(SimilarityIndicator.Rsi, run.Definition.SelectedIndicators);
        Assert.Equal(SimilarityIndicator.Rsi, Assert.Single(run.Matches).Scores.SelectedIndicators);
        Assert.NotEqual(definition.SelectedIndicators, run.Definition.SelectedIndicators);
    }

    [Fact]
    public void ACancelledScanWithAllOptionalIndicatorsThrowsCancellation()
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10).ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => _engine.Run(snapshot, Definition(snapshot, SimilarityIndicator.All), cancellation.Token));
    }

    private static SimilarityIndicator[] Indicators() =>
        Enumerable.Range(0, 10).Select(index => (SimilarityIndicator)(1 << index)).ToArray();

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0m, 0.000000000001m);

    private static SimilarityDefinition Definition(DataSnapshot snapshot, SimilarityIndicator selected) => new()
    {
        Lookback = 5, ScanStart = snapshot.Calendar.TradingDates[0], ScanEnd = snapshot.Calendar.TradingDates[4],
        DataAsOf = snapshot.DataAsOf, MinimumSimilarity = 0m, SelectedIndicators = selected
    };

    private static DataSnapshot Snapshot(params decimal[] closes)
    {
        var dates = new List<DateOnly>();
        var date = new DateOnly(2024, 1, 2);
        while (dates.Count < closes.Length)
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dates.Add(date);
            date = date.AddDays(1);
        }
        return new DataSnapshot
        {
            Instrument = new() { InstrumentId = "synthetic:selectable:test", Code = "TEST", Name = "Synthetic indicator fixture", Market = "SYNTHETIC", SecurityType = SecurityType.Synthetic },
            SourceId = Source, DataVersion = "selectable-test-v1", IsSynthetic = true, DataAsOf = dates[^1],
            Calendar = new() { Market = "SYNTHETIC", Version = "fixture-calendar-v1", IsVerified = true, TradingDates = dates, CoverageStart = dates[0], CoverageEnd = dates[^1] },
            ActionCoverage = new() { IsVerified = true, SourceId = Source, Version = "fixture-actions-v1", CoverageStart = dates[0], CoverageEnd = dates[^1] },
            Bars = closes.Select((close, index) => new DailyBar { Date = dates[index], Open = close, High = close, Low = close, Close = close, Volume = 100m, SourceId = Source }).ToArray()
        };
    }
}
