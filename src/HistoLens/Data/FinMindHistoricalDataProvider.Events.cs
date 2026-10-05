using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoLens.Core;

namespace HistoLens.Data;

public sealed partial class FinMindHistoricalDataProvider
{
    private async Task<ActionDownload> ReadActionsAsync(string code, IReadOnlyList<DateRange> ranges,
        List<SourceResponseEvidence> evidence, IProgress<DownloadProgress>? progress, int completed, int total,
        CancellationToken cancellationToken)
    {
        var actions = new List<CorporateAction>();
        var diagnostics = new List<DataIssue>();
        var hashes = new List<string>();
        var stopRequests = false;
        foreach (var range in ranges)
        {
            await TryReadAsync("TaiwanStockDividendResult", Parameters(code, range), true, range).ConfigureAwait(false);
            await TryReadAsync("TaiwanStockCapitalReductionReferencePrice", Parameters(code, range), true, range).ConfigureAwait(false);
        }
        var wholeRange = new DateRange { Start = ranges.Min(range => range.Start), End = ranges.Max(range => range.End) };
        await TryReadAsync("TaiwanStockParValueChange", new Dictionary<string, string>
        {
            ["start_date"] = wholeRange.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = wholeRange.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }, false, wholeRange).ConfigureAwait(false);
        await TryReadAsync("TaiwanStockSplitPrice", null, false, null).ConfigureAwait(false);
        return new(actions, diagnostics,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashes)))));

        async Task TryReadAsync(string dataset, IReadOnlyDictionary<string, string>? parameters, bool singleStock, DateRange? queryRange)
        {
            if (stopRequests) return;
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(completed, total, $"取得 FinMind {dataset} 事件日期"));
            try
            {
                var response = await GetResponseAsync(dataset, parameters, cancellationToken).ConfigureAwait(false);
                using var document = ParseResponse(response, dataset, evidence);
                var sourceActions = ReadActions(document.RootElement, dataset, code, ranges, singleStock, queryRange);
                actions.AddRange(sourceActions);
                hashes.Add(dataset + ":" + response.Hash);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TimeoutException)
            {
                // Successful raw prices remain useful, but absent event evidence must remain unknown.
                if (exception is HttpRequestException { StatusCode: { } status } &&
                    status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    stopRequests = true;
                diagnostics.Add(new()
                {
                    Code = "FinMindActionSourceUnavailable",
                    Message = $"{dataset} 未通過下載或欄位驗證：{exception.Message} 該事件來源保持未知。" +
                        (stopRequests ? "已停止後續來源請求。" : ""), BlocksResearch = false
                });
                hashes.Add(dataset + ":unavailable");
            }
            progress?.Report(new(++completed, total, $"完成 {dataset} 事件來源檢查"));
        }
    }

    private static IReadOnlyList<CorporateAction> ReadActions(JsonElement rows, string dataset, string code,
        IReadOnlyList<DateRange> ranges, bool singleStock, DateRange? queryRange)
    {
        var actions = new List<CorporateAction>();
        var keys = new HashSet<(string, DateOnly)>();
        foreach (var row in rows.EnumerateArray())
        {
            var stock = ReadString(row, "stock_id", dataset);
            var date = ReadDate(row, "date", dataset);
            if (singleStock && stock != code) throw InvalidSource(dataset, "事件回應包含錯誤股票代號");
            if (queryRange is not null && (date < queryRange.Start || date > queryRange.End))
                throw InvalidSource(dataset, "事件日期超出所要求範圍");
            if (!keys.Add((stock, date))) throw InvalidSource(dataset, "相同證券及日期存在重複事件");
            string kind;
            switch (dataset)
            {
                case "TaiwanStockDividendResult":
                    kind = ReadString(row, "stock_or_cache_dividend", dataset) switch
                    {
                        "息" => "CashDividend", "權" => "ExRights", "權息" or "權及息" or "權+息" => "ExRightsAndDividend",
                        _ => "ExRightsOrDividend"
                    };
                    ReadNumber(row, "before_price", dataset);
                    ReadNumber(row, "after_price", dataset);
                    ReadNumber(row, "stock_and_cache_dividend", dataset);
                    break;
                case "TaiwanStockCapitalReductionReferencePrice":
                    kind = "CapitalReduction:" + ReadString(row, "ReasonforCapitalReduction", dataset);
                    ReadNumber(row, "ClosingPriceonTheLastTradingDay", dataset);
                    ReadNumber(row, "PostReductionReferencePrice", dataset);
                    break;
                case "TaiwanStockParValueChange":
                    kind = "DenominationChange";
                    ReadNumber(row, "before_close", dataset);
                    ReadNumber(row, "after_ref_close", dataset);
                    break;
                case "TaiwanStockSplitPrice":
                    kind = "SplitOrDenominationChange:" + ReadString(row, "type", dataset);
                    ReadNumber(row, "before_price", dataset);
                    ReadNumber(row, "after_price", dataset);
                    break;
                default: throw InvalidSource(dataset, "不支援的事件資料表");
            }
            if (stock == code && ranges.Any(range => date >= range.Start && date <= range.End))
                actions.Add(new() { EffectiveDate = date, Kind = kind, SourceId = SourceId + "/" + dataset });
        }
        return actions;
    }

    private sealed record ActionDownload(IReadOnlyList<CorporateAction> Actions, IReadOnlyList<DataIssue> Diagnostics, string Version);
}
