using System.IO;
using System.Text.RegularExpressions;
using HistoLens.Core;

namespace HistoLens.Data;

public sealed record CachedMarketDataResult(HistoricalDataDownload Download, string Path, int ReusedDays,
    int DownloadedMonths, int AddedBars, int RepairedBars)
{
    public int DownloadedRanges { get; init; }
    public bool UsesDateRanges { get; init; }
}

/// <summary>Checks local calendar and price coverage before requesting missing source data.</summary>
public sealed class CachedMarketDataDownloader(IHistoricalDataProvider provider, MarketDataStore store)
{
    public async Task<CachedMarketDataResult> DownloadAsync(HistoricalDataRequest request,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request = request with { Code = request.Code?.Trim() ?? "" };
        if (provider is IRangeHistoricalDataProvider ranged) request = ranged.ValidateRequest(request);
        else if (provider is IMonthlyHistoricalDataProvider monthly) request = monthly.ValidateRequest(request);
        else if (request.Market is not ("TWSE" or "TPEx") ||
            !Regex.IsMatch(request.Code, "^[0-9]{4}$", RegexOptions.CultureInvariant) || request.Start > request.End)
            throw new ArgumentException("股票代碼或下載日期範圍不正確。", nameof(request));
        var reusedDays = 0;
        var downloadedMonths = 0;
        var downloadedRanges = 0;
        var usesDateRanges = provider is IRangeHistoricalDataProvider;
        var sourceId = provider is IHistoricalDataSource source ? source.DataSourceId : request.Market;
        IReadOnlyDictionary<DateOnly, DailyBar> originalBars = new Dictionary<DateOnly, DailyBar>();
        progress?.Report(new(0, 1, "逐日比對本機已保存資料"));
        var saved = await store.UpdateAsync(request.Code, request.Market, sourceId, async (existing, token) =>
        {
            var missingMonths = new HashSet<DateOnly>();
            var missingDates = new List<DateRange>();
            var coverage = existing is null ? null : MarketDataMerge.Coverage(existing);
            var confirmedNoPriceDates = coverage?.ConfirmedNoPriceDates?.ToHashSet() ?? [];
            var tradingDates = existing?.Snapshot.Calendar.TradingDates.ToHashSet() ?? [];
            originalBars = existing?.Snapshot.Bars.ToDictionary(bar => bar.Date) ?? new Dictionary<DateOnly, DailyBar>();
            if (existing?.Snapshot.Instrument.ListedFrom is { } listed && request.Start < listed)
                throw new ArgumentException($"下載開始日不得早於該市場掛牌日 {listed:yyyy-MM-dd}。", nameof(request));
            for (var number = request.Start.DayNumber; number <= request.End.DayNumber; number++)
            {
                token.ThrowIfCancellationRequested();
                var date = DateOnly.FromDayNumber(number);
                var known = coverage is not null && MarketDataMerge.Contains(coverage.VerifiedCalendarRanges, date) &&
                    (originalBars.TryGetValue(date, out var bar) ? MarketDataMerge.KeepBar(bar) ||
                        confirmedNoPriceDates.Contains(date) && MarketDataMerge.Contains(coverage.CheckedPriceRanges, date)
                        : !tradingDates.Contains(date) && MarketDataMerge.Contains(coverage.CheckedPriceRanges, date));
                if (known) reusedDays++;
                else if (usesDateRanges) missingDates.Add(new() { Start = date, End = date });
                else missingMonths.Add(new(date.Year, date.Month, 1));
            }
            downloadedMonths = missingMonths.Count;
            var ranges = MarketDataMerge.Normalize(missingDates);
            downloadedRanges = ranges.Count;
            var parts = usesDateRanges ? downloadedRanges : downloadedMonths;
            var unit = usesDateRanges ? "個日期區間" : "個月份";
            progress?.Report(new(0, parts, $"已略過 {reusedDays} 天；需補齊 {parts} {unit}"));
            if (parts == 0) return existing!;
            var downloaded = provider switch
            {
                IRangeHistoricalDataProvider rangeProvider => await rangeProvider.DownloadMissingRangesAsync(request,
                    ranges, progress, token).ConfigureAwait(false),
                IMonthlyHistoricalDataProvider official => await official.DownloadMissingMonthsAsync(request,
                    missingMonths.Order().ToArray(), progress, token).ConfigureAwait(false),
                _ => await provider.DownloadAsync(request, progress, token).ConfigureAwait(false)
            };
            // Store.UpdateAsync validates identity and merges under the same lock before an atomic replacement.
            if (downloaded.Snapshot.Instrument.Code != request.Code || downloaded.Snapshot.Instrument.Market != request.Market ||
                downloaded.Snapshot.SourceId != sourceId)
                throw new InvalidDataException("來源回傳不同來源、市場或股票的行情。");
            return FinMindHistoricalDataProvider.PreserveLegacyIdentity(existing, downloaded);
        }, cancellationToken).ConfigureAwait(false);
        var added = saved.Download.Snapshot.Bars.Count(bar => !originalBars.ContainsKey(bar.Date));
        var repaired = saved.Download.Snapshot.Bars.Count(bar => originalBars.TryGetValue(bar.Date, out var previous) && previous != bar);
        var completed = usesDateRanges ? downloadedRanges : downloadedMonths;
        progress?.Report(new(completed, completed,
            $"略過 {reusedDays} 天；補查 {completed} {(usesDateRanges ? "個日期區間" : "個月份")}，新增 {added} 筆、補齊 {repaired} 筆日線"));
        return new(saved.Download, saved.Path, reusedDays, downloadedMonths, added, repaired)
        { DownloadedRanges = downloadedRanges, UsesDateRanges = usesDateRanges };
    }
}
