using System.IO;
using System.Text.Json;

namespace HistoLens.Data;

/// <summary>A small atomic current-list cache, separate from historical market data.</summary>
public sealed class StockCatalogStore(string path)
{
    private const int MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { IgnoreReadOnlyProperties = true };
    private static readonly string[] LegacyResourceUrls =
    [
        "https://openapi.twse.com.tw/v1/opendata/t187ap03_L",
        "https://www.tpex.org.tw/openapi/v1/mopsfin_t187ap03_O"
    ];
    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public async Task<StockCatalog?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try { stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 8192, true); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        await using (stream)
        {
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException("股票清單檔案超過 8 MiB 上限。");
            try
            {
                var document = await JsonSerializer.DeserializeAsync<Document>(stream, Json, cancellationToken).ConfigureAwait(false);
                if (document is not { Catalog: not null, ResourceUrls: not null }
                    || !(document.FormatVersion == 2 && document.ResourceUrls.SequenceEqual(new[] { FinMindStockCatalogProvider.ResourceUrl })
                        || document.FormatVersion == 1 && document.ResourceUrls.SequenceEqual(LegacyResourceUrls)))
                    throw new InvalidDataException("股票清單檔案版本或來源資料無效。");
                StockCatalogValidation.Validate(document.Catalog, cancellationToken);
                return document.Catalog;
            }
            catch (JsonException exception) { throw new InvalidDataException("股票清單檔案不是有效的 JSON。", exception); }
        }
    }

    public async Task SaveAsync(StockCatalog catalog, CancellationToken cancellationToken = default)
    {
        StockCatalogValidation.Validate(catalog, cancellationToken);
        // Snapshot caller-owned collections before any await so validation and serialization use the same entries.
        var stableCatalog = catalog with { Entries = catalog.Entries.ToArray() };
        StockCatalogValidation.Validate(stableCatalog, cancellationToken);
        var document = new Document(2, stableCatalog, [FinMindStockCatalogProvider.ResourceUrl]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        if (bytes.Length > MaximumFileBytes) throw new InvalidDataException("股票清單檔案超過 8 MiB 上限。");
        cancellationToken.ThrowIfCancellationRequested();
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // The durable candidate is in the destination directory; replacement is one filesystem operation.
            File.Move(temporary, _path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _saveGate.Release(); }
        }
    }

    private sealed record Document(int FormatVersion, StockCatalog Catalog, IReadOnlyList<string> ResourceUrls);
}
