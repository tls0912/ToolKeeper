using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoLens.Core;
using Xunit;
using Xunit.Abstractions;

namespace HistoLens.Tests;

public sealed class ResearchStoreValidationTests(ITestOutputHelper output)
{
    private static readonly Lazy<(DataSnapshot Snapshot, ResearchRun Run)> Demo = new(() =>
    {
        var snapshot = DemoData.Create();
        return (snapshot, new ResearchEngine().Run(snapshot,
            ResearchTemplates.CreateDefinition("HL-R002", snapshot.Calendar.TradingDates[130], snapshot.DataAsOf, snapshot.DataAsOf)));
    });
    private static readonly Lazy<(DataSnapshot Snapshot, ResearchRun Run)> Dense = new(CreateDenseResearch);

    [Fact]
    public async Task ReadsOriginalIndentedV1AndPreservesItsHistoricalEngineVersion()
    {
        using var directory = new TestDirectory();
        var (snapshot, run) = Demo.Value;
        run = run with { EngineVersion = "0.1.0-m0" };
        var file = await WriteLegacyAsync(directory.Path, snapshot, run);
        var loaded = await ResearchStore.LoadAsync(file);
        Assert.Equal("0.1.0-m0", loaded.Run.EngineVersion);
        Assert.Equal(JsonSerializer.Serialize(run), JsonSerializer.Serialize(loaded.Run));
        Assert.Equal(snapshot.ContentHash, loaded.Snapshot.ContentHash);
        Assert.Equal(run.Funnel, loaded.Run.Funnel);
    }

    [Theory]
    [InlineData("statistics-null")]
    [InlineData("definition-null")]
    [InlineData("features-null")]
    [InlineData("outcome-null")]
    [InlineData("outcome-horizon")]
    [InlineData("outcome-metric")]
    [InlineData("hit-date")]
    [InlineData("evaluation-duplicate")]
    [InlineData("funnel-count")]
    [InlineData("statistic-count")]
    [InlineData("statistic-rate")]
    [InlineData("case-date")]
    [InlineData("instrument-code")]
    [InlineData("instrument-id")]
    [InlineData("source")]
    [InlineData("calendar-version")]
    [InlineData("action-version")]
    [InlineData("upper-overflow")]
    public async Task RejectsStructurallyInvalidOrInconsistentPayloadsEvenWithAValidChecksum(string mutation)
    {
        using var directory = new TestDirectory();
        var (snapshot, original) = Demo.Value;
        var run = mutation switch
        {
            "statistics-null" => original with { Statistics = null! },
            "definition-null" => original with { Definition = null! },
            "features-null" => original with { Evaluations = original.Evaluations.Select((item, index) => index == 0 ? item with { Features = null! } : item).ToArray() },
            "outcome-null" => ChangeFirstCase(original, item => item with { Outcomes = [null!] }),
            "outcome-horizon" => ChangeFirstOutcome(original, item => item with { Horizon = 999 }),
            "outcome-metric" => ChangeFirstOutcome(original, item => item with { PriceChange = null }),
            "hit-date" => ChangeFirstOutcome(original, item => item with { UpperFirstHitTradingDay = 1, UpperFirstHitDate = snapshot.Calendar.TradingDates[0] }),
            "evaluation-duplicate" => original with { Evaluations = original.Evaluations.Append(original.Evaluations[0]).ToArray() },
            "funnel-count" => original with { Funnel = original.Funnel with { SampledEvents = -1 } },
            "statistic-count" => original with { Statistics = original.Statistics.Select((item, index) => index == 0 ? item with { ValidCount = item.ValidCount + 1 } : item).ToArray() },
            "statistic-rate" => original with { Statistics = original.Statistics.Select((item, index) => index == 0 ? item with { UpRate = 2 } : item).ToArray() },
            "case-date" => ChangeFirstCase(original, item => item with { EventDate = snapshot.DataAsOf.AddDays(1) }),
            "instrument-code" => original with { Instrument = original.Instrument with { Code = "DIFFERENT" } },
            "instrument-id" => original with { Instrument = original.Instrument with { InstrumentId = "different-id" } },
            "source" => original with { SourceId = "different-source" },
            "calendar-version" => original with { CalendarVersion = "different-calendar" },
            "action-version" => original with { CorporateActionVersion = "different-actions" },
            "upper-overflow" => original with { Definition = original.Definition with { UpperThreshold = decimal.MaxValue } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var file = await WriteLegacyAsync(directory.Path, snapshot, run);
        await Assert.ThrowsAsync<InvalidDataException>(() => ResearchStore.LoadAsync(file));
        var store = new ResearchStore(System.IO.Path.Combine(directory.Path, "new-saves"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(snapshot, run));
        Assert.False(Directory.Exists(store.DirectoryPath));
    }

    [Theory]
    [InlineData("bars")]
    [InlineData("calendar")]
    [InlineData("actions")]
    [InlineData("gaps")]
    public async Task RejectsNullSnapshotCollectionsWithoutCrashingInsideHashing(string collection)
    {
        using var directory = new TestDirectory();
        var (original, run) = Demo.Value;
        var snapshot = collection switch
        {
            "bars" => original with { Bars = null! },
            "calendar" => original with { Calendar = original.Calendar with { TradingDates = null! } },
            "actions" => original with { CorporateActions = [null!] },
            "gaps" => original with { ActionCoverage = original.ActionCoverage with { Gaps = null! } },
            _ => throw new ArgumentOutOfRangeException(nameof(collection))
        };
        var file = await WriteLegacyAsync(directory.Path, snapshot, run);
        await Assert.ThrowsAsync<InvalidDataException>(() => ResearchStore.LoadAsync(file));
    }

    [Fact]
    public async Task PositivePricesWhoseDecimalRatioRoundsToZeroCanStillRoundTrip()
    {
        using var directory = new TestDirectory();
        var original = DemoData.Create();
        var dates = original.Calendar.TradingDates.Take(3).ToArray();
        var snapshot = original with
        {
            SnapshotId = "decimal-boundary-synthetic", ContentHash = "", DataAsOf = dates[^1],
            Calendar = original.Calendar with { CoverageEnd = dates[^1], TradingDates = dates },
            ActionCoverage = original.ActionCoverage with { CoverageEnd = dates[^1] }, CorporateActions = [],
            Bars = dates.Select((date, index) =>
            {
                var close = index == 2 ? 0.0000000000000000000000000001m : decimal.MaxValue / 100m;
                return new DailyBar { Date = date, Open = close, High = close, Low = close, Close = close, Volume = 1000, SourceId = original.SourceId };
            }).ToArray()
        };
        snapshot = snapshot with { ContentHash = SnapshotFingerprint.Compute(snapshot) };
        var run = new ResearchEngine().Run(snapshot, new ResearchDefinition
        {
            EventStart = dates[1], EventEnd = dates[1], DataAsOf = dates[^1], SamplingPolicy = SamplingPolicy.EveryMatch, Horizons = [1],
            Conditions = [new ResearchCondition { Feature = ResearchFeature.PriceChange, Lookback = 1, Operator = ComparisonOperator.Equal, Value = 0 }]
        });
        var outcome = Assert.Single(Assert.Single(run.Cases).Outcomes);
        Assert.True(outcome.IsValid);
        Assert.Equal(-1m, outcome.PriceChange);
        Assert.Equal(-1m, outcome.CloseMaxDrawdown);
        var loaded = await ResearchStore.LoadAsync(await new ResearchStore(directory.Path).SaveAsync(snapshot, run));
        Assert.Equal(JsonSerializer.Serialize(outcome), JsonSerializer.Serialize(Assert.Single(Assert.Single(loaded.Run.Cases).Outcomes)));
    }

    [Fact]
    public async Task DenseTenThousandBarSixConditionResearchRoundTripsWithinBoundedV1Size()
    {
        using var directory = new TestDirectory();
        var (snapshot, run) = Dense.Value;
        Assert.Equal(10_000, snapshot.Bars.Count);
        Assert.Equal(9_880, run.Cases.Count);
        Assert.Equal(6, run.Definition.Conditions.Count);
        Assert.Equal(4, run.Definition.Horizons.Count);
        var stopwatch = Stopwatch.StartNew();
        var save = new ResearchStore(directory.Path).SaveAsync(snapshot, run);
        var synchronousMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        var file = await save;
        var saveMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        var bytes = new FileInfo(file).Length;
        Assert.InRange(bytes, 1, ResearchStore.MaximumBytes);
        var loaded = await ResearchStore.LoadAsync(file);
        Assert.Equal(run.Funnel, loaded.Run.Funnel);
        Assert.Equal(run.DataContentHash, loaded.Run.DataContentHash);
        Assert.Equal(run.Cases.SelectMany(item => item.Outcomes).Select(item => (item.Horizon, item.EndDate, item.PriceChange, item.PrimaryExclusion)),
            loaded.Run.Cases.SelectMany(item => item.Outcomes).Select(item => (item.Horizon, item.EndDate, item.PriceChange, item.PrimaryExclusion)));
        Assert.Equal(JsonSerializer.Serialize(run.Statistics), JsonSerializer.Serialize(loaded.Run.Statistics));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        output.WriteLine($"Dense v1: {bytes} bytes; call returned after {synchronousMilliseconds:F2} ms; save {saveMilliseconds:F2} ms; save/load total {stopwatch.Elapsed.TotalMilliseconds:F2} ms.");
    }

    [Fact]
    public async Task SaveReturnsBeforeValidationAndSerializationTouchTheCallingThread()
    {
        using var directory = new TestDirectory();
        using var release = new ManualResetEventSlim();
        var returned = new TaskCompletionSource<Task<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (snapshot, run) = Demo.Value;
        var callerThread = 0;
        var enumeratorThread = 0;
        var guarded = snapshot with
        {
            Bars = new ObservedList<DailyBar>(snapshot.Bars, () =>
            {
                Interlocked.CompareExchange(ref enumeratorThread, Environment.CurrentManagedThreadId, 0);
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Save ran synchronously on its caller.");
            })
        };
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            try { returned.SetResult(new ResearchStore(directory.Path).SaveAsync(guarded, run)); }
            catch (Exception error) { returned.SetException(error); }
        });
        caller.Start();
        Task<string>? save = null;
        try { save = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.Set(); caller.Join(TimeSpan.FromSeconds(5)); }
        Assert.NotNull(save);
        await save;
        Assert.NotEqual(0, enumeratorThread);
        Assert.NotEqual(callerThread, enumeratorThread);
    }

    [Fact]
    public async Task CancellingAfterTemporaryFileCreationRemovesItAndRetainsPreviousSave()
    {
        using var directory = new TestDirectory();
        var store = new ResearchStore(directory.Path);
        var (demo, demoRun) = Demo.Value;
        var previous = await store.SaveAsync(demo, demoRun);
        var previousBytes = await File.ReadAllBytesAsync(previous);
        using var cancellation = new CancellationTokenSource();
        using var watcher = new FileSystemWatcher(directory.Path, "*.tmp") { NotifyFilter = NotifyFilters.FileName };
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Created += (_, _) => { observed.TrySetResult(); cancellation.Cancel(); };
        watcher.EnableRaisingEvents = true;
        var (snapshot, run) = Dense.Value;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(snapshot, run, cancellation.Token));
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        Assert.Equal(previous, Assert.Single(store.List()));
        Assert.Equal(previousBytes, await File.ReadAllBytesAsync(previous));
        Assert.Equal(demoRun.Funnel, (await ResearchStore.LoadAsync(previous)).Run.Funnel);
    }

    [Fact]
    public async Task OversizeAndPreCancelledLoadsDoNotParseTheFile()
    {
        using var directory = new TestDirectory();
        var file = System.IO.Path.Combine(directory.Path, "oversize.histolens.json");
        using (var stream = File.Create(file)) stream.SetLength(ResearchStore.MaximumBytes + 1L);
        await Assert.ThrowsAsync<InvalidDataException>(() => ResearchStore.LoadAsync(file));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ResearchStore.LoadAsync(file, new CancellationToken(true)));
    }

    private static ResearchRun ChangeFirstCase(ResearchRun run, Func<ResearchCase, ResearchCase> change) =>
        run with { Cases = run.Cases.Select((item, index) => index == 0 ? change(item) : item).ToArray() };

    private static ResearchRun ChangeFirstOutcome(ResearchRun run, Func<HorizonOutcome, HorizonOutcome> change) =>
        ChangeFirstCase(run, item => item with { Outcomes = item.Outcomes.Select((outcome, index) => index == 0 ? change(outcome) : outcome).ToArray() });

    private static async Task<string> WriteLegacyAsync(string directory, DataSnapshot snapshot, ResearchRun run)
    {
        var originalOptions = new JsonSerializerOptions { WriteIndented = true };
        var payload = JsonSerializer.Serialize(new SavedResearch { SavedAtUtc = DateTimeOffset.UtcNow, Snapshot = snapshot, Run = run }, originalOptions);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var file = System.IO.Path.Combine(directory, $"legacy-{Guid.NewGuid():N}.histolens.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { FormatVersion = 1, Checksum = checksum, Payload = payload }, originalOptions));
        return file;
    }

    private static (DataSnapshot Snapshot, ResearchRun Run) CreateDenseResearch()
    {
        var original = DemoData.Create();
        var dates = Enumerable.Range(0, 10_000).Select(index => new DateOnly(1990, 1, 1).AddDays(index)).ToArray();
        var snapshot = original with
        {
            SnapshotId = "store-dense-synthetic", ContentHash = "", DataAsOf = dates[^1],
            Instrument = original.Instrument with { ListedFrom = dates[0] },
            Calendar = original.Calendar with { Version = "store-synthetic-daily-v1", CoverageStart = dates[0], CoverageEnd = dates[^1], TradingDates = dates },
            ActionCoverage = original.ActionCoverage with { CoverageStart = dates[0], CoverageEnd = dates[^1] }, CorporateActions = [],
            Bars = dates.Select(date => new DailyBar { Date = date, Open = 100, High = 101, Low = 99, Close = 100, Volume = 1000, SourceId = original.SourceId }).ToArray()
        };
        snapshot = snapshot with { ContentHash = SnapshotFingerprint.Compute(snapshot) };
        ResearchCondition Condition(ResearchFeature feature, ComparisonOperator op, decimal value) =>
            new() { Feature = feature, Lookback = 120, Operator = op, Value = value };
        var definition = new ResearchDefinition
        {
            EventStart = dates[120], EventEnd = dates[^1], DataAsOf = dates[^1], SamplingPolicy = SamplingPolicy.EveryMatch,
            Conditions =
            [
                Condition(ResearchFeature.DistanceFromPriorLow, ComparisonOperator.GreaterThanOrEqual, 0),
                Condition(ResearchFeature.RelativeToPriorHigh, ComparisonOperator.LessThanOrEqual, 0),
                Condition(ResearchFeature.PriceChange, ComparisonOperator.Equal, 0),
                Condition(ResearchFeature.RelativeVolume, ComparisonOperator.GreaterThanOrEqual, 1),
                Condition(ResearchFeature.ConsecutiveAdvances, ComparisonOperator.Equal, 0),
                Condition(ResearchFeature.RangePosition, ComparisonOperator.Equal, 0.5m)
            ]
        };
        return (snapshot, new ResearchEngine().Run(snapshot, definition));
    }

    private sealed class ObservedList<T>(IReadOnlyList<T> items, Action onEnumeration) : IReadOnlyList<T>
    {
        public int Count => items.Count;
        public T this[int index] => items[index];
        public IEnumerator<T> GetEnumerator() { onEnumeration(); return items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens.Tests", Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
