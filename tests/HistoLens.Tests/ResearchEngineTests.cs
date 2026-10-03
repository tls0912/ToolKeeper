using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HistoLens.Core;
using Xunit;
using Xunit.Abstractions;

namespace HistoLens.Tests;

public sealed class ResearchEngineTests(ITestOutputHelper output)
{
    private const string Source = "Tests.Synthetic";
    private readonly ResearchEngine _engine = new();

    [Fact]
    public void SpecCaseA_UsesFutureOhlcAndClosePathForDistinctMetrics()
    {
        var snapshot = Snapshot(101, 100, 110, 99, 105);
        snapshot = Replace(snapshot, 2, b => b with { Open = 102, High = 111, Low = 100 });
        snapshot = Replace(snapshot, 3, b => b with { Open = 101, High = 102, Low = 98 });
        snapshot = Replace(snapshot, 4, b => b with { Open = 102, High = 108, Low = 101 });
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1) with { Horizons = [3] });
        var actual = Assert.Single(Assert.Single(run.Cases).Outcomes);
        Assert.True(actual.IsValid);
        Assert.Equal(0.05m, actual.PriceChange);
        Assert.Equal(0.11m, actual.HighestPriceChange);
        Assert.Equal(-0.02m, actual.LowestPriceChange);
        Assert.Equal(-0.10m, actual.CloseMaxDrawdown);
        Assert.Equal(1, actual.UpperFirstHitTradingDay);
        Assert.Null(actual.LowerFirstHitTradingDay);
        Assert.Equal(ThresholdOrder.UpperFirst, actual.ThresholdOrder);
    }

    [Fact]
    public void SpecCaseB_UsesAllFourSamplesAndLinearDecimalPercentiles()
    {
        var snapshot = Snapshot(100, 100, 96, 100, 100, 100, 102, 100, 106);
        snapshot = snapshot with { Bars = snapshot.Bars.Select((b, i) => b with { Volume = i % 2 == 0 ? 10 : 100 }).ToArray() };
        var definition = Definition(snapshot, 1, 7) with
        {
            Conditions = [Condition(ResearchFeature.RelativeVolume, 1, ComparisonOperator.GreaterThanOrEqual, 2)]
        };
        var stats = Assert.Single(_engine.Run(snapshot, definition).Statistics);
        Assert.Equal(4, stats.ValidCount);
        Assert.Equal(0.01m, stats.MeanPriceChange);
        Assert.Equal(0.01m, stats.MedianPriceChange);
        Assert.Equal(2, stats.UpCount);
        Assert.Equal(1, stats.FlatCount);
        Assert.Equal(1, stats.DownCount);
        Assert.Equal(0.5m, stats.UpRate);
        Assert.Equal(0.25m, stats.FlatRate);
        Assert.Equal(0.25m, stats.DownRate);
        Assert.Equal(0.06m, stats.BestPriceChange);
        Assert.Equal(-0.04m, stats.WorstPriceChange);
        Assert.Equal(-0.028m, stats.P10);
        Assert.Equal(0.048m, stats.P90);
        Assert.Equal(-0.01m, stats.P25);
        Assert.Equal(0.03m, stats.P75);
    }

    [Fact]
    public void SpecCaseC_BothHitsOnSameFirstDayRemainAmbiguous()
    {
        var snapshot = Replace(Snapshot(101, 100, 101), 2, b => b with { High = 106, Low = 96 });
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        var actual = Assert.Single(Assert.Single(run.Cases).Outcomes);
        Assert.Equal(1, actual.UpperFirstHitTradingDay);
        Assert.Equal(1, actual.LowerFirstHitTradingDay);
        Assert.Equal(ThresholdOrder.SameDayUnknown, actual.ThresholdOrder);
        var stats = Assert.Single(run.Statistics);
        Assert.Equal(1m, stats.UpperHitRate);
        Assert.Equal(1m, stats.LowerHitRate);
        Assert.Equal(1, stats.SameDayUnknownCount);
    }

    [Fact]
    public void FirstInRun_KeepsOneOfFiveConsecutiveMatches()
    {
        var snapshot = Snapshot(100, 100, 99, 98, 97, 96, 95, 95);
        var run = _engine.Run(snapshot, Definition(snapshot, 2, 6) with { SamplingPolicy = SamplingPolicy.FirstInRun });
        Assert.Equal(5, run.Funnel.CandidateDates);
        Assert.Equal(5, run.Funnel.DeterminableDates);
        Assert.Equal(5, run.Funnel.RawMatchDays);
        Assert.Equal(1, run.Funnel.SampledEvents);
        Assert.Equal(snapshot.Bars[2].Date, Assert.Single(run.Cases).EventDate);
        Assert.Equal(4, run.Evaluations.Count(e => e.SamplingExclusion == ExclusionReason.ConsecutiveMatch));
    }

    [Fact]
    public void EventRangeStartingInsideAnExistingRunDoesNotInventNewFirstDay()
    {
        var snapshot = Snapshot(100, 100, 99, 98, 97, 96, 95, 95);
        var run = _engine.Run(snapshot, Definition(snapshot, 4, 6) with { SamplingPolicy = SamplingPolicy.FirstInRun });
        Assert.Equal(3, run.Funnel.RawMatchDays);
        Assert.Empty(run.Cases);
        Assert.All(run.Evaluations, e => Assert.Equal(ExclusionReason.ConsecutiveMatch, e.SamplingExclusion));
    }

    [Fact]
    public void UnknownPreviousDayDoesNotCountAsFalseForFirstInRun()
    {
        var snapshot = Snapshot(100, 100, 99, 98, 97);
        snapshot = snapshot with { Bars = snapshot.Bars.Skip(1).ToArray() };
        var run = _engine.Run(snapshot, Definition(snapshot, 2, 3) with { SamplingPolicy = SamplingPolicy.FirstInRun });
        Assert.Equal(2, run.Funnel.RawMatchDays);
        Assert.Empty(run.Cases);
        Assert.Equal(ExclusionReason.EventStartUnknown, run.Evaluations[0].SamplingExclusion);
        Assert.Equal(ExclusionReason.ConsecutiveMatch, run.Evaluations[1].SamplingExclusion);
    }

    [Fact]
    public void DeterminableFalseDayAllowsANewRun()
    {
        var snapshot = Snapshot(100, 100, 99, 98, 98, 97, 96);
        var run = _engine.Run(snapshot, Definition(snapshot, 2, 5) with { SamplingPolicy = SamplingPolicy.FirstInRun });
        Assert.Equal(new[] { snapshot.Bars[2].Date, snapshot.Bars[5].Date }, run.Cases.Select(c => c.EventDate));
    }

    [Fact]
    public void EveryMatchRetainsAllMatchingDays()
    {
        var snapshot = Snapshot(100, 99, 98, 97, 96, 95);
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 4));
        Assert.Equal(4, run.Cases.Count);
        Assert.Equal(4, Assert.Single(run.Statistics).ValidCount);
    }

    [Fact]
    public void NonOverlapUsesLongestHorizonBeforeCheckingFutureQuality()
    {
        var snapshot = Snapshot(100, 99, 98, 97, 96, 95, 94, 93, 92, 91);
        // First event's future window is invalid, but it still controls chronological sampling.
        snapshot = Replace(snapshot, 3, b => b with { Status = TradingStatus.Suspended });
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 8) with
        {
            SamplingPolicy = SamplingPolicy.NonOverlapping, Horizons = [1, 3]
        });
        Assert.Equal(new[] { snapshot.Bars[1].Date, snapshot.Bars[5].Date }, run.Cases.Select(c => c.EventDate));
        Assert.Equal(ExclusionReason.NonTradingBar, run.Cases[0].Outcomes.Single(o => o.Horizon == 3).PrimaryExclusion);
        Assert.Equal(ExclusionReason.OverlappingWindow, run.Evaluations.Single(e => e.Date == snapshot.Bars[2].Date).SamplingExclusion);
        Assert.Equal(ExclusionReason.OverlappingWindow, run.Evaluations.Single(e => e.Date == snapshot.Bars[8].Date).SamplingExclusion);
    }

    [Fact]
    public void EachHorizonHasItsOwnCompletenessAndDenominator()
    {
        var snapshot = Snapshot(101, 100, 105, 103);
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1) with { Horizons = [1, 5] });
        Assert.Equal(1, run.Statistics.Single(s => s.Horizon == 1).ValidCount);
        var longStats = run.Statistics.Single(s => s.Horizon == 5);
        Assert.Equal(0, longStats.ValidCount);
        Assert.Equal(1, longStats.ExcludedCount);
        Assert.Null(longStats.MeanPriceChange);
        Assert.Null(longStats.UpRate);
        Assert.Equal(ExclusionReason.InsufficientFutureData, Assert.Single(longStats.Exclusions).Reason);
    }

    [Fact]
    public void MissingMiddleDayCannotBeReplacedByALaterQuote()
    {
        var snapshot = Snapshot(101, 100, 101, 102, 103, 150);
        var missingDate = snapshot.Bars[3].Date;
        snapshot = snapshot with { Bars = snapshot.Bars.Where(b => b.Date != missingDate).ToArray() };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1) with { Horizons = [3] });
        var outcome = Assert.Single(Assert.Single(run.Cases).Outcomes);
        Assert.Equal(snapshot.Calendar.TradingDates[4], outcome.EndDate);
        Assert.Equal(ExclusionReason.MissingBar, outcome.PrimaryExclusion);
        Assert.Null(outcome.PriceChange);
        Assert.Contains(run.Diagnostics, d => d.Code == "MissingBar" && d.Date == missingDate);
    }

    [Fact]
    public void ExplicitCalendarSkipsMarketHolidaysButNotStockSuspensions()
    {
        var snapshot = Snapshot(101, 100, 102, 104);
        var dates = new[] { new DateOnly(2024, 2, 7), new DateOnly(2024, 2, 15), new DateOnly(2024, 2, 16), new DateOnly(2024, 2, 19) };
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.Select((b, i) => b with { Date = dates[i] }).ToArray(), DataAsOf = dates[^1],
            Calendar = snapshot.Calendar with { TradingDates = dates, CoverageStart = dates[0], CoverageEnd = dates[^1] },
            ActionCoverage = snapshot.ActionCoverage with { CoverageStart = dates[0], CoverageEnd = dates[^1] }
        };
        var definition = Definition(snapshot, 1, 1) with { Horizons = [2] };
        var actual = Assert.Single(Assert.Single(_engine.Run(snapshot, definition).Cases).Outcomes);
        Assert.Equal(dates[^1], actual.EndDate);
        Assert.Equal(0.04m, actual.PriceChange);
        snapshot = Replace(snapshot, 2, b => b with { Status = TradingStatus.Suspended });
        actual = Assert.Single(Assert.Single(_engine.Run(snapshot, definition).Cases).Outcomes);
        Assert.Equal(dates[^1], actual.EndDate);
        Assert.Equal(ExclusionReason.NonTradingBar, actual.PrimaryExclusion);
    }

    [Fact]
    public void UnverifiedCalendarBlocksAllStatistics()
    {
        var snapshot = Snapshot(101, 100, 105);
        snapshot = snapshot with { Calendar = snapshot.Calendar with { IsVerified = false } };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        Assert.False(run.IsResearchAllowed);
        Assert.Empty(run.Statistics);
        Assert.Contains(run.Diagnostics, d => d.Code == "CalendarUnverified" && d.BlocksResearch);
    }

    [Fact]
    public void UnverifiedCorporateActionCoverageIsNotTreatedAsNoActions()
    {
        var snapshot = Snapshot(101, 100, 105);
        snapshot = snapshot with { ActionCoverage = snapshot.ActionCoverage with { IsVerified = false } };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        Assert.False(run.IsResearchAllowed);
        Assert.Empty(run.Cases);
        Assert.Empty(run.Statistics);
        Assert.Contains(run.Diagnostics, d => d.Code == "CorporateActionCoverageUnknown" && d.BlocksResearch);
    }

    [Fact]
    public void CorporateActionInOnlyTheLongerWindowPreservesShorterOutcome()
    {
        var snapshot = Snapshot(101, 100, 105, 104, 52);
        snapshot = snapshot with { CorporateActions = [new CorporateAction { EffectiveDate = snapshot.Bars[4].Date, Kind = "SyntheticSplit", SourceId = Source }] };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1) with { Horizons = [1, 3] });
        Assert.Equal(1, run.Statistics.Single(s => s.Horizon == 1).ValidCount);
        var longOutcome = Assert.Single(run.Cases).Outcomes.Single(o => o.Horizon == 3);
        Assert.Equal(ExclusionReason.CorporateActionInWindow, longOutcome.PrimaryExclusion);
        Assert.Null(longOutcome.PriceChange);
    }

    [Fact]
    public void CorporateActionInConditionLookbackMakesEventUnknown()
    {
        var snapshot = Snapshot(102, 101, 100, 105);
        snapshot = snapshot with { CorporateActions = [new CorporateAction { EffectiveDate = snapshot.Bars[1].Date, Kind = "SyntheticDistribution", SourceId = Source }] };
        var run = _engine.Run(snapshot, Definition(snapshot, 2, 2));
        Assert.Empty(run.Cases);
        Assert.Equal(MatchState.Unknown, Assert.Single(run.Evaluations).State);
        Assert.Contains(ExclusionReason.CorporateActionInWindow, run.Evaluations[0].Reasons);
    }

    [Fact]
    public void CorporateActionCoverageGapExcludesOnlyIntersectingHorizon()
    {
        var snapshot = Snapshot(101, 100, 105, 104, 106);
        snapshot = snapshot with
        {
            ActionCoverage = snapshot.ActionCoverage with
            {
                Gaps = [new DateRange { Start = snapshot.Bars[3].Date, End = snapshot.Bars[3].Date }]
            }
        };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1) with { Horizons = [1, 3] });
        Assert.True(run.IsResearchAllowed);
        Assert.Equal(1, run.Statistics.Single(s => s.Horizon == 1).ValidCount);
        Assert.Equal(ExclusionReason.CorporateActionCoverageUnknown, Assert.Single(run.Cases).Outcomes.Single(o => o.Horizon == 3).PrimaryExclusion);
    }

    [Fact]
    public void LimitedCorporateActionCoverageCannotValidateEarlierConditionWindow()
    {
        var snapshot = Snapshot(101, 100, 105);
        snapshot = snapshot with { ActionCoverage = snapshot.ActionCoverage with { CoverageStart = snapshot.Bars[1].Date } };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        Assert.Equal(MatchState.Unknown, Assert.Single(run.Evaluations).State);
        Assert.Contains(ExclusionReason.CorporateActionCoverageUnknown, run.Evaluations[0].Reasons);
        Assert.Empty(run.Cases);
    }

    [Fact]
    public void MultipleExclusionFlagsContributeOnlyOnePrimaryCount()
    {
        var snapshot = Snapshot(101, 100, 105);
        snapshot = Replace(snapshot, 2, b => b with { Status = TradingStatus.Suspended, Close = null });
        snapshot = snapshot with { CorporateActions = [new CorporateAction { EffectiveDate = snapshot.Bars[2].Date, Kind = "SyntheticAction", SourceId = Source }] };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        var actual = Assert.Single(Assert.Single(run.Cases).Outcomes);
        Assert.Equal(ExclusionReason.CorporateActionInWindow, actual.PrimaryExclusion);
        Assert.Contains(ExclusionReason.NonTradingBar, actual.Exclusions);
        Assert.Contains(ExclusionReason.MissingPrice, actual.Exclusions);
        Assert.Equal(new[] { ExclusionReason.CorporateActionInWindow, ExclusionReason.NonTradingBar, ExclusionReason.MissingPrice }, actual.Exclusions);
        Assert.Equal(1, Assert.Single(run.Statistics).Exclusions.Sum(e => e.Count));
        Assert.Equal(run.Funnel.SampledEvents, run.Statistics[0].ValidCount + run.Statistics[0].ExcludedCount);
    }

    [Fact]
    public void MissingOrInvalidPricesRemainUnknownInsteadOfTurningIntoZero()
    {
        var original = Snapshot(101, 100, 105);
        var missing = Replace(original, 1, b => b with { Close = null });
        var run = _engine.Run(missing, Definition(missing, 1, 1));
        Assert.Equal(MatchState.Unknown, Assert.Single(run.Evaluations).State);
        Assert.Contains(run.Diagnostics, d => d.Date == missing.Bars[1].Date && d.Code == "MissingPrice");
        Assert.Null(missing.Bars[1].Close);
        var invalid = Replace(original, 1, b => b with { Open = 200 });
        run = _engine.Run(invalid, Definition(invalid, 1, 1));
        Assert.Equal(MatchState.Unknown, Assert.Single(run.Evaluations).State);
        Assert.Contains(run.Diagnostics, d => d.Date == invalid.Bars[1].Date && d.Code == "InvalidBar");
        Assert.Equal(200m, invalid.Bars[1].Open);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(100, -1)]
    public void NegativeVolumeOrTurnoverFailsBarValidation(int volume, int turnover)
    {
        var snapshot = Replace(Snapshot(101, 100, 105), 1, b => b with { Volume = volume, Turnover = turnover });
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        Assert.Equal(MatchState.Unknown, Assert.Single(run.Evaluations).State);
        Assert.Contains(ExclusionReason.InvalidBar, run.Evaluations[0].Reasons);
    }

    [Fact]
    public void DuplicateDatesBlockRatherThanSilentlyChoosingOne()
    {
        var snapshot = Snapshot(101, 100, 105);
        snapshot = snapshot with { Bars = snapshot.Bars.Append(snapshot.Bars[1] with { Close = 99 }).ToArray() };
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        Assert.False(run.IsResearchAllowed);
        Assert.Contains(run.Diagnostics, d => d.Code == "DuplicateBar" && d.BlocksResearch);
    }

    [Fact]
    public void InsufficientLookbackIsUnknownAndNotAFailedCondition()
    {
        var snapshot = Snapshot(103, 102, 101, 100, 105);
        var definition = ResearchTemplates.CreateDefinition("HL-R002", snapshot.Bars[1].Date, snapshot.Bars[3].Date, snapshot.DataAsOf)
            with { SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1] };
        var run = _engine.Run(snapshot, definition);
        Assert.Equal(3, run.Funnel.CandidateDates);
        Assert.Equal(1, run.Funnel.DeterminableDates);
        Assert.Equal(1, run.Funnel.RawMatchDays);
        Assert.All(run.Evaluations.Take(2), e => Assert.Equal(MatchState.Unknown, e.State));
    }

    [Fact]
    public void FlatCloseInterruptsConsecutiveDeclines()
    {
        var snapshot = Snapshot(103, 102, 102, 101, 100);
        var definition = ResearchTemplates.CreateDefinition("HL-R002", snapshot.Bars[3].Date, snapshot.Bars[3].Date, snapshot.DataAsOf)
            with { SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1] };
        var evaluation = Assert.Single(_engine.Run(snapshot, definition).Evaluations);
        Assert.Equal(MatchState.NotMatched, evaluation.State);
        Assert.Equal(1m, Assert.Single(evaluation.Features).Value);
    }

    [Fact]
    public void RelativeVolumeExcludesEventVolumeFromItsDenominator()
    {
        var snapshot = Snapshot(100, 100, 101, 102);
        snapshot = Replace(snapshot, 2, b => b with { Volume = 150 });
        var definition = ResearchTemplates.CreateDefinition("HL-R003", snapshot.Bars[2].Date, snapshot.Bars[2].Date, snapshot.DataAsOf, 2, 1.5m)
            with { SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1] };
        var run = _engine.Run(snapshot, definition);
        Assert.Single(run.Cases);
        Assert.Equal(1.5m, run.Cases[0].Features.Single(f => f.Condition.Feature == ResearchFeature.RelativeVolume).Value);
    }

    [Fact]
    public void ZeroMeanVolumeIsUndefinedAndFutureVolumesAreNotRequired()
    {
        var snapshot = Snapshot(100, 101, 102, 103);
        snapshot = Replace(snapshot, 0, b => b with { Volume = 0 });
        snapshot = Replace(snapshot, 1, b => b with { Volume = 0 });
        var definition = Definition(snapshot, 2, 2) with
        {
            Conditions = [Condition(ResearchFeature.RelativeVolume, 2, ComparisonOperator.GreaterThanOrEqual, 1.5m)]
        };
        var run = _engine.Run(snapshot, definition);
        Assert.Equal(MatchState.Unknown, Assert.Single(run.Evaluations).State);
        Assert.Contains(ExclusionReason.UndefinedFeature, run.Evaluations[0].Reasons);

        snapshot = Snapshot(101, 100, 102);
        snapshot = Replace(snapshot, 2, b => b with { Volume = null });
        run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        Assert.Equal(1, Assert.Single(run.Statistics).ValidCount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FirstInRun_CombinesUnknownVolumeWithPriceUsingThreeStateAnd(bool priceMatches, bool missingVolume)
    {
        var snapshot = priceMatches ? Snapshot(98, 99, 100, 102, 103) : Snapshot(102, 101, 100, 102, 103);
        snapshot = Replace(snapshot, 0, b => b with { Volume = missingVolume ? null : 0 });
        snapshot = Replace(snapshot, 1, b => b with { Volume = 0 });
        var definition = ResearchTemplates.CreateDefinition("HL-R003", snapshot.Bars[2].Date,
            snapshot.Bars[3].Date, snapshot.DataAsOf, 2, 1m) with { Horizons = [1] };

        var run = _engine.Run(snapshot, definition);
        var first = run.Evaluations[0];
        Assert.Equal(priceMatches ? MatchState.Unknown : MatchState.NotMatched, first.State);
        Assert.Equal(priceMatches ? MatchState.Matched : MatchState.NotMatched,
            first.Features.Single(f => f.Condition.Feature == ResearchFeature.PriceChange).State);
        var volume = first.Features.Single(f => f.Condition.Feature == ResearchFeature.RelativeVolume);
        Assert.Equal(MatchState.Unknown, volume.State);
        Assert.Null(volume.Value);
        Assert.Equal(new[] { missingVolume ? ExclusionReason.MissingVolume : ExclusionReason.UndefinedFeature }, first.Reasons);
        Assert.Equal(MatchState.Matched, run.Evaluations[1].State);
        Assert.Equal(!priceMatches, run.Evaluations[1].IsSampled);
        Assert.Equal(priceMatches ? ExclusionReason.EventStartUnknown : (ExclusionReason?)null, run.Evaluations[1].SamplingExclusion);
        if (priceMatches) Assert.Empty(run.Cases);
        else Assert.Equal(snapshot.Bars[3].Date, Assert.Single(run.Cases).EventDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingVolumeOnlyMakesItsOwnConditionUnknown(int missingIndex)
    {
        var snapshot = Replace(Snapshot(100, 101, 102, 103), missingIndex, b => b with { Volume = null });
        var definition = ResearchTemplates.CreateDefinition("HL-R003", snapshot.Bars[2].Date,
            snapshot.Bars[2].Date, snapshot.DataAsOf, 2, 1m) with { Horizons = [1] };
        var evaluation = Assert.Single(_engine.Run(snapshot, definition).Evaluations);

        Assert.Equal(MatchState.Unknown, evaluation.State);
        Assert.Equal(MatchState.Matched, evaluation.Features[0].State);
        Assert.Equal(MatchState.Unknown, evaluation.Features[1].State);
        Assert.Equal(new[] { ExclusionReason.MissingVolume }, evaluation.Reasons);
    }

    [Fact]
    public void MissingVolumeOutsideTheVolumeConditionWindowDoesNotMakeItUnknown()
    {
        var snapshot = Replace(Snapshot(100, 101, 102, 103, 104), 0, b => b with { Volume = null });
        var definition = Definition(snapshot, 3, 3) with
        {
            Conditions =
            [
                Condition(ResearchFeature.PriceChange, 3, ComparisonOperator.GreaterThan, 0),
                Condition(ResearchFeature.RelativeVolume, 1, ComparisonOperator.GreaterThanOrEqual, 1)
            ]
        };
        var evaluation = Assert.Single(_engine.Run(snapshot, definition).Evaluations);
        Assert.Equal(MatchState.Matched, evaluation.State);
        Assert.All(evaluation.Features, feature => Assert.Equal(MatchState.Matched, feature.State));
        Assert.Empty(evaluation.Reasons);
    }

    [Fact]
    public void FalsePriceConditionDoesNotBypassTheStrictOhlcQualityGate()
    {
        var snapshot = Replace(Snapshot(102, 101, 100, 102), 0, b => b with { Open = null });
        var definition = ResearchTemplates.CreateDefinition("HL-R003", snapshot.Bars[2].Date,
            snapshot.Bars[2].Date, snapshot.DataAsOf, 2, 1m) with { Horizons = [1] };
        var evaluation = Assert.Single(_engine.Run(snapshot, definition).Evaluations);

        Assert.Equal(MatchState.Unknown, evaluation.State);
        Assert.All(evaluation.Features, feature => Assert.Equal(MatchState.Unknown, feature.State));
        Assert.Equal(new[] { ExclusionReason.MissingPrice }, evaluation.Reasons);
    }

    [Fact]
    public void FlatPriorRangeIsUndefinedAndRangePositionsAreNotClamped()
    {
        var snapshot = Snapshot(100, 100, 110, 111);
        snapshot = snapshot with { Bars = snapshot.Bars.Select(b => b with { Open = b.Close, High = b.Close, Low = b.Close }).ToArray() };
        var definition = Definition(snapshot, 2, 2) with
        {
            Conditions = [Condition(ResearchFeature.RangePosition, 2, ComparisonOperator.GreaterThan, 1)]
        };
        var run = _engine.Run(snapshot, definition);
        Assert.Equal(MatchState.Unknown, Assert.Single(run.Evaluations).State);
        Assert.Null(Assert.Single(run.Evaluations[0].Features).Value);
        snapshot = Replace(snapshot, 0, b => b with { Low = 99 });
        run = _engine.Run(snapshot, definition);
        Assert.Equal(11m, Assert.Single(Assert.Single(run.Cases).Features).Value);
    }

    [Fact]
    public void PriorHighDoesNotIncludeEventHighAndEqualityIsNotBreakout()
    {
        var snapshot = Snapshot(100, 100, 101, 102);
        snapshot = Replace(snapshot, 0, b => b with { High = 101 });
        snapshot = Replace(snapshot, 1, b => b with { High = 101 });
        snapshot = Replace(snapshot, 2, b => b with { High = 150 });
        var definition = ResearchTemplates.CreateDefinition("HL-R004", snapshot.Bars[2].Date, snapshot.Bars[2].Date, snapshot.DataAsOf, 2)
            with { SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1] };
        Assert.Empty(_engine.Run(snapshot, definition).Cases);
        snapshot = Replace(snapshot, 2, b => b with { Close = 102 });
        Assert.Single(_engine.Run(snapshot, definition).Cases);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(110, true)]
    [InlineData(99, false)]
    [InlineData(111, false)]
    public void NearLowTemplateIncludesBoundsButNotBelowPriorLow(int eventClose, bool expected)
    {
        var snapshot = Snapshot(101, eventClose, 100);
        snapshot = Replace(snapshot, 0, b => b with { Low = 100 });
        var definition = ResearchTemplates.CreateDefinition("HL-R001", snapshot.Bars[1].Date, snapshot.Bars[1].Date, snapshot.DataAsOf, 1, 0.1m)
            with { SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1] };
        Assert.Equal(expected ? 1 : 0, _engine.Run(snapshot, definition).Cases.Count);
    }

    [Fact]
    public void HitBoundariesAreInclusiveAndDistinctFromNegativeEndChange()
    {
        var snapshot = Replace(Snapshot(101, 100, 99), 2, b => b with { High = 105, Low = 97 });
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1));
        var stats = Assert.Single(run.Statistics);
        Assert.Equal(1, stats.UpperHitCount);
        Assert.Equal(1, stats.LowerHitCount);
        Assert.Equal(1, stats.DownCount);
        Assert.Equal(0, stats.UpCount);
    }

    [Fact]
    public void FutureHighestPriceCanRemainNegativeAndEventIntradayExtremesAreExcluded()
    {
        var snapshot = Replace(Snapshot(101, 100, 95), 1, b => b with { High = 1000, Low = 1 });
        var outcome = Assert.Single(Assert.Single(_engine.Run(snapshot, Definition(snapshot, 1, 1)).Cases).Outcomes);
        Assert.Equal(-0.04m, outcome.HighestPriceChange);
        Assert.Equal(-0.06m, outcome.LowestPriceChange);
        Assert.Equal(-0.05m, outcome.CloseMaxDrawdown);
    }

    [Fact]
    public void HistoricalCutoffPreventsFutureResultsAndActionLookahead()
    {
        var snapshot = Snapshot(101, 100, 105, 500);
        snapshot = snapshot with { CorporateActions = [new CorporateAction { EffectiveDate = snapshot.Bars[3].Date, Kind = "FutureAction", SourceId = Source }] };
        var definition = Definition(snapshot, 1, 1) with { Horizons = [1, 2], DataAsOf = snapshot.Bars[2].Date };
        var run = _engine.Run(snapshot, definition);
        Assert.Equal(0.05m, run.Statistics.Single(s => s.Horizon == 1).MeanPriceChange);
        var future = Assert.Single(run.Cases).Outcomes.Single(o => o.Horizon == 2);
        Assert.Equal(ExclusionReason.InsufficientFutureData, future.PrimaryExclusion);
        Assert.DoesNotContain(ExclusionReason.CorporateActionInWindow, future.Exclusions);
        Assert.Null(future.PriceChange);
    }

    [Fact]
    public void EmptyAndSingleFlatSamplesUseNullOrExactApplicableStatistics()
    {
        var snapshot = Snapshot(101, 100, 100);
        var single = Assert.Single(_engine.Run(snapshot, Definition(snapshot, 1, 1)).Statistics);
        Assert.Equal(1, single.ValidCount);
        Assert.True(single.IsSmallSample);
        Assert.Equal(1m, single.FlatRate);
        Assert.Equal(0m, single.P10);
        Assert.Equal(0m, single.P90);
        var empty = Assert.Single(_engine.Run(snapshot, Definition(snapshot, 2, 2)).Statistics);
        Assert.Equal(0, empty.ValidCount);
        Assert.Null(empty.MedianPriceChange);
        Assert.Null(empty.UpperHitRate);
        Assert.Null(empty.P10);
        Assert.False(empty.IsSmallSample);
    }

    [Fact]
    public void PositiveChangeSmallerThanDisplayPrecisionIsStillUp()
    {
        var snapshot = Snapshot(101, 100, 100.00000001m);
        var stats = Assert.Single(_engine.Run(snapshot, Definition(snapshot, 1, 1)).Statistics);
        Assert.Equal(1, stats.UpCount);
        Assert.Equal(0, stats.FlatCount);
        Assert.Equal(0.0000000001m, stats.MeanPriceChange);
    }

    [Fact]
    public void InvalidDefinitionsCannotBeExecuted()
    {
        var snapshot = Snapshot(101, 100, 105);
        var definition = Definition(snapshot, 1, 1);
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot, definition with { Horizons = [1, 1] }));
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot, definition with { Conditions = [] }));
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot, definition with { LowerThreshold = -1 }));
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot, definition with { DataAsOf = snapshot.Bars[0].Date }));
        Assert.Throws<ArgumentException>(() => _engine.Run(snapshot, definition with
        {
            Conditions = [Condition(ResearchFeature.ConsecutiveDeclines, 3, ComparisonOperator.GreaterThanOrEqual, 4)]
        }));
    }

    [Fact]
    public void UpperThresholdOutsideThePercentageDisplayRangeIsRejectedBeforeResearch()
    {
        var snapshot = Snapshot(101, 100, 105);
        var error = Assert.Throws<ArgumentException>(() => _engine.Run(snapshot,
            Definition(snapshot, 1, 1) with { UpperThreshold = decimal.MaxValue }));
        Assert.Equal("definition", error.ParamName);
        Assert.Contains("百分比可表示範圍", error.Message);
    }

    [Fact]
    public void ExtremeRepresentableUpperThresholdHasNoReachableUpperPrice()
    {
        var snapshot = Snapshot(101, 100, 105);
        var run = _engine.Run(snapshot, Definition(snapshot, 1, 1) with { UpperThreshold = decimal.MaxValue / 100m });
        var outcome = Assert.Single(Assert.Single(run.Cases).Outcomes);

        Assert.True(outcome.IsValid);
        Assert.Equal(0.05m, outcome.PriceChange);
        Assert.Null(outcome.UpperFirstHitTradingDay);
        Assert.Equal(ThresholdOrder.Neither, outcome.ThresholdOrder);
        Assert.Equal(0, Assert.Single(run.Statistics).UpperHitCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpperPriceAtDecimalBoundaryOnlyHitsWhenTheProductIsRepresentable(bool targetOverflows)
    {
        var basePrice = (decimal.MaxValue - 1m) / 2m + (targetOverflows ? 1m : 0m);
        var snapshot = Replace(Snapshot(basePrice, basePrice, basePrice), 2, b => b with { High = decimal.MaxValue });
        var definition = Definition(snapshot, 1, 1) with
        {
            UpperThreshold = 1m,
            Conditions = [Condition(ResearchFeature.PriceChange, 1, ComparisonOperator.GreaterThanOrEqual, 0)]
        };
        var outcome = Assert.Single(Assert.Single(_engine.Run(snapshot, definition).Cases).Outcomes);

        Assert.True(outcome.IsValid);
        Assert.Equal(targetOverflows ? (int?)null : 1, outcome.UpperFirstHitTradingDay);
        Assert.Equal(targetOverflows ? ThresholdOrder.Neither : ThresholdOrder.UpperFirst, outcome.ThresholdOrder);
    }

    [Fact]
    public void RepresentableUpperPriceRetainsDecimalMultiplicationRounding()
    {
        const decimal unit = 0.0000000000000000000000000001m;
        var snapshot = Snapshot(2m * unit, unit, unit);
        snapshot = snapshot with
        {
            Bars = snapshot.Bars.Select(b => b with { Open = b.Close, High = b.Close, Low = b.Close }).ToArray()
        };
        var outcome = Assert.Single(Assert.Single(_engine.Run(snapshot,
            Definition(snapshot, 1, 1) with { UpperThreshold = 0.5m }).Cases).Outcomes);

        Assert.True(outcome.IsValid);
        Assert.Null(outcome.UpperFirstHitTradingDay);
        Assert.Equal(1, outcome.LowerFirstHitTradingDay);
    }

    [Fact]
    public void CancelledResearchNeverReturnsACompleteResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var snapshot = Snapshot(101, 100, 105);
        Assert.Throws<OperationCanceledException>(() => _engine.Run(snapshot, Definition(snapshot, 1, 1), cancellation.Token));
    }

    [Fact]
    public void SnapshotHashIsStableAcrossInputOrderingAndRetrievalTimeButDetectsChanges()
    {
        var snapshot = Snapshot(101, 100, 105);
        var hash = SnapshotFingerprint.Compute(snapshot);
        var reordered = snapshot with
        {
            Bars = snapshot.Bars.Reverse().ToArray(), RetrievedAtUtc = DateTimeOffset.UtcNow,
            Calendar = snapshot.Calendar with { TradingDates = snapshot.Calendar.TradingDates.Reverse().ToArray() },
            SnapshotId = "different-id"
        };
        Assert.Equal(hash, SnapshotFingerprint.Compute(reordered));
        var modified = Replace(snapshot with { ContentHash = hash }, 2, b => b with { Volume = 999 });
        Assert.NotEqual(hash, SnapshotFingerprint.Compute(modified));
        var run = _engine.Run(modified, Definition(modified, 1, 1));
        Assert.False(run.IsResearchAllowed);
        Assert.Contains(run.Diagnostics, d => d.Code == "SnapshotHashMismatch");
    }

    [Fact]
    public void SnapshotHashIgnoresDecimalDisplayScale()
    {
        var snapshot = Snapshot(101, 100, 105);
        var sameValues = snapshot with
        {
            Bars = snapshot.Bars.Select(b => b with
            {
                Open = b.Open * 1.00m, High = b.High * 1.000m, Low = b.Low * 1.0m,
                Close = b.Close * 1.0000m, Volume = b.Volume * 1.000m
            }).ToArray()
        };
        Assert.Equal(SnapshotFingerprint.Compute(snapshot), SnapshotFingerprint.Compute(sameValues));
    }

    [Fact]
    public void DemoIsUnmistakablySyntheticDeterministicAndSupportsAllTemplates()
    {
        var snapshot = DemoData.Create();
        Assert.True(snapshot.IsSynthetic);
        Assert.Equal(SecurityType.Synthetic, snapshot.Instrument.SecurityType);
        Assert.Equal("SYNTHETIC", snapshot.Instrument.Market);
        Assert.Contains("非真實行情", snapshot.Instrument.Name);
        Assert.Equal(snapshot.ContentHash, DemoData.Create().ContentHash);
        foreach (var template in ResearchTemplates.All)
        {
            var definition = ResearchTemplates.CreateDefinition(template.Id, snapshot.Calendar.TradingDates[150],
                snapshot.Calendar.TradingDates[^61], snapshot.DataAsOf);
            var first = _engine.Run(snapshot, definition);
            var second = _engine.Run(snapshot, definition);
            Assert.True(first.IsResearchAllowed);
            Assert.Equal(4, first.Statistics.Count);
            Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
            output.WriteLine($"{template.Id}: {first.Funnel.RawMatchDays} matches, {first.Cases.Count} sampled events");
        }
    }

    [Fact]
    public void RunCopiesDefinitionCollectionsAndJsonRoundTripPreservesResults()
    {
        var snapshot = Snapshot(101, 100, 105);
        var conditions = new List<ResearchCondition> { Condition(ResearchFeature.PriceChange, 1, ComparisonOperator.LessThan, 0) };
        var horizons = new List<int> { 1 };
        var definition = Definition(snapshot, 1, 1) with { Conditions = conditions, Horizons = horizons };
        var run = _engine.Run(snapshot, definition);
        conditions.Clear();
        horizons.Clear();
        Assert.Single(run.Definition.Conditions);
        Assert.Single(run.Definition.Horizons);
        var json = JsonSerializer.Serialize(run);
        var restored = JsonSerializer.Deserialize<ResearchRun>(json);
        Assert.Equal(json, JsonSerializer.Serialize(restored));
    }

    [Fact]
    public void TenThousandBarsSixConditionsFourHorizons_ReportsMeasuredRuntime()
    {
        var snapshot = Snapshot(Enumerable.Repeat(100m, 10_000).ToArray());
        var definition = Definition(snapshot, 120, 9999) with
        {
            Conditions =
            [
                Condition(ResearchFeature.PriceChange, 1, ComparisonOperator.GreaterThanOrEqual, -1),
                Condition(ResearchFeature.DistanceFromPriorLow, 120, ComparisonOperator.GreaterThanOrEqual, 0),
                Condition(ResearchFeature.RelativeToPriorHigh, 60, ComparisonOperator.GreaterThanOrEqual, -1),
                Condition(ResearchFeature.RelativeVolume, 20, ComparisonOperator.GreaterThanOrEqual, 0),
                Condition(ResearchFeature.RangePosition, 60, ComparisonOperator.GreaterThanOrEqual, 0),
                Condition(ResearchFeature.ConsecutiveAdvances, 3, ComparisonOperator.GreaterThanOrEqual, 0)
            ],
            Horizons = [5, 10, 20, 60]
        };
        var stopwatch = Stopwatch.StartNew();
        var first = _engine.Run(snapshot, definition);
        stopwatch.Stop();
        var firstMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart();
        var warm = _engine.Run(snapshot, definition);
        stopwatch.Stop();
        output.WriteLine($"10,000 synthetic bars / 6 conditions / 4 horizons / EveryMatch: first measured run {firstMilliseconds:F1} ms, warm run {stopwatch.Elapsed.TotalMilliseconds:F1} ms. Input already in memory; includes validation/hash; excludes I/O. JIT may be warm from other tests.");
        output.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; OS: {RuntimeInformation.OSDescription}; Architecture: {RuntimeInformation.ProcessArchitecture}; logical processors: {Environment.ProcessorCount}; process working set: {Environment.WorkingSet:N0} bytes.");
        Assert.Equal(9880, first.Cases.Count);
        Assert.Equal(9880 - 5, first.Statistics.Single(s => s.Horizon == 5).ValidCount);
        Assert.Equal(9880 - 60, first.Statistics.Single(s => s.Horizon == 60).ValidCount);
        Assert.Equal(first.Funnel, warm.Funnel);
    }

    private static ResearchDefinition Definition(DataSnapshot snapshot, int firstIndex, int lastIndex) => new()
    {
        EventStart = snapshot.Calendar.TradingDates[firstIndex], EventEnd = snapshot.Calendar.TradingDates[lastIndex],
        DataAsOf = snapshot.DataAsOf, SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1],
        Conditions = [Condition(ResearchFeature.PriceChange, 1, ComparisonOperator.LessThan, 0)]
    };

    private static ResearchCondition Condition(ResearchFeature feature, int lookback, ComparisonOperator op, decimal value) =>
        new() { Feature = feature, Lookback = lookback, Operator = op, Value = value };

    private static DataSnapshot Replace(DataSnapshot snapshot, int index, Func<DailyBar, DailyBar> replace) =>
        snapshot with { Bars = snapshot.Bars.Select((b, i) => i == index ? replace(b) : b).ToArray() };

    private static DataSnapshot Snapshot(params decimal[] closes)
    {
        var dates = new List<DateOnly>();
        var day = new DateOnly(2024, 1, 2);
        while (dates.Count < closes.Length)
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dates.Add(day);
            day = day.AddDays(1);
        }
        return new DataSnapshot
        {
            Instrument = new Instrument
            {
                InstrumentId = "synthetic:test:2024", Code = "TEST", Name = "Synthetic fixture", Market = "SYNTHETIC",
                SecurityType = SecurityType.Synthetic
            },
            SourceId = Source, DataVersion = "test-v1", IsSynthetic = true, DataAsOf = dates[^1],
            Calendar = new TradingCalendar
            {
                Market = "SYNTHETIC", Version = "test-calendar-v1", IsVerified = true,
                TradingDates = dates, CoverageStart = dates[0], CoverageEnd = dates[^1]
            },
            ActionCoverage = new CorporateActionCoverage
            {
                IsVerified = true, Version = "test-actions-v1", SourceId = Source, CoverageStart = dates[0], CoverageEnd = dates[^1]
            },
            Bars = closes.Select((c, i) => new DailyBar
            {
                Date = dates[i], Open = c, High = c + 1, Low = c - 1, Close = c, Volume = 100, SourceId = Source
            }).ToArray()
        };
    }
}
