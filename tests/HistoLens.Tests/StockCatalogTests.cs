using System.IO;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

/// <summary>Artificial catalog fixtures for offline persistence and validation regression checks.</summary>
public sealed class StockCatalogTests
{

    [Fact]
    public async Task StoreRoundTripsCurrentListAndKeepsSourceUrlsWithoutRedundantLabel()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "nested", "stocks.json");
        var store = new StockCatalogStore(path);
        Assert.Null(await store.LoadAsync());
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(path)));
        var catalog = Fixture();
        await store.SaveAsync(catalog);
        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(catalog.UpdatedAtUtc, loaded.UpdatedAtUtc);
        Assert.Equal(catalog.Entries, loaded.Entries);
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains(FinMindStockCatalogProvider.ResourceUrl, text);
        Assert.DoesNotContain("\"Label\"", text);
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task StoreAtomicReplacementPreservesPreviousListWhenDestinationIsLocked()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "stocks.json");
        var store = new StockCatalogStore(path);
        await store.SaveAsync(Fixture());
        var originalBytes = await File.ReadAllBytesAsync(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = await Record.ExceptionAsync(() => store.SaveAsync(Fixture("新的測試名稱")));
            // Windows reports a locked MoveFileEx destination as access denied on this runtime.
            Assert.True(error is IOException or UnauthorizedAccessException,
                $"Expected a surfaced filesystem failure, received {error?.GetType().Name ?? "no exception"}.");
        }
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        await store.SaveAsync(Fixture("新的測試名稱"));
        Assert.Equal("新的測試名稱", (await store.LoadAsync())!.Entries[0].Name);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("one-market")]
    [InlineData("duplicate")]
    [InlineData("unknown-market")]
    [InlineData("bad-code")]
    [InlineData("blank-name")]
    [InlineData("null-entry")]
    [InlineData("null-entries")]
    [InlineData("default-time")]
    [InlineData("non-utc-time")]
    [InlineData("future-time")]
    public async Task InvalidSaveCannotReplacePriorCatalog(string mutation)
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "stocks.json");
        var store = new StockCatalogStore(path);
        var catalog = Fixture();
        await store.SaveAsync(catalog);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var invalid = mutation switch
        {
            "empty" => catalog with { Entries = [] },
            "one-market" => catalog with { Entries = [catalog.Entries[0]] },
            "duplicate" => catalog with { Entries = [catalog.Entries[0], catalog.Entries[1], catalog.Entries[0]] },
            "unknown-market" => catalog with { Entries = [catalog.Entries[0] with { Market = "UNKNOWN" }, catalog.Entries[1]] },
            "bad-code" => catalog with { Entries = [catalog.Entries[0] with { Code = "0050A" }, catalog.Entries[1]] },
            "blank-name" => catalog with { Entries = [catalog.Entries[0] with { Name = " " }, catalog.Entries[1]] },
            "null-entry" => catalog with { Entries = [null!, catalog.Entries[1]] },
            "null-entries" => catalog with { Entries = null! },
            "default-time" => catalog with { UpdatedAtUtc = default },
            "non-utc-time" => catalog with { UpdatedAtUtc = catalog.UpdatedAtUtc.ToOffset(TimeSpan.FromHours(8)) },
            "future-time" => catalog with { UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(1) },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CancelledSaveLeavesPreviousCatalogIntact()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "stocks.json");
        var store = new StockCatalogStore(path);
        await store.SaveAsync(Fixture());
        var originalBytes = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Fixture("新名稱"), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.LoadAsync(cancellation.Token));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"FormatVersion\":2,\"Catalog\":null,\"ResourceUrls\":[]}")]
    public async Task CorruptStoredCatalogIsReportedInsteadOfPretendingNoFileExists(string text)
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "stocks.json");
        await File.WriteAllTextAsync(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => new StockCatalogStore(path).LoadAsync());
        Assert.Equal(text, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task StoredListSizeIsBounded()
    {
        using var directory = new TestDirectory();
        var path = System.IO.Path.Combine(directory.Path, "stocks.json");
        using (var stream = File.Create(path)) stream.SetLength(8 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => new StockCatalogStore(path).LoadAsync());
    }

    private static StockCatalog Fixture(string listedName = "測試上市股") => new(DateTimeOffset.UtcNow,
        [new("TWSE", "2330", listedName), new("TPEx", "6488", "測試上櫃股")]);

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens-stock-catalog-tests-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
