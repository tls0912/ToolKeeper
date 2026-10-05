using System.IO;

namespace HistoLens.Data;

/// <summary>A current company selection entry; not a historical security identity.</summary>
public sealed record StockCatalogEntry(string Market, string Code, string Name)
{
    public string Label => $"{Code} · {Name} · {Market}";
}

/// <summary>UpdatedAtUtc is the local successful retrieval time, not a market-data date.</summary>
public sealed record StockCatalog(DateTimeOffset UpdatedAtUtc, IReadOnlyList<StockCatalogEntry> Entries);

public interface IStockCatalogProvider
{
    Task<StockCatalog> GetAsync(CancellationToken cancellationToken = default);
}

internal static class StockCatalogValidation
{
    internal const int MaximumEntries = 20_000;

    internal static void Validate(StockCatalog catalog, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (catalog is null || catalog.Entries is null || catalog.Entries.Count is 0 or > MaximumEntries
            || catalog.UpdatedAtUtc.Offset != TimeSpan.Zero
            || catalog.UpdatedAtUtc < new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)
            || catalog.UpdatedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("股票清單的資料或更新時間不完整。");

        var keys = new HashSet<(string Market, string Code)>();
        var hasTwse = false;
        var hasTpex = false;
        foreach (var entry in catalog.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null || entry.Market is not ("TWSE" or "TPEx")
                || entry.Code is null || entry.Code.Length != 4 || entry.Code.Any(character => character is < '0' or > '9')
                || string.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 300 || entry.Name != entry.Name.Trim()
                || entry.Name.Any(char.IsControl))
                throw new InvalidDataException("股票清單含有不支援的市場、代號或名稱。");
            if (!keys.Add((entry.Market, entry.Code)))
                throw new InvalidDataException($"股票清單有重複的 {entry.Market} {entry.Code}，無法唯一選取。");
            hasTwse |= entry.Market == "TWSE";
            hasTpex |= entry.Market == "TPEx";
        }
        if (!hasTwse || !hasTpex)
            throw new InvalidDataException("股票清單必須同時包含上市及上櫃資料；未保存部分更新。");
    }
}
