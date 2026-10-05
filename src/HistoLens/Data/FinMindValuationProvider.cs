using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using HistoLens.Core;

namespace HistoLens.Data;

/// <summary>Reads the most recently published P/E from FinMind when analysis requests it.</summary>
public sealed class FinMindValuationProvider : ICurrentValuationProvider
{
    public const string ResourceUrl = "https://api.finmindtrade.com/api/v4/data";
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;

    public FinMindValuationProvider() : this(SharedHttpClient) { }

    /// <summary>The caller owns the injected client. Each request has a forty-five-second default timeout.</summary>
    public FinMindValuationProvider(HttpClient httpClient, TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestTimeout = FinMindAuxiliaryHttp.ValidateTimeout(requestTimeout);
    }

    public async Task<CurrentValuation?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
        var code = instrument.Code.Trim();
        var market = instrument.Market.Trim();
        market = market.Equals("TWSE", StringComparison.OrdinalIgnoreCase) ? "TWSE"
            : market.Equals("TPEx", StringComparison.OrdinalIgnoreCase) ? "TPEx" : "";
        if (instrument.SecurityType != SecurityType.CommonStock || market.Length == 0 || code.Length != 4
            || code.Any(character => character is < '0' or > '9')
            || instrument.ListedUntil is { } until && until < today
            || instrument.ListedFrom is { } from && from > today)
            return null;
        var start = today.AddDays(-14);
        var url = ResourceUrl + "?dataset=TaiwanStockPER&data_id=" + Uri.EscapeDataString(code)
            + "&start_date=" + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            + "&end_date=" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var data = await FinMindAuxiliaryHttp.ReadAsync(_httpClient, url, "TaiwanStockPER", _timeProvider,
            _requestTimeout, cancellationToken).ConfigureAwait(false);
        var dates = new HashSet<DateOnly>();
        DateOnly? latest = null;
        decimal? latestRatio = null;
        foreach (var row in data.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FinMindAuxiliaryHttp.RequiredString(row, "stock_id", "TaiwanStockPER") != code)
                throw InvalidSource("回應包含其他股票代號");
            var dateText = FinMindAuxiliaryHttp.RequiredString(row, "date", "TaiwanStockPER");
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || date < start || date > today || !dates.Add(date))
                throw InvalidSource("資料日期無效、超出查詢期間或重複");
            var ratio = ReadRatio(row);
            if (latest is null || date > latest)
            {
                latest = date;
                latestRatio = ratio;
            }
        }
        if (latest is not { } latestDate || instrument.ListedFrom is { } listedFrom && latestDate < listedFrom
            || instrument.ListedUntil is { } listedUntil && latestDate > listedUntil)
            return null;
        // The P/E response has no name, market or ISIN; it must not invent an independent identity confirmation.
        return new(market, code, latestRatio, latestDate, _timeProvider.GetUtcNow().ToUniversalTime(),
            "FinMind/TaiwanStockPER", url);
    }

    private static decimal? ReadRatio(JsonElement row)
    {
        if (!row.TryGetProperty("PER", out var value)) throw InvalidSource("缺少 PER 欄位");
        if (value.ValueKind == JsonValueKind.Null) return null;
        decimal ratio;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out ratio))
            return ratio >= 0 ? ratio == 0 ? null : ratio : throw InvalidSource("PER 是負值");
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!.Trim();
            if (text is "" or "--" or "-" or "None" or "N/A") return null;
            if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out ratio) && ratio >= 0)
                return ratio == 0 ? null : ratio;
        }
        throw InvalidSource("PER 數值格式無效");
    }

    private static System.IO.InvalidDataException InvalidSource(string reason) =>
        FinMindAuxiliaryHttp.InvalidSource("TaiwanStockPER", reason);
}
