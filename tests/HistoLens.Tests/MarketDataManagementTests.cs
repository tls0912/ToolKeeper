using System.Diagnostics;
using System.IO;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

public sealed class MarketDataManagementTests
{
    [Fact]
    public async Task InventoryShowsSavedRangeAndActualPriceDatesAfterExpansionForEachStock()
    {
        using var directory = new TestDirectory();
        var first = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        var firstPath = await directory.Store.SaveAsync(first);
        var expandedPath = await directory.Store.SaveAsync(Fixture(new(2025, 1, 4), new(2025, 1, 6)));
        var other = Rehash(first with { Snapshot = first.Snapshot with
        {
            Instrument = first.Snapshot.Instrument with { Code = "2317", InstrumentId = "TWSE:OTHER", Name = "Other offline fixture" }
        } });
        var otherPath = await directory.Store.SaveAsync(other);
        var entries = await directory.Store.ListEntriesAsync();
        Assert.Equal(2, entries.Count);
        var expanded = Assert.Single(entries, entry => entry.Code == "2330");
        Assert.Equal(firstPath, expandedPath);
        Assert.Equal(firstPath, expanded.Path);
        Assert.Equal(first.Snapshot.Instrument.Name, expanded.Name);
        Assert.Equal(new DateOnly(2025, 1, 2), expanded.CoverageStart);
        Assert.Equal(new DateOnly(2025, 1, 6), expanded.CoverageEnd);
        Assert.Equal(expanded.CoverageStart, expanded.FirstPriceDate);
        Assert.Equal(expanded.CoverageEnd, expanded.LastPriceDate);
        Assert.Equal(5, expanded.BarCount);
        Assert.False(expanded.BlocksResearch);
        var second = Assert.Single(entries, entry => entry.Code == "2317");
        Assert.Equal(otherPath, second.Path);
        Assert.Equal("Other offline fixture", second.Name);
        Assert.Equal(2, second.BarCount);
        Assert.Equal(await directory.Store.ListAsync(), entries.Select(entry => entry.Path));
    }

    [Fact]
    public async Task SavedCoverageDoesNotPretendMissingAndNoTradingDatesHavePrices()
    {
        using var directory = new TestDirectory();
        var download = Fixture(new(2025, 1, 2), new(2025, 1, 6));
        download = Rehash(download with { Snapshot = download.Snapshot with
        {
            Bars = download.Snapshot.Bars.Skip(1).Select(bar => bar.Date == new DateOnly(2025, 1, 6)
                ? bar with { Status = TradingStatus.NoTrading, Open = null, High = null, Low = null, Close = null }
                : bar).ToArray()
        } });
        await directory.Store.SaveAsync(download);
        var entry = Assert.Single(await directory.Store.ListEntriesAsync());
        Assert.Equal(new DateOnly(2025, 1, 2), entry.CoverageStart);
        Assert.Equal(new DateOnly(2025, 1, 6), entry.CoverageEnd);
        Assert.Equal(new DateOnly(2025, 1, 3), entry.FirstPriceDate);
        Assert.Equal(new DateOnly(2025, 1, 5), entry.LastPriceDate);
        Assert.Equal(4, entry.BarCount);
        Assert.False(entry.BlocksResearch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InventoryMarksBothProviderAndSnapshotBlockingDiagnostics(bool providerBlock)
    {
        using var directory = new TestDirectory();
        var download = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        download = providerBlock
            ? download with { Diagnostics = [new() { Code = "ProviderBlocked", Message = "Offline source gap", BlocksResearch = true }] }
            : Rehash(download with { Snapshot = download.Snapshot with { Calendar = download.Snapshot.Calendar with { IsVerified = false } } });
        await directory.Store.SaveAsync(download);
        Assert.True(Assert.Single(await directory.Store.ListEntriesAsync()).BlocksResearch);
    }

    [Fact]
    public async Task InventoryWithNoPriceRowsReturnsNullActualPriceDates()
    {
        using var directory = new TestDirectory();
        var download = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        download = Rehash(download with { Snapshot = download.Snapshot with { Bars = [] } });
        await directory.Store.SaveAsync(download);
        var entry = Assert.Single(await directory.Store.ListEntriesAsync());
        Assert.Null(entry.FirstPriceDate);
        Assert.Null(entry.LastPriceDate);
        Assert.Equal(0, entry.BarCount);
    }

    [Fact]
    public async Task MetadataListingConsolidatesLegacyDuplicatesOnceAndRetainsFingerprint()
    {
        using var directory = new TestDirectory();
        var download = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        var path = await directory.Store.SaveAsync(download);
        File.Copy(path, Path.Combine(directory.Path, "old-copy.twse-data.json"));
        var entry = Assert.Single(await directory.Store.ListEntriesAsync());
        Assert.Equal(path, entry.Path);
        Assert.Equal(2, entry.BarCount);
        Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "legacy")));
        Assert.Equal(download.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(entry.Path)).Snapshot.ContentHash);
        Assert.Single(await directory.Store.ListEntriesAsync());
        Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "legacy")));
    }

    [Fact]
    public async Task DeleteRemovesOnlySelectedStockAndPreservesResearchLegacyAndOtherStock()
    {
        using var directory = new TestDirectory();
        var first = Fixture(new(2025, 1, 2), new(2025, 1, 3));
        var path = await directory.Store.SaveAsync(first);
        var other = Rehash(first with { Snapshot = first.Snapshot with
        { Instrument = first.Snapshot.Instrument with { Code = "2317", InstrumentId = "TWSE:OTHER" } } });
        var otherPath = await directory.Store.SaveAsync(other);
        var legacyDirectory = Path.Combine(directory.Path, "legacy");
        Directory.CreateDirectory(legacyDirectory);
        var backup = Path.Combine(legacyDirectory, "keep.bak");
        var research = Path.Combine(directory.Path, "keep.histolens.json");
        await File.WriteAllTextAsync(backup, "recoverable original");
        await File.WriteAllTextAsync(research, "independent research snapshot");
        var otherBytes = await File.ReadAllBytesAsync(otherPath);
        await directory.Store.DeleteAsync(path);
        Assert.False(File.Exists(path));
        Assert.Equal(otherBytes, await File.ReadAllBytesAsync(otherPath));
        Assert.Equal("recoverable original", await File.ReadAllTextAsync(backup));
        Assert.Equal("independent research snapshot", await File.ReadAllTextAsync(research));
        Assert.Equal("2317", Assert.Single(await directory.Store.ListEntriesAsync()).Code);
    }

    [Fact]
    public async Task DeleteArchivesUnconsolidatedSameStockDuplicateSoRefreshCannotRestoreIt()
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture(new(2025, 1, 2), new(2025, 1, 3)));
        var duplicate = Path.Combine(directory.Path, "old-copy.twse-data.json");
        File.Copy(path, duplicate);
        var duplicateBytes = await File.ReadAllBytesAsync(duplicate);
        await directory.Store.DeleteAsync(path);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(duplicate));
        Assert.Empty(await directory.Store.ListEntriesAsync());
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(directory.Path, "legacy")));
        Assert.Equal(duplicateBytes, await File.ReadAllBytesAsync(backup));
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("traversal")]
    [InlineData("nested")]
    [InlineData("legacy")]
    [InlineData("temporary")]
    [InlineData("research")]
    public async Task DeleteRejectsOutsideNestedAndNonActivePathsWithoutTouchingFiles(string kind)
    {
        using var directory = new TestDirectory();
        using var outside = new TestDirectory();
        var local = await directory.Store.SaveAsync(Fixture(new(2025, 1, 2), new(2025, 1, 3)));
        var target = kind switch
        {
            "outside" => Path.Combine(outside.Path, "external.twse-data.json"),
            "traversal" => Path.Combine(directory.Path, "..", Path.GetFileName(outside.Path), "external.twse-data.json"),
            "nested" => Path.Combine(directory.Path, "nested", "nested.twse-data.json"),
            "legacy" => Path.Combine(directory.Path, "legacy", "old.twse-data.json"),
            "temporary" => Path.Combine(directory.Path, "candidate.twse-data.json.tmp"),
            "research" => Path.Combine(directory.Path, "keep.histolens.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        await File.WriteAllTextAsync(target, "must remain");
        await Assert.ThrowsAsync<ArgumentException>(() => directory.Store.DeleteAsync(target));
        Assert.Equal("must remain", await File.ReadAllTextAsync(target));
        Assert.True(File.Exists(local));
    }

    [Fact]
    public async Task DeleteRejectsDirectoryWithActiveFileSuffix()
    {
        using var directory = new TestDirectory();
        var target = Path.Combine(directory.Path, "directory.twse-data.json");
        Directory.CreateDirectory(target);
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.DeleteAsync(target));
        Assert.True(Directory.Exists(target));
    }

    [Fact]
    public async Task DeleteRejectsJunctionStoreWithoutTouchingItsExternalTarget()
    {
        using var directory = new TestDirectory();
        using var outside = new TestDirectory();
        var external = await outside.Store.SaveAsync(Fixture(new(2025, 1, 2), new(2025, 1, 3)));
        var bytes = await File.ReadAllBytesAsync(external);
        var junction = Path.Combine(directory.Path, "linked-store");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = $"/c mklink /J \"{junction}\" \"{outside.Path}\"",
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        try
        {
            var store = new MarketDataStore(junction);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.DeleteAsync(Path.Combine(junction, Path.GetFileName(external))));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(external));
        }
        finally { Directory.Delete(junction); }
    }

    [Fact]
    public async Task MissingSelectedFileIsReportedAndCancelledDeleteRetainsExactFile()
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture(new(2025, 1, 2), new(2025, 1, 3)));
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.Store.DeleteAsync(path, cancellation.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<FileNotFoundException>(() => directory.Store.DeleteAsync(Path.Combine(directory.Path, "missing.twse-data.json")));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task CorruptActiveFileBlocksInventoryAndDeletionBeforeAnyMutation()
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture(new(2025, 1, 2), new(2025, 1, 3)));
        var bytes = await File.ReadAllBytesAsync(path);
        var corrupt = Path.Combine(directory.Path, "broken.twse-data.json");
        await File.WriteAllTextAsync(corrupt, "{ corrupt }");
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.ListEntriesAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => directory.Store.DeleteAsync(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal("{ corrupt }", await File.ReadAllTextAsync(corrupt));
    }

    [Fact]
    public async Task DeleteWaitsForOngoingUpdateAcrossStoreInstances()
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture(new(2025, 1, 2), new(2025, 1, 3)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var update = directory.Store.UpdateAsync("2330", async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return Fixture(new(2025, 1, 4), new(2025, 1, 6));
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task delete;
        try
        {
            delete = new MarketDataStore(directory.Path).DeleteAsync(path);
            Assert.False(delete.IsCompleted);
            Assert.True(File.Exists(path));
        }
        finally { release.TrySetResult(); }
        await update;
        await delete;
        Assert.Empty(await directory.Store.ListEntriesAsync());
    }

    [Fact]
    public async Task DeleteCancelledWhileWaitingForUpdateDoesNotDelete()
    {
        using var directory = new TestDirectory();
        var path = await directory.Store.SaveAsync(Fixture(new(2025, 1, 2), new(2025, 1, 3)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var update = directory.Store.UpdateAsync("2330", async (existing, _) =>
        {
            entered.SetResult();
            await release.Task;
            return existing!;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        try
        {
            var delete = new MarketDataStore(directory.Path).DeleteAsync(path, cancellation.Token);
            Assert.False(delete.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delete);
        }
        finally { release.TrySetResult(); }
        await update;
        Assert.True(File.Exists(path));
        Assert.Single(await directory.Store.ListEntriesAsync());
    }

    private static HistoricalDataDownload Fixture(DateOnly start, DateOnly end) => CachedMarketDataTests.Fixture(start, end);

    private static HistoricalDataDownload Rehash(HistoricalDataDownload download)
    {
        var hash = SnapshotFingerprint.Compute(download.Snapshot);
        return download with { Snapshot = download.Snapshot with { ContentHash = hash, SnapshotId = hash } };
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens-management-tests-" + Guid.NewGuid().ToString("N"));
        public MarketDataStore Store => new(Path);
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
