using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HistoLens.Data;

/// <summary>Reads FinMind's current selection master; entries are not historical security identities.</summary>
public sealed class FinMindStockCatalogProvider : IStockCatalogProvider
{
    public const string ResourceUrl = "https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockInfo";
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;

    public FinMindStockCatalogProvider() : this(SharedHttpClient) { }

    /// <summary>The caller owns the injected client. Requests are bounded and are never retried.</summary>
    public FinMindStockCatalogProvider(HttpClient httpClient, TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestTimeout = FinMindAuxiliaryHttp.ValidateTimeout(requestTimeout);
    }

    public async Task<StockCatalog> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var data = await FinMindAuxiliaryHttp.ReadAsync(_httpClient, ResourceUrl, "TaiwanStockInfo",
            _timeProvider, _requestTimeout, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
        var rows = new List<CatalogRow>();
        foreach (var row in data.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = FinMindAuxiliaryHttp.RequiredString(row, "stock_id", "TaiwanStockInfo").Trim();
            if (code.Length == 0) throw InvalidSource("股票代號空白");
            if (code.Length != 4 || code.Any(character => character is < '0' or > '9')) continue;
            var name = FinMindAuxiliaryHttp.RequiredString(row, "stock_name", "TaiwanStockInfo").Trim();
            var type = FinMindAuxiliaryHttp.RequiredString(row, "type", "TaiwanStockInfo").Trim();
            var industry = FinMindAuxiliaryHttp.RequiredString(row, "industry_category", "TaiwanStockInfo").Trim();
            if (name.Length is 0 or > 300 || name.Any(char.IsControl) || industry.Length == 0 || type.Length == 0)
                throw InvalidSource($"{code} 名稱、市場或證券類型不完整");
            var dateText = FinMindAuxiliaryHttp.RequiredString(row, "date", "TaiwanStockInfo");
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date > today)
                throw InvalidSource($"{code} 資料日期無法確認");
            rows.Add(new(code, name, type, industry, date));
        }

        var entries = new List<StockCatalogEntry>();
        foreach (var codeRows in rows.GroupBy(row => row.Code, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latestDate = codeRows.Max(row => row.Date);
            var current = codeRows.Where(row => row.Date == latestDate).ToArray();
            if (current.Select(row => row.Type).Distinct(StringComparer.Ordinal).Count() != 1
                || current.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count() != 1)
                throw InvalidSource($"{codeRows.Key} 最新資料包含不同市場或名稱，無法唯一選取");
            var selected = current[0];
            var market = selected.Type switch { "twse" => "TWSE", "tpex" => "TPEx", _ => null };
            if (market is null || current.Any(row => !IsCommonStockCategory(row.Industry))) continue;
            entries.Add(new(market, selected.Code, selected.Name));
        }
        var catalog = new StockCatalog(_timeProvider.GetUtcNow().ToUniversalTime(), entries
            .OrderBy(entry => entry.Market, StringComparer.Ordinal).ThenBy(entry => entry.Code, StringComparer.Ordinal).ToArray());
        StockCatalogValidation.Validate(catalog, cancellationToken);
        return catalog;
    }

    internal static bool IsCommonStockCategory(string category) =>
        !category.Contains("ETF", StringComparison.OrdinalIgnoreCase)
        && !category.Contains("ETN", StringComparison.OrdinalIgnoreCase)
        && !category.Contains("Index", StringComparison.OrdinalIgnoreCase)
        && !category.Contains("存託", StringComparison.Ordinal)
        && !category.Contains("受益證券", StringComparison.Ordinal)
        && category is not ("大盤" or "所有證券");

    private static InvalidDataException InvalidSource(string reason) =>
        new($"FinMind 股票清單來源資料無法確認：{reason}。");

    private sealed record CatalogRow(string Code, string Name, string Type, string Industry, DateOnly Date);
}

/// <summary>Bounded public JSON requests shared by the two auxiliary FinMind providers.</summary>
internal static class FinMindAuxiliaryHttp
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;

    internal static TimeSpan ValidateTimeout(TimeSpan? value)
    {
        var timeout = value ?? TimeSpan.FromSeconds(45);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(value));
        return timeout;
    }

    internal static async Task<JsonDocument> ReadAsync(HttpClient client, string url, string dataset,
        TimeProvider timeProvider, TimeSpan requestTimeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(requestTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.UserAgent.ParseAdd("ToolKeeper-HistoLens/0.1 (+https://github.com/tls0912/ToolKeeper)");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "text/json"))
                throw InvalidSource(dataset, "回應不是 JSON");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                throw InvalidSource(dataset, "回應超過 8 MiB 上限");
            await using var input = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var count = await input.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (bytes.Length + count > MaximumResponseBytes) throw InvalidSource(dataset, "回應超過 8 MiB 上限");
                bytes.Write(buffer, 0, count);
            }
            linked.Token.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(bytes.ToArray()));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var code))
                throw InvalidSource(dataset, "缺少 API 狀態碼");
            if (code != 200)
                throw new HttpRequestException($"FinMind {dataset} 回覆 API 狀態 {code}；未重試。", null,
                    code is >= 400 and <= 599 ? (HttpStatusCode)code : null);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                || data.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object))
                throw InvalidSource(dataset, "缺少有效的 data 資料列陣列");
            return JsonDocument.Parse(data.GetRawText());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"FinMind {dataset} 不是合法 JSON。", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"FinMind {dataset} 不是合法 UTF-8。", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"FinMind {dataset} 下載逾時。", exception);
        }
    }

    internal static string RequiredString(JsonElement row, string field, string dataset)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
            throw InvalidSource(dataset, $"缺少文字欄位 {field}");
        return value.GetString()!;
    }

    internal static InvalidDataException InvalidSource(string dataset, string reason) =>
        new($"FinMind {dataset} 來源資料無法確認：{reason}。");
}
