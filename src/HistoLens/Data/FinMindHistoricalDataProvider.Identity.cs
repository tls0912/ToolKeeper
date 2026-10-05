using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistoLens.Core;

namespace HistoLens.Data;

public sealed partial class FinMindHistoricalDataProvider
{
    // A provider-scoped identity, not an invented ISIN or a certified security-lifetime registry.
    private static Instrument ReadInstrument(JsonElement rows, HistoricalDataRequest request)
    {
        var versions = rows.EnumerateArray().Where(row => ReadString(row, "stock_id", "TaiwanStockInfo") == request.Code)
            .Select(row => new
            {
                Date = ReadDate(row, "date", "TaiwanStockInfo"),
                Market = ReadString(row, "type", "TaiwanStockInfo"),
                Name = ReadString(row, "stock_name", "TaiwanStockInfo").Trim(),
                Category = ReadString(row, "industry_category", "TaiwanStockInfo")
            }).ToArray();
        if (versions.Length == 0) throw InvalidSource("TaiwanStockInfo", "查無所選股票");
        var latest = versions.Max(row => row.Date);
        var current = versions.Where(row => row.Date == latest).ToArray();
        var market = request.Market == "TWSE" ? "twse" : "tpex";
        if (current.Any(row => row.Market != market) || current.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count() != 1)
            throw InvalidSource("TaiwanStockInfo", "最新版本的股票市場或名稱無法唯一確認");
        var name = current[0].Name;
        if (name.Length is 0 or > 300 || name.Any(char.IsControl) || current.Any(row =>
            string.IsNullOrWhiteSpace(row.Category) || !FinMindStockCatalogProvider.IsCommonStockCategory(row.Category)))
            throw InvalidSource("TaiwanStockInfo", "非目前支援的股票，或名稱不完整");
        var previousBoundary = versions.Where(row => row.Market != market || row.Name != name)
            .Select(row => (DateOnly?)row.Date).Max();
        if (previousBoundary is { } boundary && request.Start <= boundary)
            throw InvalidSource("TaiwanStockInfo", $"指定期間跨越 {boundary:yyyy-MM-dd} 的不同市場或名稱版本；無完整沿革證據，未自動串接");
        var key = request.Market + "\n" + request.Code + "\n" + name + "\n" + previousBoundary?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
        return new()
        {
            InstrumentId = $"FinMind:{request.Market}:{request.Code}:{version}", Code = request.Code,
            Name = name, Market = request.Market, Currency = "TWD", SecurityType = SecurityType.CommonStock
            // No listing date is inferred from the first returned price or the current master date.
        };
    }

    // Existing v1 FinMind caches can keep their saved instrument reference only when their OWN
    // preserved FinMind master evidence resolves to the same source scope as the new response.
    // This never imports, relabels or joins an old official-price cache.
    internal static HistoricalDataDownload PreserveLegacyIdentity(HistoricalDataDownload? existing, HistoricalDataDownload incoming)
    {
        if (existing is null || existing.Snapshot.SourceId != SourceId || incoming.Snapshot.SourceId != SourceId ||
            existing.Snapshot.Instrument.InstrumentId == incoming.Snapshot.Instrument.InstrumentId) return incoming;
        var old = existing.Snapshot.Instrument;
        var next = incoming.Snapshot.Instrument;
        if (old.Market != next.Market || old.Code != next.Code ||
            !Regex.IsMatch(old.InstrumentId, "^(TWSE|TPEx):TW[A-Z0-9]{10}$", RegexOptions.CultureInvariant)) return incoming;
        var evidence = existing.SourceEvidence?.Where(item => item.Dataset == "TaiwanStockInfo")
            .OrderByDescending(item => item.RetrievedAtUtc).FirstOrDefault();
        if (evidence is null) return incoming;
        using var json = JsonDocument.Parse(evidence.RawJson);
        var request = new HistoricalDataRequest(old.Code,
            existing.Snapshot.Calendar.CoverageStart < incoming.Snapshot.Calendar.CoverageStart
                ? existing.Snapshot.Calendar.CoverageStart : incoming.Snapshot.Calendar.CoverageStart,
            incoming.Snapshot.DataAsOf, old.Market);
        var previousScope = ReadInstrument(json.RootElement.GetProperty("data"), request);
        if (previousScope.InstrumentId != next.InstrumentId || previousScope.Name != old.Name)
            throw new InvalidDataException("本機 FinMind 主檔證據與目前證券版本不同；保留原資料，未合併不同身分。");
        var snapshot = incoming.Snapshot with { Instrument = old, ContentHash = "", SnapshotId = "" };
        var hash = SnapshotFingerprint.Compute(snapshot);
        return incoming with
        {
            Snapshot = snapshot with { ContentHash = hash, SnapshotId = "finmind-legacy-reference-" + hash[..16] },
            Diagnostics = incoming.Diagnostics.Append(new DataIssue
            {
                Code = "SavedInstrumentReferenceRetained",
                Message = "依本機保存的 FinMind 主檔證據保留舊證券參照；本次未向其他資料來源重新核對。"
            }).ToArray()
        };
    }
}
