using System.IO;
using System.Text.Json;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

public sealed class StockCatalogStoreTests
{
    private static readonly string[] LegacyUrls =
    [
        "https://openapi.twse.com.tw/v1/opendata/t187ap03_L",
        "https://www.tpex.org.tw/openapi/v1/mopsfin_t187ap03_O"
    ];

    [Fact]
    public async Task FinMindSaveUsesVersionTwoAndOnlyFinMindResourceProvenance()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "catalog.json");
        var catalog = Fixture();
        var store = new StockCatalogStore(path);
        await store.SaveAsync(catalog);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(2, document.RootElement.GetProperty("FormatVersion").GetInt32());
        Assert.Equal(new[] { FinMindStockCatalogProvider.ResourceUrl },
            document.RootElement.GetProperty("ResourceUrls").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(catalog.Entries, (await store.LoadAsync())!.Entries);
    }

    [Fact]
    public async Task LegacyCatalogCanLoadOfflineWithoutRewritingItsSourceOrFile()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "catalog.json");
        var catalog = Fixture();
        var text = JsonSerializer.Serialize(new { FormatVersion = 1, Catalog = catalog, ResourceUrls = LegacyUrls });
        await File.WriteAllTextAsync(path, text);
        var loaded = await new StockCatalogStore(path).LoadAsync();
        Assert.Equal(catalog.UpdatedAtUtc, loaded!.UpdatedAtUtc);
        Assert.Equal(catalog.Entries, loaded.Entries);
        Assert.Equal(text, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(1, "finmind")]
    [InlineData(2, "legacy")]
    [InlineData(2, "unknown")]
    [InlineData(3, "finmind")]
    public async Task VersionAndResourceMismatchIsRejectedWithoutChangingStoredFile(int version, string source)
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "catalog.json");
        var urls = source switch
        {
            "finmind" => new[] { FinMindStockCatalogProvider.ResourceUrl },
            "legacy" => LegacyUrls,
            _ => ["https://example.invalid/catalog"]
        };
        var text = JsonSerializer.Serialize(new { FormatVersion = version, Catalog = Fixture(), ResourceUrls = urls });
        await File.WriteAllTextAsync(path, text);
        await Assert.ThrowsAsync<InvalidDataException>(() => new StockCatalogStore(path).LoadAsync());
        Assert.Equal(text, await File.ReadAllTextAsync(path));
    }

    private static StockCatalog Fixture() => new(DateTimeOffset.UtcNow,
        [new("TWSE", "2330", "Offline listed fixture"), new("TPEx", "6488", "Offline OTC fixture")]);

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens.FinMindCatalogStore", Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
