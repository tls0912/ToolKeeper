using System.IO;
using System.Text.Json;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

public sealed class PriceComparabilityTests
{
    [Fact]
    public void VerifiedPriceComparisonAllowsResearchWithoutClaimingCompleteCorporateActions()
    {
        var snapshot = Snapshot();
        var run = new ResearchEngine().Run(snapshot, Definition(snapshot));
        Assert.False(snapshot.ActionCoverage.IsVerified);
        Assert.True(run.IsResearchAllowed);
        Assert.NotEmpty(run.Cases);
        Assert.Equal(snapshot.ComparabilityCoverage!.Version, run.PriceComparisonVersion);
        Assert.Contains(run.Diagnostics, issue => issue.Code == "CorporateActionCoverageUnknown" && !issue.BlocksResearch);
        Assert.DoesNotContain(run.Diagnostics, issue => issue.BlocksResearch);
    }

    [Fact]
    public void ConditionAndOutcomeWindowsNeverCrossComparisonGapsOrKnownActionBoundaries()
    {
        var snapshot = Snapshot();
        var dates = snapshot.Calendar.TradingDates;
        snapshot = snapshot with
        {
            ComparabilityCoverage = snapshot.ComparabilityCoverage! with
            {
                Gaps = [new() { Start = dates[0], End = dates[0] }, new() { Start = dates[6], End = dates[6] }]
            },
            CorporateActions = [new() { EffectiveDate = dates[11], Kind = "RawComparisonBoundary", SourceId = "TWSE/STOCK_DAY" }]
        };
        var run = new ResearchEngine().Run(snapshot, Definition(snapshot));
        Assert.True(run.IsResearchAllowed);
        Assert.Contains(run.Evaluations, item => item.Date == dates[6] && item.Reasons.Contains(ExclusionReason.CorporateActionCoverageUnknown));
        Assert.Contains(run.Evaluations, item => item.Date == dates[11] && item.Reasons.Contains(ExclusionReason.CorporateActionInWindow));
        Assert.Contains(run.Cases.SelectMany(item => item.Outcomes), item => item.Exclusions.Contains(ExclusionReason.CorporateActionCoverageUnknown));
        Assert.Contains(run.Cases.SelectMany(item => item.Outcomes), item => item.Exclusions.Contains(ExclusionReason.CorporateActionInWindow));
        foreach (var item in run.Cases)
        {
            foreach (var outcome in item.Outcomes.Where(value => value.IsValid))
                Assert.DoesNotContain(new[] { dates[0], dates[6], dates[11] }, boundary => boundary >= item.ConditionWindowStart && boundary <= outcome.EndDate);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unverified")]
    [InlineData("source")]
    [InlineData("gap-outside")]
    [InlineData("gap-null")]
    [InlineData("version")]
    public void ComparisonCoverageMustHaveAValidVerifiedContract(string mutation)
    {
        var snapshot = Snapshot();
        snapshot = snapshot with { ComparabilityCoverage = mutation switch
        {
            "missing" => null,
            "unverified" => snapshot.ComparabilityCoverage! with { IsVerified = false },
            "source" => snapshot.ComparabilityCoverage! with { SourceId = "unknown" },
            "gap-outside" => snapshot.ComparabilityCoverage! with { Gaps = [new() { Start = new(1990, 1, 1), End = new(1990, 1, 2) }] },
            "gap-null" => snapshot.ComparabilityCoverage! with { Gaps = [null!] },
            "version" => snapshot.ComparabilityCoverage! with { Version = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        } };
        Assert.False(new ResearchEngine().Run(snapshot, Definition(snapshot)).IsResearchAllowed);
    }

    [Fact]
    public void ComparisonContentAffectsHashButNullFieldsDoNotAlterLegacyJson()
    {
        var legacy = DemoData.Create();
        Assert.DoesNotContain("ComparabilityCoverage", JsonSerializer.Serialize(legacy));
        // Fingerprint of the original 0.1.0-m0 delivered demo, before comparison coverage existed.
        Assert.Equal("957ca53cdd6baddf3b561a2ab08c75b00627e3080df5456c97d88e973de386c3", SnapshotFingerprint.Compute(legacy));
        var snapshot = Snapshot();
        var hash = SnapshotFingerprint.Compute(snapshot);
        Assert.NotEqual(hash, SnapshotFingerprint.Compute(snapshot with { ComparabilityCoverage = snapshot.ComparabilityCoverage! with { Version = "revised" } }));
        var gaps = new[] { new DateRange { Start = snapshot.Calendar.TradingDates[0], End = snapshot.Calendar.TradingDates[0] },
            new DateRange { Start = snapshot.Calendar.TradingDates[6], End = snapshot.Calendar.TradingDates[6] } };
        var one = snapshot with { ComparabilityCoverage = snapshot.ComparabilityCoverage! with { Gaps = gaps } };
        var two = snapshot with { ComparabilityCoverage = snapshot.ComparabilityCoverage! with { Gaps = gaps.Reverse().ToArray() } };
        Assert.Equal(SnapshotFingerprint.Compute(one), SnapshotFingerprint.Compute(two));
    }

    [Fact]
    public async Task ComparableMarketDataAndResearchRoundTripAndRejectVersionMismatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens-comparability-" + Guid.NewGuid().ToString("N"));
        try
        {
            var snapshot = Snapshot();
            var hash = SnapshotFingerprint.Compute(snapshot);
            snapshot = snapshot with { SnapshotId = hash, ContentHash = hash };
            var run = new ResearchEngine().Run(snapshot, Definition(snapshot));
            var dataPath = await new MarketDataStore(directory).SaveAsync(new HistoricalDataDownload(snapshot, SnapshotValidator.Validate(snapshot)));
            Assert.False((await MarketDataStore.LoadAsync(dataPath)).Snapshot.ActionCoverage.IsVerified);
            var store = new ResearchStore(directory);
            var researchPath = await store.SaveAsync(snapshot, run);
            var loaded = await ResearchStore.LoadAsync(researchPath);
            Assert.Equal(JsonSerializer.Serialize(run), JsonSerializer.Serialize(loaded.Run));
            Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(loaded.Snapshot));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(snapshot, run with { PriceComparisonVersion = "other" }));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static ResearchDefinition Definition(DataSnapshot snapshot) => new()
    {
        EventStart = snapshot.Calendar.CoverageStart, EventEnd = snapshot.DataAsOf, DataAsOf = snapshot.DataAsOf,
        Conditions = [new() { Feature = ResearchFeature.PriceChange, Lookback = 1, Operator = ComparisonOperator.GreaterThan, Value = -1 }],
        SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1, 3]
    };

    private static DataSnapshot Snapshot()
    {
        // Explicitly fabricated fixture for the core contract; no network or actual price assertions.
        var dates = Enumerable.Range(0, 28).Select(i => new DateOnly(2025, 1, 2).AddDays(i))
            .Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToArray();
        return new()
        {
            SourceId = "TWSE", DataVersion = "test-fixture", RetrievedAtUtc = DateTimeOffset.UtcNow,
            DataAsOf = dates[^1],
            Instrument = new() { InstrumentId = "TWSE:TEST", Code = "2330", Name = "Offline test fixture", Market = "TWSE" },
            Calendar = new() { Market = "TWSE", Version = "test", IsVerified = true, CoverageStart = dates[0], CoverageEnd = dates[^1], TradingDates = dates },
            ActionCoverage = new() { SourceId = "TWSE", Version = "incomplete-test", CoverageStart = dates[0], CoverageEnd = dates[^1], IsVerified = false },
            ComparabilityCoverage = new() { SourceId = "TWSE/STOCK_DAY", Version = "test-comparison", CoverageStart = dates[0], CoverageEnd = dates[^1], IsVerified = true,
                Gaps = [new() { Start = dates[0], End = dates[0] }] },
            Bars = dates.Select((date, i) => new DailyBar { Date = date, Open = 100 - i, High = 101 - i, Low = 99 - i, Close = 100 - i, Volume = 1000, SourceId = "TWSE" }).ToArray()
        };
    }
}
