using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

public sealed class CachedMarketDataTests
{
    private static HistoricalDataRequest January => new("2330", new(2025, 1, 2), new(2025, 1, 6));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationDuringDownloadRetainsPreviousFileExactly(bool cancel)
    {
        using var directory = new TestDirectory();
        var initial = await new CachedMarketDataDownloader(new OfflineRangeProvider(), directory.Store).DownloadAsync(January);
        var oldBytes = await File.ReadAllBytesAsync(initial.Path);
        using var cancellation = new CancellationTokenSource();
        var failing = new OfflineRangeProvider
        {
            BeforeDownload = () =>
            {
                if (cancel) cancellation.Cancel();
                else throw new HttpRequestException("Offline failure fixture");
            }
        };
        var cache = new CachedMarketDataDownloader(failing, directory.Store);
        var request = new HistoricalDataRequest("2330", new(2025, 2, 3), new(2025, 2, 5));
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.DownloadAsync(request, cancellationToken: cancellation.Token));
        else await Assert.ThrowsAsync<HttpRequestException>(() => cache.DownloadAsync(request));
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(initial.Path));
        Assert.Single(await directory.Store.ListAsync());
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CorruptExistingFileBlocksBeforeAnySourceRequest()
    {
        using var directory = new TestDirectory();
        var initial = await new CachedMarketDataDownloader(new OfflineRangeProvider(), directory.Store).DownloadAsync(January);
        var text = await File.ReadAllTextAsync(initial.Path);
        var node = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        node["Checksum"] = "corrupted";
        await File.WriteAllTextAsync(initial.Path, node.ToJsonString());
        var nextProvider = new OfflineRangeProvider();
        await Assert.ThrowsAsync<InvalidDataException>(() => new CachedMarketDataDownloader(nextProvider, directory.Store).DownloadAsync(January));
        Assert.Equal(0, nextProvider.Calls);
        Assert.Single(Directory.GetFiles(directory.Path, "*.market-data.json"));
    }

    [Fact]
    public async Task LegacyV1FilesConsolidateAndKeepRecoverableOriginalsAndFingerprintCompatibility()
    {
        using var directory = new TestDirectory();
        var first = Fixture(January.Start, January.End);
        var second = Fixture(new(2025, 2, 3), new(2025, 2, 5));
        var firstPath = await WriteLegacy(directory.Path, "old-a", first);
        await WriteLegacy(directory.Path, "old-b", second);
        Assert.Equal(first.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(firstPath)).Snapshot.ContentHash);
        var path = Assert.Single(await directory.Store.ListAsync());
        Assert.Single(Directory.GetFiles(directory.Path, "*.market-data.json"));
        Assert.Equal(2, Directory.GetFiles(System.IO.Path.Combine(directory.Path, "legacy")).Length);
        var merged = await MarketDataStore.LoadAsync(path);
        Assert.Equal(first.Snapshot.Bars.Count + second.Snapshot.Bars.Count, merged.Snapshot.Bars.Count);
        Assert.False(merged.Snapshot.Calendar.IsVerified);
        Assert.Equal(SnapshotFingerprint.Compute(merged.Snapshot), merged.Snapshot.ContentHash);
        Assert.Equal(first.Snapshot.Bars, merged.Snapshot.Bars.Take(first.Snapshot.Bars.Count));
    }

    [Fact]
    public async Task FailedAtomicReplacementKeepsOriginalFileAndRemovesTemporaryCandidate()
    {
        using var directory = new TestDirectory();
        var original = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        var path = await directory.Store.SaveAsync(original);
        var before = await File.ReadAllBytesAsync(path);
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => directory.Store.SaveAsync(Fixture(new(2025, 2, 3), new(2025, 2, 5))));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString() ?? "Expected replacement to fail while another reader denies delete sharing.");
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        Assert.Equal(original.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(path)).Snapshot.ContentHash);
    }

    [Fact]
    public async Task DifferentStockAndStableIdentityCannotMix()
    {
        using var directory = new TestDirectory();
        var first = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        var other = Rehash(first with { Snapshot = first.Snapshot with
        { Instrument = first.Snapshot.Instrument with { Code = "2317", InstrumentId = "TWSE:OTHER" } } });
        var firstPath = await directory.Store.SaveAsync(first);
        var otherPath = await directory.Store.SaveAsync(other);
        Assert.NotEqual(firstPath, otherPath);
        Assert.Equal(2, (await directory.Store.ListAsync()).Count);
        var differentIdentity = Rehash(first with { Snapshot = first.Snapshot with
        { Instrument = first.Snapshot.Instrument with { InstrumentId = "TWSE:REUSED-CODE" } } });
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.SaveAsync(differentIdentity));
        Assert.Equal(first.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(firstPath)).Snapshot.ContentHash);
    }

    [Fact]
    public async Task ConcurrentStoreInstancesSerializeAndKeepAllDates()
    {
        using var directory = new TestDirectory();
        var first = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        var second = Fixture(new(2025, 2, 3), new(2025, 2, 5));
        await Task.WhenAll(directory.Store.SaveAsync(first), new MarketDataStore(directory.Path).SaveAsync(second));
        var merged = await MarketDataStore.LoadAsync(Assert.Single(await directory.Store.ListAsync()));
        Assert.Equal(first.Snapshot.Bars.Concat(second.Snapshot.Bars).OrderBy(bar => bar.Date), merged.Snapshot.Bars);
    }

    [Fact]
    public async Task ConcurrentIdenticalDownloadsCheckAgainAfterAcquiringLock()
    {
        using var directory = new TestDirectory();
        var provider = new OfflineRangeProvider();
        var first = new CachedMarketDataDownloader(provider, directory.Store);
        var second = new CachedMarketDataDownloader(provider, new MarketDataStore(directory.Path));
        var results = await Task.WhenAll(first.DownloadAsync(January), second.DownloadAsync(January));
        Assert.Equal(1, provider.Calls);
        Assert.Equal(results[0].Path, results[1].Path);
        Assert.Single(results, result => result.DownloadedRanges == 0);
    }

    [Fact]
    public async Task NewPartialEvidenceCannotEraseOlderBlocksActionsOrUnknownIntervals()
    {
        using var directory = new TestDirectory();
        var first = Fixture(new(2025, 1, 2), new(2025, 1, 6));
        first = Rehash(first with
        {
            Snapshot = first.Snapshot with
            {
                CorporateActions = [new() { EffectiveDate = new(2025, 1, 3), Kind = "NonComparablePrice", SourceId = "TWSE/STOCK_DAY" }],
                ComparabilityCoverage = first.Snapshot.ComparabilityCoverage! with { Gaps = [new() { Start = new(2025, 1, 4), End = new(2025, 1, 4) }] }
            },
            Diagnostics = [new() { Code = "ProviderBlocked", Message = "Unresolved source evidence", BlocksResearch = true }]
        });
        await directory.Store.SaveAsync(first);
        var later = Fixture(new(2025, 3, 2), new(2025, 3, 6));
        var path = await directory.Store.SaveAsync(later);
        var merged = await MarketDataStore.LoadAsync(path);
        Assert.Contains(merged.Diagnostics, issue => issue.Code == "ProviderBlocked" && issue.BlocksResearch);
        Assert.Contains(merged.Snapshot.CorporateActions, action => action.Kind == "NonComparablePrice");
        Assert.True(InGap(merged.Snapshot.ComparabilityCoverage!, new(2025, 1, 4)));
        Assert.True(InGap(merged.Snapshot.ComparabilityCoverage!, new(2025, 2, 5)));
        Assert.False(merged.Snapshot.ActionCoverage.IsVerified);
        Assert.Contains(merged.Snapshot.ActionCoverage.Gaps, range => range.Start <= new DateOnly(2025, 2, 5) && range.End >= new DateOnly(2025, 2, 5));
    }

    internal static HistoricalDataDownload Fixture(DateOnly start, DateOnly end)
    {
        var dates = Enumerable.Range(start.DayNumber, end.DayNumber - start.DayNumber + 1).Select(DateOnly.FromDayNumber).ToArray();
        return Rehash(new(new()
        {
            Instrument = new() { InstrumentId = "TWSE:CACHE-TEST", Code = "2330", Name = "Cache test fixture", Market = "TWSE" },
            SourceId = "TWSE", DataVersion = "offline-test", RetrievedAtUtc = DateTimeOffset.UtcNow, DataAsOf = end,
            Calendar = new() { IsVerified = true, Market = "TWSE", Version = "offline-calendar", CoverageStart = start, CoverageEnd = end, TradingDates = dates },
            ActionCoverage = new() { IsVerified = false, SourceId = "TWSE", Version = "offline-action", CoverageStart = start, CoverageEnd = end },
            ComparabilityCoverage = new() { IsVerified = true, SourceId = "TWSE/STOCK_DAY", Version = "offline-comparison", CoverageStart = start, CoverageEnd = end },
            Bars = dates.Select(date => new DailyBar { Date = date, Open = 100, High = 101, Low = 99, Close = 100, Volume = 1000, SourceId = "TWSE" }).ToArray()
        }, []));
    }

    private static HistoricalDataDownload Rehash(HistoricalDataDownload download)
    {
        var hash = SnapshotFingerprint.Compute(download.Snapshot);
        return download with { Snapshot = download.Snapshot with { ContentHash = hash, SnapshotId = hash } };
    }
    private static bool InGap(PriceComparisonCoverage coverage, DateOnly date) => coverage.Gaps.Any(gap => gap.Start <= date && gap.End >= date);
    private static async Task<string> WriteLegacy(string directory, string name, HistoricalDataDownload download)
    {
        var payload = JsonSerializer.Serialize(new { FormatVersion = 1, SavedAtUtc = DateTimeOffset.UtcNow, Download = download },
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var path = System.IO.Path.Combine(directory, name + ".twse-data.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { FormatVersion = 1, Checksum = checksum, Payload = payload }));
        return path;
    }
    /// <summary>Explicit invented calendar and prices, with no HTTP or claim of actual market coverage.</summary>
    private sealed class OfflineRangeProvider : IRangeHistoricalDataProvider, IHistoricalDataSource
    {
        private int _calls;
        public string DataSourceId => "FinMind";
        public int Calls => _calls;
        public Action? BeforeDownload { get; init; }
        public HistoricalDataRequest ValidateRequest(HistoricalDataRequest request) => request;
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            DownloadMissingRangesAsync(request, [new() { Start = request.Start, End = request.End }], progress, cancellationToken);
        public Task<HistoricalDataDownload> DownloadMissingRangesAsync(HistoricalDataRequest request,
            IReadOnlyList<DateRange> ranges, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            BeforeDownload?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            var parts = ranges.Select(range =>
            {
                var legacy = Fixture(range.Start, range.End);
                var snapshot = legacy.Snapshot with
                {
                    SourceId = "FinMind", ComparabilityCoverage = null,
                    Bars = legacy.Snapshot.Bars.Select(bar => bar with { SourceId = "FinMind" }).ToArray(),
                    ActionCoverage = legacy.Snapshot.ActionCoverage with { SourceId = "FinMind" }
                };
                return Rehash(new(snapshot, [])
                {
                    CacheCoverage = new()
                    {
                        CheckedPriceRanges = [range], VerifiedCalendarRanges = [range]
                    }
                });
            });
            return Task.FromResult(MarketDataMerge.Merge(parts));
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens-cache-tests-" + Guid.NewGuid().ToString("N"));
        public MarketDataStore Store => new(Path);
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
