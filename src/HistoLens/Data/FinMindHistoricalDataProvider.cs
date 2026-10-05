using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HistoLens.Core;

namespace HistoLens.Data;

/// <summary>Explicit, anonymous requests to FinMind's public API for raw daily prices.</summary>
public sealed partial class FinMindHistoricalDataProvider : IRangeHistoricalDataProvider, IHistoricalDataSource
{
    public const string SourceId = "FinMind";
    // The price feed starts earlier, but the separate trading-date feed has no earlier verified coverage.
    public static readonly DateOnly EarliestDate = new(1999, 1, 5);
    private const string AdapterVersion = "finmind-public-v2";
    private const string Endpoint = "https://api.finmindtrade.com/api/v4/data";
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim SharedRequestGate = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestInterval;
    private readonly TimeSpan _requestTimeout;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _requestGate;

    public string DataSourceId => SourceId;

    public FinMindHistoricalDataProvider() : this(SharedHttpClient) { }

    /// <summary>The caller owns the injected client. Zero delay is intended for offline fixtures.</summary>
    public FinMindHistoricalDataProvider(HttpClient httpClient, TimeProvider? timeProvider = null,
        TimeSpan? requestInterval = null, TimeSpan? requestTimeout = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestInterval = requestInterval ?? TimeSpan.FromSeconds(3);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(45);
        if (_requestInterval < TimeSpan.Zero || _requestInterval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestInterval));
        if (_requestTimeout <= TimeSpan.Zero || _requestTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        _delay = delay ?? ((duration, token) => Task.Delay(duration, _timeProvider, token));
        _requestGate = ReferenceEquals(httpClient, SharedHttpClient) ? SharedRequestGate : new(1, 1);
    }

    public HistoricalDataRequest ValidateRequest(HistoricalDataRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = request.Code?.Trim() ?? "";
        if (request.Market is not ("TWSE" or "TPEx"))
            throw new ArgumentException("FinMind 目前只接受明確指定的上市或上櫃市場。", nameof(request));
        if (!Regex.IsMatch(code, "^[0-9]{4}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("請輸入四位數股票代號；市場與股票名稱會向 FinMind 主檔核對。", nameof(request));
        if (request.Start < EarliestDate || request.End < request.Start)
            throw new ArgumentException($"下載期間須自 {EarliestDate:yyyy-MM-dd} 起，且開始日不得晚於結束日。", nameof(request));
        var yesterday = TaiwanToday().AddDays(-1);
        if (request.End > yesterday)
            throw new ArgumentException($"只接受已完成日期；結束日最晚為台灣時間昨日 {yesterday:yyyy-MM-dd}。", nameof(request));
        if (request.End >= request.Start.AddYears(10))
            throw new ArgumentException("單次下載最多十年；請縮小日期範圍。", nameof(request));
        return request with { Code = code };
    }

    public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DownloadCoreAsync(request, [new() { Start = request.Start, End = request.End }], progress, cancellationToken);
    }

    public Task<HistoricalDataDownload> DownloadMissingRangesAsync(HistoricalDataRequest request,
        IReadOnlyList<DateRange> ranges, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        return DownloadCoreAsync(request, ranges, progress, cancellationToken);
    }

    private async Task<HistoricalDataDownload> DownloadCoreAsync(HistoricalDataRequest request,
        IReadOnlyList<DateRange> missingRanges, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = ValidateRequest(request);
        if (missingRanges.Count == 0 || missingRanges.Any(range => range is null || range.Start > range.End ||
            range.Start < request.Start || range.End > request.End))
            throw new ArgumentException("缺失日期範圍不得為空或超出指定下載期間。", nameof(missingRanges));
        var ranges = MarketDataMerge.Normalize(missingRanges).ToArray();
        var completed = 0;
        var total = 2 + ranges.Length * 3 + 2;
        var evidence = new List<SourceResponseEvidence>();
        progress?.Report(new(completed, total, "取得 FinMind 股票主檔"));
        var masterResponse = await GetResponseAsync("TaiwanStockInfo", null, cancellationToken).ConfigureAwait(false);
        Instrument identity;
        using (var master = ParseResponse(masterResponse, "TaiwanStockInfo", evidence))
            identity = ReadInstrument(master.RootElement, request);
        progress?.Report(new(++completed, total, "已核對 FinMind 股票代號與市場"));
        var calendarResponse = await GetResponseAsync("TaiwanStockTradingDate", null, cancellationToken).ConfigureAwait(false);
        DateOnly[] marketDates;
        using (var calendar = ParseResponse(calendarResponse, "TaiwanStockTradingDate", evidence))
            marketDates = ReadCalendar(calendar.RootElement, request);
        progress?.Report(new(++completed, total, "已驗證 FinMind 交易日表格式與涵蓋範圍"));

        var downloaded = new List<(DateRange Range, DailyBar[] Bars, string Hash)>();
        foreach (var range in ranges)
        {
            progress?.Report(new(completed, total, $"下載 FinMind {range.Start:yyyy-MM-dd} 至 {range.End:yyyy-MM-dd} 缺少行情"));
            var response = await GetResponseAsync("TaiwanStockPrice", Parameters(request.Code, range), cancellationToken).ConfigureAwait(false);
            using var prices = ParseResponse(response, "TaiwanStockPrice", evidence);
            var bars = ReadPrices(prices.RootElement, request.Code, range);
            if (bars.Any(bar => !marketDates.Contains(bar.Date)))
                throw InvalidSource("TaiwanStockPrice", "行情日期不在獨立交易日表內");
            downloaded.Add((range, bars, response.Hash));
            progress?.Report(new(++completed, total, $"已取得 {bars.Length} 筆原始日線"));
        }
        var actions = await ReadActionsAsync(request.Code, ranges, evidence, progress, completed, total, cancellationToken).ConfigureAwait(false);
        var downloads = new List<HistoricalDataDownload>();
        foreach (var (range, bars, priceHash) in downloaded)
        {
            var snapshot = new DataSnapshot
            {
                Instrument = identity, SourceId = SourceId, IsSynthetic = false,
                DataVersion = AdapterVersion + "/TaiwanStockPrice/raw/TWD/shares/" + priceHash,
                RetrievedAtUtc = _timeProvider.GetUtcNow(), DataAsOf = range.End,
                Calendar = new()
                {
                    Market = request.Market, IsVerified = true,
                    CoverageStart = range.Start, CoverageEnd = range.End,
                    Version = AdapterVersion + "/TaiwanStockTradingDate/source-validated/" + calendarResponse.Hash,
                    TradingDates = marketDates.Where(date => date >= range.Start && date <= range.End).ToArray()
                },
                ActionCoverage = new()
                {
                    IsVerified = false, CoverageStart = range.Start, CoverageEnd = range.End,
                    SourceId = "FinMind/TaiwanStockDividendResult+TaiwanStockCapitalReductionReferencePrice+TaiwanStockParValueChange+TaiwanStockSplitPrice",
                    Version = AdapterVersion + "/event-inventory-unverified/" + actions.Version, Gaps = [range]
                },
                CorporateActions = actions.Actions.Where(action => action.EffectiveDate >= range.Start && action.EffectiveDate <= range.End)
                    .DistinctBy(action => (action.EffectiveDate, action.Kind, action.SourceId)).OrderBy(action => action.EffectiveDate).ToArray(),
                Bars = bars
            };
            var hash = SnapshotFingerprint.Compute(snapshot);
            snapshot = snapshot with { ContentHash = hash, SnapshotId = $"finmind-{request.Market.ToLowerInvariant()}-{request.Code}-{range.Start:yyyyMMdd}-{range.End:yyyyMMdd}-{hash[..16]}" };
            var diagnostics = actions.Diagnostics.ToList();
            diagnostics.Add(new()
            {
                Code = "FinMindCalendarSource",
                Message = "交易日採用 FinMind 獨立交易日資料表，已檢查日期格式、重複及涵蓋範圍；不再向其他機構交叉查詢。"
            });
            diagnostics.Add(new()
            {
                Code = "FinMindSecurityLifetimeUnverified",
                Message = "證券依 FinMind 市場、代號、名稱及已知主檔版本分開識別；來源未提供 ISIN 或完整存續沿革，不宣稱已核實所有歷史身分。"
            });
            diagnostics.Add(new()
            {
                Code = "FinMindCorporateActionScopeUnverified",
                Message = "保留已取得除權息、減資、面額變更與分割日期；停復牌及事件修訂完整性仍未知，原始價格未還原。"
            });
            diagnostics.AddRange(SnapshotValidator.Validate(snapshot, cancellationToken));
            downloads.Add(new(snapshot, diagnostics)
            {
                CacheCoverage = new()
                {
                    CheckedPriceRanges = [range], VerifiedCalendarRanges = [range],
                    ConfirmedNoPriceDates = bars.Where(bar => bar.Open is null && bar.High is null && bar.Low is null && bar.Close is null)
                        .Select(bar => bar.Date).ToArray(),
                    KnownActionRanges = [], ActionEvidenceGaps = [range],
                    KnownComparisonRanges = [], ComparisonEvidenceGaps = [range]
                }
            });
        }
        var result = MarketDataMerge.Merge(downloads) with { SourceEvidence = evidence };
        progress?.Report(new(total, total, $"FinMind 下載完成：{result.Snapshot.Bars.Count} 筆原始日線"));
        return result;
    }

    private static DateOnly[] ReadCalendar(JsonElement rows, HistoricalDataRequest request)
    {
        var dates = rows.EnumerateArray().Select(row => ReadDate(row, "date", "TaiwanStockTradingDate")).ToArray();
        if (dates.Length == 0 || dates.Distinct().Count() != dates.Length)
            throw InvalidSource("TaiwanStockTradingDate", "交易日期空白或重複");
        if (dates.Min() > request.Start || dates.Max() < request.End)
            throw InvalidSource("TaiwanStockTradingDate", "交易日表未涵蓋要求的歷史範圍");
        return dates.Where(date => date >= request.Start && date <= request.End).Order().ToArray();
    }

    private static DailyBar[] ReadPrices(JsonElement rows, string code, DateRange range)
    {
        var bars = new List<DailyBar>();
        var dates = new HashSet<DateOnly>();
        foreach (var row in rows.EnumerateArray())
        {
            if (ReadString(row, "stock_id", "TaiwanStockPrice") != code)
                throw InvalidSource("TaiwanStockPrice", "回應包含錯誤股票代號");
            var date = ReadDate(row, "date", "TaiwanStockPrice");
            if (date < range.Start || date > range.End || !dates.Add(date))
                throw InvalidSource("TaiwanStockPrice", "行情日期超出要求範圍或重複");
            var volume = ReadNumber(row, "Trading_Volume", "TaiwanStockPrice");
            var money = ReadNumber(row, "Trading_money", "TaiwanStockPrice");
            if (volume < 0 || volume is { } value && decimal.Truncate(value) != value || money < 0)
                throw InvalidSource("TaiwanStockPrice", "成交股數必須是非負整數股，成交金額不得為負數");
            bars.Add(new()
            {
                Date = date, Open = ReadPrice(row, "open"), High = ReadPrice(row, "max"), Low = ReadPrice(row, "min"), Close = ReadPrice(row, "close"),
                Volume = volume, Turnover = money, SourceId = SourceId, OriginalVolumeUnit = "shares",
                Status = volume == 0 ? TradingStatus.NoTrading : TradingStatus.Traded
            });
        }
        return bars.OrderBy(bar => bar.Date).ToArray();
    }

    private static decimal? ReadPrice(JsonElement row, string name)
    {
        var value = ReadNumber(row, name, "TaiwanStockPrice");
        if (value < 0) throw InvalidSource("TaiwanStockPrice", "價格為負數");
        return value == 0 ? null : value;
    }

    private static IReadOnlyDictionary<string, string> Parameters(string code, DateRange range) => new Dictionary<string, string>
    {
        ["data_id"] = code, ["start_date"] = range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["end_date"] = range.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    };

    private DateOnly TaiwanToday() => DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(TimeSpan.FromHours(8)).DateTime);
    private static InvalidDataException InvalidSource(string dataset, string detail) => new($"FinMind {dataset}：{detail}；本次資料未通過驗證。");
}
