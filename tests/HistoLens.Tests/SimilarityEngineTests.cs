using System.Collections;
using System.Text.Json;
using HistoLens.Core;
using Xunit;

namespace HistoLens.Tests;

public sealed class SimilarityEngineTests
{
    private const string Source = "Tests.Synthetic";
    private readonly SimilarityEngine _engine = new();

    [Fact]
    public void ScaledIdenticalPathsHaveOneHundredForEveryScore()
    {
        var snapshot = Snapshot(50m, 55m, 49.5m, 54.45m, 49.005m, 100m, 110m, 99m, 108.9m, 98.01m);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        var match = Assert.Single(run.Matches);
        Assert.True(run.IsAllowed);
        Assert.Equal(run.Reference!.NormalizedCloses, match.Features.NormalizedCloses);
        Assert.Equal(new SimilarityScores { PricePath = 100, HighPosition = 100, LowPosition = 100, Slope = 100, Volatility = 100 }, match.Scores);
        Assert.Equal(100m, match.Scores.Total);
        Assert.Equal(1, run.CandidateCount);
        Assert.Equal(1, run.ComparableCount);
    }

    [Fact]
    public void FeaturesUseWholeWindowExtremaOlsSlopeAndPopulationReturnDeviation()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 100m, 110m, 99m, 108.9m, 98.01m);
        var reference = _engine.Run(snapshot, Definition(snapshot, 0, 4)).Reference!;
        Assert.Equal(snapshot.Calendar.TradingDates[5], reference.Start);
        Assert.Equal(snapshot.DataAsOf, reference.End);
        Assert.Equal(100m, reference.BaseClose);
        Assert.Equal(98.01m, reference.EndClose);
        Assert.Equal(110m, reference.High);
        Assert.Equal(98.01m, reference.Low);
        Assert.Equal(-0.0199m, reference.PriceChange);
        Assert.Equal(-0.109m, reference.DistanceFromHigh);
        Assert.Equal(0m, reference.DistanceFromLow);
        // For x = 0..4, sum((x - 2) * y) / 10 = -0.0508 / 10.
        Assert.Equal(-0.00508m, reference.Slope);
        // Daily returns +10%, -10%, +10%, -10% have population standard deviation 10%.
        Assert.Equal(0.1m, reference.Volatility);
        Assert.Equal(new[] { 0m, 0.1m, -0.01m, 0.089m, -0.0199m }, reference.NormalizedCloses);
    }

    [Fact]
    public void LinearPathAndFlatPathUseSpecifiedScalesAndEqualWeights()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 100m, 105m, 110m, 115m, 120m);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        var scores = Assert.Single(run.Matches).Scores;
        Assert.Equal(0.05m, run.Reference!.Slope);
        var expectedPath = 100m / (1m + (decimal)Math.Sqrt(0.015d) / 0.05m);
        var returns = new[] { 0.05d, 110d / 105d - 1, 115d / 110d - 1, 120d / 115d - 1 };
        var mean = returns.Average();
        var expectedVolatility = (decimal)Math.Sqrt(returns.Average(value => (value - mean) * (value - mean)));
        AssertClose(expectedPath, scores.PricePath);
        Assert.Equal(100m, scores.HighPosition);
        Assert.Equal(20m, scores.LowPosition);
        Assert.Equal(20m, scores.Slope);
        AssertClose(100m / (1m + expectedVolatility / 0.02m), scores.Volatility);
        Assert.Equal((scores.PricePath + scores.HighPosition + scores.LowPosition + scores.Slope + scores.Volatility) / 5m, scores.Total);
        Assert.Equal(0m, Assert.Single(run.Matches).Features.Volatility);
        Assert.Equal(0m, Assert.Single(run.Matches).Features.Slope);
    }

    [Fact]
    public void ThresholdIncludesEqualityAndUsesTheUnroundedTotal()
    {
        var snapshot = Snapshot(100m, 100m, 100m, 100m, 100m, 100m, 101m, 102m, 103m, 104m);
        var definition = Definition(snapshot, 0, 4);
        var total = Assert.Single(_engine.Run(snapshot, definition).Matches).Scores.Total;
        Assert.NotEqual(decimal.Round(total, 2), total);
        var equal = _engine.Run(snapshot, definition with { MinimumSimilarity = total });
        Assert.Single(equal.Matches);
        Assert.Equal(0, equal.BelowThresholdCount);
        Assert.Equal(1, equal.ComparableCount);
        var below = _engine.Run(snapshot, definition with { MinimumSimilarity = total - 0.000000000001m });
        Assert.Single(below.Matches);
        Assert.Equal(0, below.BelowThresholdCount);
        Assert.Empty(_engine.Run(snapshot, definition with { MinimumSimilarity = total + 0.000000000001m }).Matches);
    }

    [Fact]
    public void ThresholdOfOneHundredIncludesIdenticalWindows()
    {
        var snapshot = FlatSnapshot(10);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4) with { MinimumSimilarity = 100 });
        Assert.Equal(100m, Assert.Single(run.Matches).Scores.Total);
        Assert.Equal(0, run.BelowThresholdCount);
        Assert.Equal(0, run.OverlapExcludedCount);
    }

    [Fact]
    public void ScanRangeIncludesOnlyWholeWindowsWithinBothBoundaries()
    {
        var snapshot = FlatSnapshot(20);
        var run = _engine.Run(snapshot, Definition(snapshot, 2, 10));
        Assert.Equal(5, run.CandidateCount); // Starts 2..6; ends 6..10.
        Assert.Equal(5, run.ComparableCount);
        Assert.Equal(4, run.OverlapExcludedCount);
        var match = Assert.Single(run.Matches);
        Assert.Equal(snapshot.Calendar.TradingDates[2], match.Features.Start);
        Assert.Equal(snapshot.Calendar.TradingDates[6], match.Features.End);
        Assert.Empty(_engine.Run(snapshot, Definition(snapshot, 2, 5)).Matches);
        Assert.Equal(0, _engine.Run(snapshot, Definition(snapshot, 2, 5)).CandidateCount);
    }

    [Fact]
    public void ReferenceAndWindowsSharingAnyReferenceDayAreNotCandidates()
    {
        var snapshot = FlatSnapshot(20);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 19));
        Assert.Equal(snapshot.Calendar.TradingDates[15], run.Reference!.Start);
        Assert.Equal(11, run.CandidateCount); // Starts 0..10; final historical endpoint is day 14.
        Assert.Equal(new[] { snapshot.Calendar.TradingDates[0], snapshot.Calendar.TradingDates[5], snapshot.Calendar.TradingDates[10] },
            run.Matches.Select(match => match.Features.Start));
        Assert.All(run.Matches, match => Assert.True(match.Features.End < run.Reference.Start));
        Assert.Equal(8, run.OverlapExcludedCount);
        Assert.Equal(run.ComparableCount, run.Matches.Count + run.BelowThresholdCount + run.OverlapExcludedCount);
    }

    [Fact]
    public void GreedyDeduplicationChoosesBestScoreBeforeEarliestStart()
    {
        var snapshot = Snapshot(100m, 100m, 101m, 102m, 103m, 104m, 106m, 108m, 110m, 100m, 101m, 102m, 103m, 104m);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 8));
        var match = Assert.Single(run.Matches);
        Assert.Equal(snapshot.Calendar.TradingDates[1], match.Features.Start);
        Assert.Equal(100m, match.Scores.Total);
        Assert.Equal(5, run.CandidateCount);
        Assert.Equal(4, run.OverlapExcludedCount);
        var strict = _engine.Run(snapshot, Definition(snapshot, 0, 8) with { MinimumSimilarity = 99.999m });
        Assert.Single(strict.Matches);
        Assert.Equal(4, strict.BelowThresholdCount);
        Assert.Equal(0, strict.OverlapExcludedCount);
    }

    [Fact]
    public void EqualScoresPreferEarlierStartsAndResultsAreRepeatableForUnorderedSnapshotCollections()
    {
        var snapshot = FlatSnapshot(30);
        var definition = Definition(snapshot, 0, 29);
        var first = _engine.Run(snapshot, definition);
        var reordered = snapshot with
        {
            Bars = snapshot.Bars.Reverse().ToArray(),
            Calendar = snapshot.Calendar with { TradingDates = snapshot.Calendar.TradingDates.Reverse().ToArray() }
        };
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(_engine.Run(snapshot, definition)));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(_engine.Run(reordered, definition)));
        Assert.Equal(new[] { 0, 5, 10, 15, 20 }.Select(index => snapshot.Calendar.TradingDates[index]), first.Matches.Select(match => match.Features.Start));
    }

    [Theory]
    [InlineData("missing", ExclusionReason.MissingBar)]
    [InlineData("suspended", ExclusionReason.NonTradingBar)]
    [InlineData("ohlc", ExclusionReason.MissingPrice)]
    [InlineData("invalid", ExclusionReason.InvalidBar)]
    public void HistoricalQualityProblemsExcludeTheWindowWithoutFillingValues(string mutation, ExclusionReason expected)
    {
        var snapshot = Mutate(FlatSnapshot(10), mutation, 2);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.NotNull(run.Reference);
        Assert.Equal(1, run.CandidateCount);
        Assert.Equal(0, run.ComparableCount);
        Assert.Empty(run.Matches);
        Assert.Equal(new ExclusionCount { Reason = expected, Count = 1 }, Assert.Single(run.Exclusions));
    }

    [Fact]
    public void SimultaneousQualityProblemsHaveOneStablePrimaryCountPerCandidate()
    {
        var snapshot = Mutate(Mutate(Mutate(FlatSnapshot(10), "suspended", 1), "missing", 2), "action", 3);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.Equal(1, run.CandidateCount - run.ComparableCount);
        Assert.Equal(new ExclusionCount { Reason = ExclusionReason.MissingBar, Count = 1 }, Assert.Single(run.Exclusions));
    }

    [Fact]
    public void MissingVolumeAndActionsWithoutPriceEffectDoNotExcludeAWindow()
    {
        var snapshot = Replace(FlatSnapshot(10), 2, bar => bar with { Volume = null });
        snapshot = snapshot with { CorporateActions = [new() { EffectiveDate = snapshot.Calendar.TradingDates[2], Kind = "NonPriceAction", SourceId = Source, AffectsPriceComparison = false }] };
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.Single(run.Matches);
        Assert.Empty(run.Exclusions);
        Assert.Equal("CorporateActionAnnotation", Assert.Single(run.Matches[0].Features.Warnings).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void CorporateActionsAtEitherWindowEndpointAreWarningsAndKeepRawPrices(int actionIndex)
    {
        var snapshot = Mutate(FlatSnapshot(10), "action", actionIndex);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        var match = Assert.Single(run.Matches);
        Assert.Empty(run.Exclusions);
        Assert.Equal(1, run.WarningCandidateCount);
        var warning = Assert.Single(match.Features.Warnings);
        Assert.Equal("CorporateActionInWindow", warning.Code);
        Assert.Equal(snapshot.Calendar.TradingDates[actionIndex], warning.Date);
        Assert.Equal("PriceAction · " + Source, warning.Message);
        Assert.False(warning.BlocksResearch);
        Assert.Equal(100m, match.Features.BaseClose);
        Assert.Equal(100m, match.Features.EndClose);
    }

    [Fact]
    public void RawPriceDiscontinuitiesInReferenceAndCandidateCompleteWithoutAdjustingOrInventingEvents()
    {
        var snapshot = Snapshot(50m, 50m, 25m, 25m, 25m, 100m, 100m, 50m, 50m, 50m);
        snapshot = snapshot with
        {
            ActionCoverage = snapshot.ActionCoverage with { IsVerified = false },
            CorporateActions = [
                new() { EffectiveDate = snapshot.Calendar.TradingDates[2], Kind = "ConfirmedSplit", SourceId = Source },
                new() { EffectiveDate = snapshot.Calendar.TradingDates[7], Kind = "NonComparablePrice", SourceId = "TWSE/STOCK_DAY" }
            ]
        };
        var original = JsonSerializer.Serialize(snapshot);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.Equal(100m, Assert.Single(run.Matches).Scores.Total);
        Assert.Equal(new[] { 0m, 0m, -0.5m, -0.5m, -0.5m }, run.Reference!.NormalizedCloses);
        Assert.Equal(50m, run.Reference.EndClose);
        Assert.Equal(25m, run.Matches[0].Features.EndClose);
        Assert.Contains(run.Reference.Warnings, issue => issue.Code == "CorporateActionInWindow" && issue.Date == snapshot.Calendar.TradingDates[7]);
        Assert.Contains(run.Reference.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
        Assert.Contains(run.Matches[0].Features.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
        Assert.Equal(2, run.Reference.Warnings.Count);
        Assert.Empty(run.Exclusions);
        Assert.Empty(run.BlockingReasons);
        Assert.Equal(original, JsonSerializer.Serialize(snapshot));
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("coverage")]
    public void HistoricalActionCoverageGapsWarnWithoutExcludingValidPriceWindows(string mutation)
    {
        var snapshot = Mutate(FlatSnapshot(10), mutation, 2);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.Equal(1, run.ComparableCount);
        Assert.Equal(1, run.WarningCandidateCount);
        Assert.Equal("CorporateActionCoverageUnknown", Assert.Single(Assert.Single(run.Matches).Features.Warnings).Code);
        Assert.Empty(run.Exclusions);
    }

    [Theory]
    [InlineData("unverified")]
    [InlineData("version")]
    [InlineData("source")]
    public void UnknownActionCoverageWithoutComparisonEvidenceStillCompletesWithExplicitWarnings(string mutation)
    {
        var snapshot = FlatSnapshot(10);
        snapshot = snapshot with { ActionCoverage = mutation switch
        {
            "unverified" => snapshot.ActionCoverage with { IsVerified = false },
            "version" => snapshot.ActionCoverage with { Version = "" },
            "source" => snapshot.ActionCoverage with { SourceId = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        } };
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.Contains(run.Diagnostics, issue => issue.Code == "CorporateActionCoverageUnknown" && !issue.BlocksResearch);
        Assert.Equal("CorporateActionCoverageUnknown", Assert.Single(run.Reference!.Warnings).Code);
        Assert.Equal("CorporateActionCoverageUnknown", Assert.Single(Assert.Single(run.Matches).Features.Warnings).Code);
    }

    [Fact]
    public void VerifiedRawComparisonCoverageIsUsedWhenActionInventoryIsIncomplete()
    {
        var snapshot = OfficialComparisonSnapshot();
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.Single(run.Matches);
        Assert.Contains(run.Diagnostics, issue => issue.Code == "CorporateActionCoverageUnknown" && !issue.BlocksResearch);
        Assert.Contains(run.Reference!.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
        Assert.Contains(run.Matches[0].Features.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
        snapshot = snapshot with { ComparabilityCoverage = snapshot.ComparabilityCoverage! with { Gaps = [new() { Start = snapshot.Calendar.TradingDates[2], End = snapshot.Calendar.TradingDates[2] }] } };
        run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.Empty(run.Exclusions);
        Assert.Contains(Assert.Single(run.Matches).Features.Warnings, issue => issue.Code == "PriceComparisonCoverageUnknown");
    }

    [Fact]
    public void ActionAndPriceComparisonCoverageWarningsRemainDistinct()
    {
        var snapshot = OfficialComparisonSnapshot();
        snapshot = snapshot with { ActionCoverage = snapshot.ActionCoverage with { IsVerified = true, Gaps = [new() { Start = snapshot.Calendar.TradingDates[2], End = snapshot.Calendar.TradingDates[2] }] } };
        var match = Assert.Single(_engine.Run(snapshot, Definition(snapshot, 0, 4)).Matches);
        Assert.Contains(match.Features.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
        Assert.DoesNotContain(match.Features.Warnings, issue => issue.Code == "PriceComparisonCoverageUnknown");
        snapshot = snapshot with
        {
            ActionCoverage = snapshot.ActionCoverage with { Gaps = [] },
            ComparabilityCoverage = snapshot.ComparabilityCoverage! with { Gaps = [new() { Start = snapshot.Calendar.TradingDates[2], End = snapshot.Calendar.TradingDates[2] }] }
        };
        match = Assert.Single(_engine.Run(snapshot, Definition(snapshot, 0, 4)).Matches);
        Assert.DoesNotContain(match.Features.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
        Assert.Contains(match.Features.Warnings, issue => issue.Code == "PriceComparisonCoverageUnknown");
    }

    [Theory]
    [InlineData("missing", "ReferenceMissingBar")]
    [InlineData("suspended", "ReferenceNonTradingBar")]
    [InlineData("ohlc", "ReferenceMissingPrice")]
    [InlineData("invalid", "ReferenceInvalidBar")]
    public void IncompleteReferenceBlocksInsteadOfFallingBackToOlderPrices(string mutation, string code)
    {
        var snapshot = Mutate(FlatSnapshot(10), mutation, 9);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 9));
        Assert.False(run.IsAllowed);
        Assert.Null(run.Reference);
        Assert.Empty(run.Matches);
        Assert.Equal(0, run.CandidateCount);
        Assert.Contains(run.Diagnostics, issue => issue.Code == code && issue.BlocksResearch);
        Assert.NotEmpty(run.BlockingReasons);
    }

    [Fact]
    public void TooFewCalendarDaysBeforeCutoffBlockReference()
    {
        var snapshot = FlatSnapshot(4);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 3));
        Assert.False(run.IsAllowed);
        Assert.Contains(run.Diagnostics, issue => issue.Code == "ReferenceInsufficientHistory");
        snapshot = FlatSnapshot(10);
        run = _engine.Run(snapshot, Definition(snapshot, 0, 3) with { DataAsOf = snapshot.Calendar.TradingDates[3] });
        Assert.False(run.IsAllowed);
        Assert.Null(run.Reference);
    }

    [Fact]
    public void ReferenceUsesLatestCalendarDateAtOrBeforeCutoffAndExactlyLookbackDays()
    {
        var snapshot = Snapshot(100m, 101m, 102m, 103m, 104m, 105m, 106m, 107m, 108m, 109m);
        var cutoff = snapshot.Calendar.TradingDates[8].AddDays(1); // Saturday following the Friday at index 8.
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 8) with { DataAsOf = cutoff });
        Assert.True(run.IsAllowed);
        Assert.Equal(snapshot.Calendar.TradingDates[4], run.Reference!.Start);
        Assert.Equal(snapshot.Calendar.TradingDates[8], run.Reference.End);
        Assert.Equal(108m, run.Reference.EndClose);
        Assert.Equal(5, run.Reference.NormalizedCloses.Count);
    }

    [Theory]
    [InlineData("source", "BarSourceMismatch")]
    [InlineData("duplicate-bar", "DuplicateBar")]
    [InlineData("duplicate-date", "DuplicateTradingDate")]
    [InlineData("calendar", "CalendarUnverified")]
    [InlineData("hash", "SnapshotHashMismatch")]
    [InlineData("comparison", "PriceComparisonCoverageInvalid")]
    [InlineData("action-coverage-invalid", "CorporateActionCoverageInvalid")]
    public void SourceStructureAndHashFailuresBlockBeforeAnySimilarityComputation(string mutation, string code)
    {
        var snapshot = FlatSnapshot(10);
        snapshot = mutation switch
        {
            "source" => Replace(snapshot, 0, bar => bar with { SourceId = "other" }),
            "duplicate-bar" => snapshot with { Bars = snapshot.Bars.Append(snapshot.Bars[0]).ToArray() },
            "duplicate-date" => snapshot with { Calendar = snapshot.Calendar with { TradingDates = snapshot.Calendar.TradingDates.Append(snapshot.Calendar.TradingDates[0]).ToArray() } },
            "calendar" => snapshot with { Calendar = snapshot.Calendar with { IsVerified = false } },
            "hash" => snapshot with { ContentHash = "not-the-content-hash" },
            "comparison" => OfficialComparisonSnapshot() with { ComparabilityCoverage = new() { IsVerified = true, SourceId = "unknown" } },
            "action-coverage-invalid" => snapshot with { ActionCoverage = snapshot.ActionCoverage with { Gaps = [new() { Start = snapshot.DataAsOf.AddDays(1), End = snapshot.DataAsOf.AddDays(1) }] } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.False(run.IsAllowed);
        Assert.Null(run.Reference);
        Assert.Equal(0, run.CandidateCount);
        Assert.Contains(run.Diagnostics, issue => issue.Code == code && issue.BlocksResearch);
    }

    [Fact]
    public void VerifiedHashIsAcceptedAndChangesAreDetected()
    {
        var snapshot = FlatSnapshot(10);
        snapshot = snapshot with { ContentHash = SnapshotFingerprint.Compute(snapshot) };
        Assert.True(_engine.Run(snapshot, Definition(snapshot, 0, 4)).IsAllowed);
        var changed = Replace(snapshot, 0, bar => bar with { Close = 99, Low = 99 });
        Assert.False(_engine.Run(changed, Definition(changed, 0, 4)).IsAllowed);
    }

    [Fact]
    public void ScanOutsideCalendarOrCutoffBeyondSnapshotBlocks()
    {
        var snapshot = FlatSnapshot(10);
        var definition = Definition(snapshot, 0, 4);
        var outside = _engine.Run(snapshot, definition with { ScanStart = snapshot.Calendar.CoverageStart.AddDays(-1) });
        Assert.Contains(outside.Diagnostics, issue => issue.Code == "ScanRangeOutsideCalendar" && issue.BlocksResearch);
        var future = _engine.Run(snapshot, definition with { DataAsOf = snapshot.DataAsOf.AddDays(1) });
        Assert.Contains(future.Diagnostics, issue => issue.Code == "AsOfBeyondSnapshot" && issue.BlocksResearch);
        Assert.False(outside.IsAllowed);
        Assert.False(future.IsAllowed);
    }

    [Theory]
    [InlineData("lookback-low")]
    [InlineData("lookback-high")]
    [InlineData("scan-default")]
    [InlineData("scan-reversed")]
    [InlineData("scan-after-cutoff")]
    [InlineData("threshold-low")]
    [InlineData("threshold-high")]
    public void DefinitionRejectsInvalidWindowsDatesAndThresholds(string mutation)
    {
        var snapshot = FlatSnapshot(10);
        var definition = Definition(snapshot, 0, 4);
        definition = mutation switch
        {
            "lookback-low" => definition with { Lookback = 4 },
            "lookback-high" => definition with { Lookback = 251 },
            "scan-default" => definition with { ScanStart = default },
            "scan-reversed" => definition with { ScanEnd = definition.ScanStart.AddDays(-1) },
            "scan-after-cutoff" => definition with { ScanEnd = definition.DataAsOf.AddDays(1) },
            "threshold-low" => definition with { MinimumSimilarity = -0.0001m },
            "threshold-high" => definition with { MinimumSimilarity = 100.0001m },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.Throws<ArgumentException>(() => SimilarityEngine.ValidateDefinition(definition));
    }

    [Fact]
    public void NullDefinitionsAndNecessarySnapshotCollectionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SimilarityEngine.ValidateDefinition(null!));
        var snapshot = FlatSnapshot(10);
        Assert.Throws<ArgumentNullException>(() => _engine.Run(null!, Definition(snapshot, 0, 4)));
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot with { Bars = null! }, Definition(snapshot, 0, 4)));
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot with { Calendar = null! }, Definition(snapshot, 0, 4)));
    }

    [Fact]
    public void ExtremeRepresentablePricesDoNotOverflowIntermediateVarianceOrDistance()
    {
        var snapshot = Snapshot(1m, 10000000000000000000000000m, 10000000000000000000000000m, 10000000000000000000000000m, 10000000000000000000000000m,
            100m, 100m, 100m, 100m, 100m);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        var match = Assert.Single(run.Matches);
        Assert.True(match.Features.Volatility > 0m);
        Assert.InRange(match.Scores.Total, 0m, 100m);
        Assert.InRange(match.Scores.PricePath, 0m, 100m);
        Assert.InRange(match.Scores.LowPosition, 0m, 100m);
        Assert.InRange(match.Scores.Slope, 0m, 100m);
        Assert.InRange(match.Scores.Volatility, 0m, 100m);
    }

    [Fact]
    public void UnrepresentableFeaturesExcludeHistoricalWindowAndBlockReference()
    {
        var snapshot = Snapshot(0.0000000000000000000000000001m, decimal.MaxValue, decimal.MaxValue, decimal.MaxValue, decimal.MaxValue,
            100m, 100m, 100m, 100m, 100m);
        var run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.True(run.IsAllowed);
        Assert.Empty(run.Matches);
        Assert.Equal(ExclusionReason.UndefinedFeature, Assert.Single(run.Exclusions).Reason);
        snapshot = snapshot with { Bars = snapshot.Bars.Select((bar, index) => bar with
        {
            Open = index == 5 ? 0.0000000000000000000000000001m : decimal.MaxValue,
            High = index == 5 ? 0.0000000000000000000000000001m : decimal.MaxValue,
            Low = index == 5 ? 0.0000000000000000000000000001m : decimal.MaxValue,
            Close = index == 5 ? 0.0000000000000000000000000001m : decimal.MaxValue
        }).ToArray() };
        run = _engine.Run(snapshot, Definition(snapshot, 0, 4));
        Assert.False(run.IsAllowed);
        Assert.Contains(run.Diagnostics, issue => issue.Code == "ReferenceUndefinedFeature" && issue.BlocksResearch);
    }

    [Fact]
    public void PreCancelledRunThrowsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var snapshot = FlatSnapshot(10);
        Assert.Throws<OperationCanceledException>(() => _engine.Run(snapshot, Definition(snapshot, 0, 4), cancellation.Token));
    }

    [Fact]
    public void CancellationRequestedAfterRunStartsIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        var snapshot = FlatSnapshot(10);
        var definition = Definition(snapshot, 0, 4);
        snapshot = snapshot with { Bars = new CancelOnEnumeration<DailyBar>(snapshot.Bars, cancellation) };
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() => _engine.Run(snapshot, definition, cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
    }

    private static void AssertClose(decimal expected, decimal actual) => Assert.InRange(Math.Abs(expected - actual), 0m, 0.000000000001m);

    private static SimilarityDefinition Definition(DataSnapshot snapshot, int start, int end) => new()
    {
        Lookback = 5, ScanStart = snapshot.Calendar.TradingDates[start], ScanEnd = snapshot.Calendar.TradingDates[end],
        DataAsOf = snapshot.DataAsOf, MinimumSimilarity = 0
    };

    private static DataSnapshot Replace(DataSnapshot snapshot, int index, Func<DailyBar, DailyBar> replace) =>
        snapshot with { Bars = snapshot.Bars.Select((bar, i) => i == index ? replace(bar) : bar).ToArray() };

    private static DataSnapshot Mutate(DataSnapshot snapshot, string mutation, int index)
    {
        var date = snapshot.Calendar.TradingDates[index];
        return mutation switch
        {
            "missing" => snapshot with { Bars = snapshot.Bars.Where(bar => bar.Date != date).ToArray() },
            "suspended" => Replace(snapshot, index, bar => bar with { Status = TradingStatus.Suspended }),
            "ohlc" => Replace(snapshot, index, bar => bar with { Open = null }),
            "invalid" => Replace(snapshot, index, bar => bar with { High = 99 }),
            "action" => snapshot with { CorporateActions = [new() { EffectiveDate = date, Kind = "PriceAction", SourceId = Source }] },
            "gap" => snapshot with { ActionCoverage = snapshot.ActionCoverage with { Gaps = [new() { Start = date, End = date }] } },
            "coverage" => snapshot with { ActionCoverage = snapshot.ActionCoverage with { CoverageStart = snapshot.Calendar.TradingDates[index + 1] } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }

    private static DataSnapshot OfficialComparisonSnapshot()
    {
        var snapshot = FlatSnapshot(10);
        return snapshot with
        {
            IsSynthetic = false, SourceId = "TWSE",
            Instrument = snapshot.Instrument with { InstrumentId = "TWSE:TEST", Market = "TWSE", SecurityType = SecurityType.CommonStock },
            Calendar = snapshot.Calendar with { Market = "TWSE" },
            ActionCoverage = snapshot.ActionCoverage with { IsVerified = false, SourceId = "TWSE" },
            ComparabilityCoverage = new()
            {
                IsVerified = true, SourceId = "TWSE/STOCK_DAY", Version = "fixture-v1",
                CoverageStart = snapshot.Calendar.CoverageStart, CoverageEnd = snapshot.Calendar.CoverageEnd
            },
            Bars = snapshot.Bars.Select(bar => bar with { SourceId = "TWSE" }).ToArray()
        };
    }

    private static DataSnapshot FlatSnapshot(int count) => Snapshot(Enumerable.Repeat(100m, count).ToArray());

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
            Instrument = new() { InstrumentId = "synthetic:similarity:test", Code = "TEST", Name = "Synthetic test fixture", Market = "SYNTHETIC", SecurityType = SecurityType.Synthetic },
            SourceId = Source, DataVersion = "similarity-test-v1", IsSynthetic = true, DataAsOf = dates[^1],
            Calendar = new() { Market = "SYNTHETIC", Version = "fixture-calendar-v1", IsVerified = true, TradingDates = dates, CoverageStart = dates[0], CoverageEnd = dates[^1] },
            ActionCoverage = new() { IsVerified = true, SourceId = Source, Version = "fixture-actions-v1", CoverageStart = dates[0], CoverageEnd = dates[^1] },
            Bars = closes.Select((close, index) => new DailyBar { Date = dates[index], Open = close, High = close, Low = close, Close = close, Volume = 100, SourceId = Source }).ToArray()
        };
    }

    private sealed class CancelOnEnumeration<T>(IReadOnlyList<T> items, CancellationTokenSource cancellation) : IReadOnlyList<T>
    {
        public int Count => items.Count;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator()
        {
            cancellation.Cancel();
            return items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
