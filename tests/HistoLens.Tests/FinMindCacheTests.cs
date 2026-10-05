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

public sealed class FinMindCacheTests
{
    private static HistoricalDataRequest Request => new("2330", new(2025, 1, 2), new(2025, 1, 10));

    [Theory]
    [InlineData("TWSE")]
    [InlineData("TPEx")]
    public async Task OfficialAndFinMindCoexistAndDeletionOnlyRemovesSelectedSource(string market)
    {
        using var directory = new TestDirectory();
        var official = Fixture(Request with { Market = market }, market, 100);
        var finMind = Fixture(Request with { Market = market }, close: 200);
        var officialPath = await directory.Store.SaveAsync(official);
        var finMindPath = await directory.Store.SaveAsync(finMind);
        Assert.NotEqual(officialPath, finMindPath);
        Assert.StartsWith(market + "-2330-", Path.GetFileName(officialPath));
        Assert.StartsWith("FinMind-" + market + "-2330-", Path.GetFileName(finMindPath));
        var inventory = await directory.Store.ListEntriesAsync();
        Assert.Equal(2, inventory.Count);
        Assert.Contains(inventory, entry => entry.SourceId == market && entry.Path == officialPath);
        Assert.Contains(inventory, entry => entry.SourceId == "FinMind" && entry.Path == finMindPath);
        await directory.Store.DeleteAsync(finMindPath);
        Assert.Equal(officialPath, Assert.Single(await directory.Store.ListAsync()));
        Assert.Equal(official.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(officialPath)).Snapshot.ContentHash);
    }

    [Fact]
    public async Task FinMindCacheDoesNotReuseOrOverwriteOfficialPrices()
    {
        using var directory = new TestDirectory();
        var officialPath = await directory.Store.SaveAsync(Fixture(Request, "TWSE", 100));
        var before = await File.ReadAllBytesAsync(officialPath);
        var provider = new RecordingProvider { Close = 200 };
        var result = await new CachedMarketDataDownloader(provider, directory.Store).DownloadAsync(Request);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(0, result.ReusedDays);
        Assert.All(result.Download.Snapshot.Bars, bar => Assert.Equal(200m, bar.Close));
        Assert.Equal(before, await File.ReadAllBytesAsync(officialPath));
        Assert.Equal(2, (await directory.Store.ListAsync()).Count);
    }

    [Fact]
    public async Task FullyCachedAndNarrowerRangesMakeZeroRequestsAndKeepSavedBytes()
    {
        using var directory = new TestDirectory();
        var provider = new RecordingProvider();
        var cache = new CachedMarketDataDownloader(provider, directory.Store);
        var first = await cache.DownloadAsync(Request);
        var before = await File.ReadAllBytesAsync(first.Path);
        provider.Reset();
        var repeat = await cache.DownloadAsync(Request);
        var weekend = await cache.DownloadAsync(Request with { Start = new(2025, 1, 4), End = new(2025, 1, 5) });
        Assert.Equal(0, provider.Calls);
        Assert.Equal(9, repeat.ReusedDays);
        Assert.Equal(2, weekend.ReusedDays);
        Assert.True(repeat.UsesDateRanges);
        Assert.Equal(0, repeat.DownloadedRanges);
        Assert.Equal(0, repeat.DownloadedMonths);
        Assert.Equal(before, await File.ReadAllBytesAsync(first.Path));
        Assert.DoesNotContain(repeat.Download.Snapshot.Bars, bar => bar.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    [Fact]
    public async Task ExpandingDatesOnlyFetchesExactHeadAndTailWithoutWholeMonthExpansion()
    {
        using var directory = new TestDirectory();
        var provider = new RecordingProvider();
        var cache = new CachedMarketDataDownloader(provider, directory.Store);
        var first = await cache.DownloadAsync(Request with { Start = new(2025, 1, 6), End = new(2025, 1, 8) });
        provider.Reset();
        var expanded = await cache.DownloadAsync(Request);
        Assert.Equal(new[] { Range(new(2025, 1, 2), new(2025, 1, 5)), Range(new(2025, 1, 9), new(2025, 1, 10)) }, provider.Ranges);
        Assert.Equal(2, expanded.DownloadedRanges);
        Assert.Equal(3, expanded.ReusedDays);
        Assert.Equal(Request.Start, expanded.Download.Snapshot.Calendar.CoverageStart);
        Assert.Equal(Request.End, expanded.Download.Snapshot.DataAsOf);
        Assert.Equal(first.Download.Snapshot.Bars, expanded.Download.Snapshot.Bars.Where(bar => bar.Date >= new DateOnly(2025, 1, 6) && bar.Date <= new DateOnly(2025, 1, 8)));
        Assert.True(expanded.Download.Snapshot.Calendar.IsVerified);
    }

    [Fact]
    public async Task MissingTradingDateInsideMinMaxIsFetchedExactlyAndValidPricesSurvive()
    {
        using var directory = new TestDirectory();
        var missing = new DateOnly(2025, 1, 7);
        var initial = Fixture(Request);
        initial = Rehash(initial with { Snapshot = initial.Snapshot with { Bars = initial.Snapshot.Bars.Where(bar => bar.Date != missing).ToArray() } });
        await directory.Store.SaveAsync(initial);
        var provider = new RecordingProvider { Close = 200 };
        var repaired = await new CachedMarketDataDownloader(provider, directory.Store).DownloadAsync(Request);
        Assert.Equal(new[] { Range(missing, missing) }, provider.Ranges);
        Assert.Equal(1, repaired.AddedBars);
        Assert.Equal(0, repaired.RepairedBars);
        Assert.All(repaired.Download.Snapshot.Bars.Where(bar => bar.Date != missing), bar => Assert.Equal(100m, bar.Close));
        Assert.Equal(200m, Assert.Single(repaired.Download.Snapshot.Bars, bar => bar.Date == missing).Close);
        Assert.DoesNotContain(repaired.Download.Diagnostics, issue => issue.Code == "MissingBar");
    }

    [Fact]
    public async Task UnverifiedMiddleCalendarRangeMustBeDownloadedEvenWhenBarsExist()
    {
        using var directory = new TestDirectory();
        var initial = Fixture(Request);
        initial = Rehash(initial with
        {
            Snapshot = initial.Snapshot with { Calendar = initial.Snapshot.Calendar with { IsVerified = false } },
            CacheCoverage = initial.CacheCoverage! with
            { VerifiedCalendarRanges = [Range(Request.Start, new(2025, 1, 6)), Range(new(2025, 1, 8), Request.End)] }
        });
        await directory.Store.SaveAsync(initial);
        var provider = new RecordingProvider();
        var result = await new CachedMarketDataDownloader(provider, directory.Store).DownloadAsync(Request);
        Assert.Equal(new[] { Range(new(2025, 1, 7), new(2025, 1, 7)) }, provider.Ranges);
        Assert.True(result.Download.Snapshot.Calendar.IsVerified);
        Assert.Equal(initial.Snapshot.Bars, result.Download.Snapshot.Bars);
    }

    [Fact]
    public async Task SeparateCachedRangesDoNotHideAnInteriorGap()
    {
        using var directory = new TestDirectory();
        await directory.Store.SaveAsync(Fixture(Request with { End = new(2025, 1, 3) }));
        await directory.Store.SaveAsync(Fixture(Request with { Start = new(2025, 1, 9) }));
        var provider = new RecordingProvider();
        var result = await new CachedMarketDataDownloader(provider, directory.Store).DownloadAsync(Request);
        Assert.Equal(new[] { Range(new(2025, 1, 4), new(2025, 1, 8)) }, provider.Ranges);
        Assert.True(result.Download.Snapshot.Calendar.IsVerified);
        Assert.DoesNotContain(result.Download.Diagnostics, issue => issue.Code == "CacheCalendarGap");
    }

    [Fact]
    public async Task ConfirmedNoPriceRowRemainsUnknownButDoesNotRepeatDownloadAndLaterValidBarRepairsIt()
    {
        using var directory = new TestDirectory();
        var noPrice = new DateOnly(2025, 1, 7);
        var initial = Fixture(Request);
        initial = Rehash(initial with
        {
            Snapshot = initial.Snapshot with { Bars = initial.Snapshot.Bars.Select(bar => bar.Date != noPrice ? bar :
                bar with { Open = null, High = null, Low = null, Close = null, Volume = 0 }).ToArray() },
            CacheCoverage = initial.CacheCoverage! with { ConfirmedNoPriceDates = [noPrice] }
        });
        var path = await directory.Store.SaveAsync(initial);
        var provider = new RecordingProvider();
        var result = await new CachedMarketDataDownloader(provider, directory.Store).DownloadAsync(Request);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(TradingStatus.Traded, Assert.Single(result.Download.Snapshot.Bars, bar => bar.Date == noPrice).Status);
        Assert.Contains(result.Download.Diagnostics, issue => issue.Code == "MissingPrice" && issue.Date == noPrice);
        await directory.Store.SaveAsync(Fixture(Request with { Start = noPrice, End = noPrice }));
        var loaded = await MarketDataStore.LoadAsync(path);
        Assert.Empty(loaded.CacheCoverage!.ConfirmedNoPriceDates!);
        Assert.Equal(100m, Assert.Single(loaded.Snapshot.Bars, bar => bar.Date == noPrice).Close);
        Assert.DoesNotContain(loaded.Diagnostics, issue => issue.Code == "MissingPrice" && issue.Date == noPrice);
    }

    [Theory]
    [InlineData("missing-row")]
    [InlineData("valid-row")]
    [InlineData("unqueried-range")]
    [InlineData("duplicate-date")]
    public async Task InvalidConfirmedNoPriceMetadataIsRejected(string mutation)
    {
        using var directory = new TestDirectory();
        var date = new DateOnly(2025, 1, 7);
        var initial = Fixture(Request);
        var noPrice = initial.Snapshot.Bars.Select(bar => bar.Date != date ? bar : bar with { Open = null, High = null, Low = null, Close = null }).ToArray();
        initial = Rehash(initial with
        {
            Snapshot = initial.Snapshot with { Bars = mutation switch
            { "missing-row" => noPrice.Where(bar => bar.Date != date).ToArray(), "valid-row" => initial.Snapshot.Bars, _ => noPrice } },
            CacheCoverage = initial.CacheCoverage! with
            {
                ConfirmedNoPriceDates = mutation == "duplicate-date" ? [date, date] : [date],
                CheckedPriceRanges = mutation == "unqueried-range" ? [Range(Request.Start, new(2025, 1, 6))] : initial.CacheCoverage.CheckedPriceRanges
            }
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.SaveAsync(initial));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOrCancellationPreservesExistingFinMindFileExactly(bool cancel)
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture(Request));
        var before = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        var provider = new RecordingProvider
        {
            OnDownload = (_, _) =>
            {
                if (!cancel) throw new HttpRequestException("Offline failure fixture");
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
        };
        var cache = new CachedMarketDataDownloader(provider, directory.Store);
        var expanded = Request with { End = new(2025, 1, 13) };
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.DownloadAsync(expanded, cancellationToken: cancellation.Token));
        else await Assert.ThrowsAsync<HttpRequestException>(() => cache.DownloadAsync(expanded));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task WrongIncomingSourceAndStableIdentityCannotReplaceFinMindFile()
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture(Request));
        var before = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.UpdateAsync("2330", "TWSE", "FinMind",
            (_, _) => Task.FromResult(Fixture(Request, "TWSE")), CancellationToken.None));
        var differentIdentity = Rehash(Fixture(Request) with { Snapshot = Fixture(Request).Snapshot with
        { Instrument = Fixture(Request).Snapshot.Instrument with { InstrumentId = "TWSE:REUSED-CODE" } } });
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.SaveAsync(differentIdentity));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task RawSourceEvidenceIsSeparateAndDeduplicatedWithoutChangingSnapshotFingerprint()
    {
        using var directory = new TestDirectory();
        var first = Fixture(Request);
        var path = await directory.Store.SaveAsync(first);
        var secondEvidence = Evidence("TaiwanStockTradingDate", "{\"status\":200,\"data\":[{\"date\":\"2025-01-02\"}]}", 1);
        await directory.Store.SaveAsync(first with { SourceEvidence = [first.SourceEvidence![0], secondEvidence] });
        var loaded = await MarketDataStore.LoadAsync(path);
        Assert.Equal(2, loaded.SourceEvidence!.Count);
        Assert.Contains(secondEvidence, loaded.SourceEvidence);
        Assert.Equal(SnapshotFingerprint.Compute(loaded.Snapshot), loaded.Snapshot.ContentHash);
        Assert.Equal(first.Snapshot.Bars, loaded.Snapshot.Bars);
        // Same normalized payload must still preserve additional original response evidence.
        Assert.Equal(first.Snapshot.ContentHash, loaded.Snapshot.ContentHash);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("token-url")]
    [InlineData("foreign-host")]
    [InlineData("http")]
    [InlineData("row-count")]
    [InlineData("dataset")]
    [InlineData("failed-response")]
    [InlineData("malformed-json")]
    public async Task InvalidRawSourceEvidenceIsRejectedBeforeWriting(string mutation)
    {
        using var directory = new TestDirectory();
        var initial = Fixture(Request);
        var original = initial.SourceEvidence![0];
        var evidence = mutation switch
        {
            "hash" => original with { Sha256 = new string('0', 64) },
            "token-url" => original with { ResourceUrl = original.ResourceUrl + "&token=must-not-store" },
            "foreign-host" => original with { ResourceUrl = original.ResourceUrl.Replace("api.finmindtrade.com", "example.com", StringComparison.Ordinal) },
            "http" => original with { ResourceUrl = original.ResourceUrl.Replace("https:", "http:", StringComparison.Ordinal) },
            "row-count" => original with { RowCount = original.RowCount + 1 },
            "dataset" => original with { Dataset = "TaiwanStockInfo" },
            "failed-response" => Evidence("TaiwanStockPrice", "{\"status\":402,\"data\":[]}", 0),
            "malformed-json" => Evidence("TaiwanStockPrice", "not JSON", 0),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.SaveAsync(initial with { SourceEvidence = [evidence] }));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task VersionOneOfficialFileStillLoadsAndConsolidatesBesideFinMind()
    {
        using var directory = new TestDirectory();
        var old = Fixture(Request, "TWSE") with { CacheCoverage = null, SourceEvidence = null };
        var payload = JsonSerializer.Serialize(new { FormatVersion = 1, SavedAtUtc = DateTimeOffset.UtcNow, Download = old },
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        var legacyPath = Path.Combine(directory.Path, "legacy.twse-data.json");
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new
        { FormatVersion = 1, Payload = payload, Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))) }));
        var loaded = await MarketDataStore.LoadAsync(legacyPath);
        Assert.Equal(old.Snapshot.ContentHash, loaded.Snapshot.ContentHash);
        await directory.Store.SaveAsync(Fixture(Request));
        var entries = await directory.Store.ListEntriesAsync();
        Assert.Equal(2, entries.Count);
        var official = Assert.Single(entries, entry => entry.SourceId == "TWSE");
        Assert.Equal(old.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(official.Path)).Snapshot.ContentHash);
        Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "legacy")));
    }

    private static HistoricalDataDownload Fixture(HistoricalDataRequest request, string sourceId = "FinMind", decimal close = 100)
    {
        // This is an explicitly generated offline calendar fixture, not production holiday inference.
        var dates = Enumerable.Range(request.Start.DayNumber, request.End.DayNumber - request.Start.DayNumber + 1)
            .Select(DateOnly.FromDayNumber).Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToArray();
        var snapshot = new DataSnapshot
        {
            Instrument = new() { InstrumentId = request.Market + ":TEST-ISIN", Code = request.Code, Name = "Offline cache fixture", Market = request.Market },
            SourceId = sourceId, DataVersion = "offline-cache-test", RetrievedAtUtc = new(2025, 2, 1, 0, 0, 0, TimeSpan.Zero), DataAsOf = request.End,
            Calendar = new() { Market = request.Market, Version = "offline-calendar", IsVerified = true, CoverageStart = request.Start, CoverageEnd = request.End, TradingDates = dates },
            ActionCoverage = new() { SourceId = sourceId, Version = "offline-unknown-actions", CoverageStart = request.Start, CoverageEnd = request.End },
            Bars = dates.Select(date => new DailyBar { Date = date, Open = close, High = close, Low = close, Close = close, Volume = 1000, SourceId = sourceId }).ToArray()
        };
        var raw = JsonSerializer.Serialize(new { status = 200, data = dates.Select(date => new { date = date.ToString("yyyy-MM-dd"), stock_id = request.Code, close }) });
        return Rehash(new(snapshot, [])
        {
            CacheCoverage = new() { CheckedPriceRanges = [Range(request.Start, request.End)], VerifiedCalendarRanges = [Range(request.Start, request.End)] },
            SourceEvidence = sourceId == "FinMind" ? [Evidence("TaiwanStockPrice", raw, dates.Length)] : null
        });
    }

    private static SourceResponseEvidence Evidence(string dataset, string raw, int count) => new(
        "https://api.finmindtrade.com/api/v4/data?dataset=" + dataset, dataset,
        new(2025, 2, 1, 0, 0, 0, TimeSpan.Zero), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), count, raw);

    private static HistoricalDataDownload Rehash(HistoricalDataDownload download)
    {
        var hash = SnapshotFingerprint.Compute(download.Snapshot);
        var snapshot = download.Snapshot with { SnapshotId = hash, ContentHash = hash };
        return download with { Snapshot = snapshot, Diagnostics = SnapshotValidator.Validate(snapshot) };
    }

    private static DateRange Range(DateOnly start, DateOnly end) => new() { Start = start, End = end };

    private sealed class RecordingProvider : IRangeHistoricalDataProvider, IHistoricalDataSource
    {
        public string DataSourceId => "FinMind";
        public int Calls { get; private set; }
        public decimal Close { get; init; } = 100;
        public IReadOnlyList<DateRange> Ranges { get; private set; } = [];
        public Func<HistoricalDataRequest, IReadOnlyList<DateRange>, HistoricalDataDownload>? OnDownload { get; init; }
        public void Reset() { Calls = 0; Ranges = []; }
        public HistoricalDataRequest ValidateRequest(HistoricalDataRequest request) => request;
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Cache must call exact missing-range download.");
        public Task<HistoricalDataDownload> DownloadMissingRangesAsync(HistoricalDataRequest request,
            IReadOnlyList<DateRange> ranges, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Ranges = ranges;
            return Task.FromResult(OnDownload?.Invoke(request, ranges) ?? MarketDataMerge.Merge(ranges.Select(range =>
                Fixture(request with { Start = range.Start, End = range.End }, close: Close))));
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens-finmind-cache-tests-" + Guid.NewGuid().ToString("N"));
        public MarketDataStore Store => new(Path);
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
