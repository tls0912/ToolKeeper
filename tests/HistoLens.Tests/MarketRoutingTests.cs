using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

public sealed class MarketRoutingTests
{

    [Fact]
    public async Task SameCodeInDifferentMarketsKeepsSeparateFilesAndReusesOnlyItsOwnPrices()
    {
        using var directory = new TestDirectory();
        var start = new DateOnly(2025, 1, 1);
        var end = new DateOnly(2025, 1, 31);
        var listedPath = await directory.Store.SaveAsync(Fixture("TWSE", start, end, 100, "FinMind"));
        var otcPath = await directory.Store.SaveAsync(Fixture("TPEx", start, end, 200, "FinMind"));
        Assert.NotEqual(listedPath, otcPath);
        var provider = new RecordingProvider();
        var cache = new CachedMarketDataDownloader(provider, directory.Store);
        var listed = await cache.DownloadAsync(new("2330", start, end));
        var otc = await cache.DownloadAsync(new("2330", start, end, "TPEx"));
        Assert.Equal(listedPath, listed.Path);
        Assert.Equal(otcPath, otc.Path);
        Assert.All(listed.Download.Snapshot.Bars, bar => Assert.Equal(100m, bar.Close));
        Assert.All(otc.Download.Snapshot.Bars, bar => Assert.Equal(200m, bar.Close));
        Assert.Equal(31, otc.ReusedDays);
        Assert.Equal(0, otc.DownloadedRanges);
        Assert.Equal(0, provider.Calls);
        var inventory = await directory.Store.ListEntriesAsync();
        Assert.Equal(2, inventory.Count);
        Assert.Equal(new[] { "TPEx", "TWSE" }, inventory.Select(entry => entry.Market).Order(StringComparer.Ordinal));
        Assert.All(inventory, entry => Assert.Equal("FinMind", entry.SourceId));
    }

    [Theory]
    [InlineData("TWSE")]
    [InlineData("TPEx")]
    public async Task CacheRequestsMissingRangeForItsExplicitMarketAndRetainsExistingDates(string market)
    {
        using var directory = new TestDirectory();
        await directory.Store.SaveAsync(Fixture(market, new(2025, 1, 1), new(2025, 1, 31), sourceId: "FinMind"));
        var provider = new RecordingProvider();
        var cache = new CachedMarketDataDownloader(provider, directory.Store);
        var result = await cache.DownloadAsync(new("2330", new(2025, 1, 1), new(2025, 2, 28), market));
        var range = Assert.Single(provider.Ranges);
        Assert.Equal(new DateOnly(2025, 2, 1), range.Start);
        Assert.Equal(new DateOnly(2025, 2, 28), range.End);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(market, provider.Market);
        Assert.Equal(31, result.ReusedDays);
        Assert.Equal(1, result.DownloadedRanges);
        Assert.Equal(59, result.Download.Snapshot.Bars.Count);
        Assert.Equal(market, result.Download.Snapshot.Instrument.Market);
        Assert.Equal("FinMind", result.Download.Snapshot.SourceId);
        Assert.True(result.Download.Snapshot.Calendar.IsVerified);
        Assert.Null(result.Download.Snapshot.ComparabilityCoverage);
        Assert.StartsWith("finmind-cache-v2/", result.Download.Snapshot.DataVersion);
    }

    [Fact]
    public async Task WrongMarketIncomingDownloadCannotReplaceEitherExistingMarketFile()
    {
        using var directory = new TestDirectory();
        var start = new DateOnly(2025, 1, 1);
        var end = new DateOnly(2025, 1, 31);
        var twsePath = await directory.Store.SaveAsync(Fixture("TWSE", start, end));
        var tpexPath = await directory.Store.SaveAsync(Fixture("TPEx", start, end));
        var twseBytes = await File.ReadAllBytesAsync(twsePath);
        var tpexBytes = await File.ReadAllBytesAsync(tpexPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.UpdateAsync("2330", "TPEx",
            (_, _) => Task.FromResult(Fixture("TWSE", start, end, 999)), CancellationToken.None));
        Assert.Equal(twseBytes, await File.ReadAllBytesAsync(twsePath));
        Assert.Equal(tpexBytes, await File.ReadAllBytesAsync(tpexPath));
        Assert.Equal(2, (await directory.Store.ListAsync()).Count);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task DifferentStableIdentityWithinSameMarketCannotOverwriteExistingSecurity()
    {
        using var directory = new TestDirectory();
        var download = Fixture("TPEx", new(2025, 1, 1), new(2025, 1, 31));
        var path = await directory.Store.SaveAsync(download);
        var original = await File.ReadAllBytesAsync(path);
        var changed = Rehash(download with { Snapshot = download.Snapshot with
        {
            Instrument = download.Snapshot.Instrument with { InstrumentId = "TPEx:OTHER-LIFETIME" }
        } });
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.SaveAsync(changed));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Single(await directory.Store.ListAsync());
    }

    [Fact]
    public async Task LegacyTwseVersionOneEnvelopeStillLoadsWithoutChangingSnapshotHash()
    {
        using var directory = new TestDirectory();
        var download = Fixture("TWSE", new(2025, 1, 1), new(2025, 1, 31)) with { CacheCoverage = null };
        var path = Path.Combine(directory.Path, "legacy.twse-data.json");
        var payload = JsonSerializer.Serialize(new { FormatVersion = 1, SavedAtUtc = DateTimeOffset.UtcNow, Download = download });
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            FormatVersion = 1, Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), Payload = payload
        }));
        var loaded = await MarketDataStore.LoadAsync(path);
        Assert.Equal(download.Snapshot.ContentHash, loaded.Snapshot.ContentHash);
        Assert.Equal(JsonSerializer.Serialize(download), JsonSerializer.Serialize(loaded));
        var entry = Assert.Single(await directory.Store.ListEntriesAsync());
        Assert.Equal("TWSE", entry.Market);
        Assert.Equal(download.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(entry.Path)).Snapshot.ContentHash);
    }

    [Theory]
    [InlineData("TWSE", "TPEx")]
    [InlineData("TPEx", "TWSE")]
    public async Task SourceAndInstrumentMarketMustAgree(string source, string market)
    {
        using var directory = new TestDirectory();
        var download = Fixture(market, new(2025, 1, 1), new(2025, 1, 31));
        download = Rehash(download with { Snapshot = download.Snapshot with { SourceId = source } });
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.SaveAsync(download));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task TpexConflictKeepsOldPricesAndUsesActualSourceWithoutInventingComparisonCoverage()
    {
        using var directory = new TestDirectory();
        var first = Fixture("TPEx", new(2025, 1, 1), new(2025, 1, 31), 100);
        await directory.Store.SaveAsync(first);
        var path = await directory.Store.SaveAsync(Fixture("TPEx", new(2025, 1, 1), new(2025, 1, 31), 200));
        var merged = await MarketDataStore.LoadAsync(path);
        Assert.All(merged.Snapshot.Bars, bar => Assert.Equal(100m, bar.Close));
        Assert.StartsWith("tpex-2330-cache-", merged.Snapshot.SnapshotId);
        Assert.Null(merged.Snapshot.ComparabilityCoverage);
        Assert.False(merged.Snapshot.ActionCoverage.IsVerified);
        Assert.NotEmpty(merged.Snapshot.CorporateActions);
        Assert.All(merged.Snapshot.CorporateActions, action => Assert.Equal("TPEx/cache", action.SourceId));
    }

    [Fact]
    public async Task UnknownTpexActionCoverageRemainsUnknownWhileRawSimilarityScanCompletes()
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture("TPEx", new(2025, 1, 1), new(2025, 1, 31)));
        var snapshot = (await MarketDataStore.LoadAsync(path)).Snapshot;
        var run = new SimilarityEngine().Run(snapshot, new()
        {
            Lookback = 5, ScanStart = snapshot.Calendar.CoverageStart, ScanEnd = snapshot.DataAsOf,
            DataAsOf = snapshot.DataAsOf, MinimumSimilarity = 0
        });
        Assert.False(snapshot.ActionCoverage.IsVerified);
        Assert.Null(snapshot.ComparabilityCoverage);
        Assert.True(run.IsAllowed);
        Assert.True(run.CandidateCount > 0);
        Assert.NotEmpty(run.Matches);
        Assert.Contains(run.Diagnostics, issue => issue.Code == "CorporateActionCoverageUnknown" && !issue.BlocksResearch);
        Assert.Contains(run.Reference!.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
    }

    private static HistoricalDataDownload Fixture(string market, DateOnly start, DateOnly end, decimal close = 100, string? sourceId = null)
    {
        sourceId ??= market;
        // Explicit fabricated market dates and prices: this fixture makes no claim about actual sessions or prices.
        var dates = Enumerable.Range(start.DayNumber, end.DayNumber - start.DayNumber + 1).Select(DateOnly.FromDayNumber).ToArray();
        var snapshot = new DataSnapshot
        {
            SourceId = sourceId, DataVersion = "offline-market-routing", RetrievedAtUtc = DateTimeOffset.UtcNow, DataAsOf = end,
            Instrument = new() { InstrumentId = market + ":OFFLINE-TEST", Market = market, Code = "2330", Name = "Offline test fixture" },
            Calendar = new() { Market = market, Version = "offline-calendar", IsVerified = true, CoverageStart = start, CoverageEnd = end, TradingDates = dates },
            ActionCoverage = new() { SourceId = sourceId, Version = "offline-actions-unknown", IsVerified = false, CoverageStart = start, CoverageEnd = end },
            Bars = dates.Select(date => new DailyBar { Date = date, Open = close, High = close, Low = close, Close = close, Volume = 1000, SourceId = sourceId }).ToArray()
        };
        return Rehash(new(snapshot, SnapshotValidator.Validate(snapshot))
        {
            CacheCoverage = new() { CheckedPriceRanges = [new() { Start = start, End = end }], VerifiedCalendarRanges = [new() { Start = start, End = end }] }
        });
    }

    private static HistoricalDataDownload Rehash(HistoricalDataDownload download)
    {
        var hash = SnapshotFingerprint.Compute(download.Snapshot);
        return download with { Snapshot = download.Snapshot with { SnapshotId = hash, ContentHash = hash } };
    }

    private sealed class RecordingProvider : IRangeHistoricalDataProvider, IHistoricalDataSource
    {
        public string DataSourceId => "FinMind";
        public int Calls { get; private set; }
        public string? Market { get; private set; }
        public IReadOnlyList<DateRange> Ranges { get; private set; } = [];
        public HistoricalDataRequest ValidateRequest(HistoricalDataRequest request)
        {
            if (request.Market is not ("TWSE" or "TPEx"))
                throw new ArgumentException("Unknown offline fixture market.", nameof(request));
            return request;
        }
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            DownloadMissingRangesAsync(request, [new() { Start = request.Start, End = request.End }], progress, cancellationToken);
        public Task<HistoricalDataDownload> DownloadMissingRangesAsync(HistoricalDataRequest request,
            IReadOnlyList<DateRange> ranges, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            ValidateRequest(request);
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Market = request.Market;
            Ranges = ranges;
            return Task.FromResult(MarketDataMerge.Merge(ranges.Select(range =>
                Fixture(request.Market, range.Start, range.End, sourceId: "FinMind"))));
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens-routing-tests-" + Guid.NewGuid().ToString("N"));
        public MarketDataStore Store => new(Path);
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
