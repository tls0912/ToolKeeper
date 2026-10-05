using System.IO;
using System.Security.Cryptography;
using System.Text;
using HistoLens.Core;

namespace HistoLens.Data;

/// <summary>Conservative union of a single security's downloads. No prices or market days are invented.</summary>
internal static class MarketDataMerge
{
    internal static string IdentityKey(Instrument instrument) => string.Join("\n", instrument.Market,
        instrument.InstrumentId, instrument.Code, instrument.Currency, instrument.SecurityType,
        instrument.ListedFrom?.ToString("yyyy-MM-dd"), instrument.ListedUntil?.ToString("yyyy-MM-dd"));

    internal static MarketDataCacheCoverage Coverage(HistoricalDataDownload download) => download.CacheCoverage ?? new()
    {
        // V1 files were produced only after successful whole-range calendar/price requests.
        CheckedPriceRanges = [new() { Start = download.Snapshot.Calendar.CoverageStart, End = download.Snapshot.DataAsOf }],
        VerifiedCalendarRanges = download.Snapshot.Calendar.IsVerified
            ? [new() { Start = download.Snapshot.Calendar.CoverageStart, End = download.Snapshot.Calendar.CoverageEnd }] : []
    };

    internal static bool KeepBar(DailyBar bar) => bar.Status is TradingStatus.Suspended or TradingStatus.NoTrading or TradingStatus.ReferenceOnly ||
        SnapshotValidator.GetBarReasons(bar, false).Count == 0;

    internal static HistoricalDataDownload Merge(IEnumerable<HistoricalDataDownload> downloads)
    {
        var items = downloads.ToArray();
        if (items.Length == 0) throw new ArgumentException("No market data to merge.", nameof(downloads));
        var first = items[0];
        var sourceId = first.Snapshot.SourceId;
        if (items.Any(item => IdentityKey(item.Snapshot.Instrument) != IdentityKey(first.Snapshot.Instrument) ||
            item.Snapshot.SourceId != first.Snapshot.SourceId))
            throw new InvalidDataException("不同市場、股票或穩定身份的行情不可合併。");
        if (items.Length == 1) return first;
        var sourceEvidence = items.All(item => item.SourceEvidence is null) ? null : items.SelectMany(item => item.SourceEvidence ?? [])
            .DistinctBy(evidence => (evidence.Dataset, evidence.ResourceUrl, evidence.Sha256.ToUpperInvariant())).ToArray();
        if (items.All(item => item.Snapshot.ContentHash == first.Snapshot.ContentHash &&
            item.Diagnostics.SequenceEqual(first.Diagnostics) && SameCoverage(item.CacheCoverage, first.CacheCoverage)))
            return (sourceEvidence ?? []).SequenceEqual(first.SourceEvidence ?? []) ? first : first with { SourceEvidence = sourceEvidence };

        var snapshots = items.Select(item => item.Snapshot).ToArray();
        var start = snapshots.Min(snapshot => snapshot.Calendar.CoverageStart);
        var end = snapshots.Max(snapshot => snapshot.DataAsOf);
        var calendarRanges = Normalize(items.SelectMany(item => Coverage(item).VerifiedCalendarRanges));
        var calendarGaps = Complement(calendarRanges, start, end);
        var dates = snapshots.SelectMany(snapshot => snapshot.Calendar.TradingDates).Distinct().Order().ToArray();
        var bars = new Dictionary<DateOnly, DailyBar>();
        var conflicts = new HashSet<DateOnly>();
        foreach (var item in items)
        foreach (var bar in item.Snapshot.Bars)
        {
            if (!bars.TryGetValue(bar.Date, out var previous)) bars.Add(bar.Date, bar);
            else if (!KeepBar(previous) && KeepBar(bar)) bars[bar.Date] = bar;
            else if (KeepBar(previous) && KeepBar(bar) && previous != bar) conflicts.Add(bar.Date);
        }

        var actions = snapshots.SelectMany(snapshot => snapshot.CorporateActions).ToList();
        // New source evidence based on a changed price must not silently prove comparability of the retained old price.
        foreach (var date in conflicts)
        {
            actions.Add(new() { EffectiveDate = date, Kind = "CachedPriceConflict", SourceId = sourceId + "/cache" });
            var next = dates.FirstOrDefault(candidate => candidate > date);
            if (next != default) actions.Add(new() { EffectiveDate = next, Kind = "CachedPriceConflict", SourceId = sourceId + "/cache" });
        }
        var actionCoverage = snapshots.Select(snapshot => snapshot.ActionCoverage).ToArray();
        var knownActions = Normalize(items.SelectMany(item => item.CacheCoverage?.KnownActionRanges ??
            (item.Snapshot.ActionCoverage.IsVerified ? Complement(item.Snapshot.ActionCoverage.Gaps,
                item.Snapshot.ActionCoverage.CoverageStart, item.Snapshot.ActionCoverage.CoverageEnd) : [])));
        var actionEvidenceGaps = Normalize(items.SelectMany(item => item.CacheCoverage?.ActionEvidenceGaps ?? item.Snapshot.ActionCoverage.Gaps));
        var actionGaps = Normalize(Complement(knownActions, start, end).Concat(actionEvidenceGaps));
        var comparisons = snapshots.Select(snapshot => snapshot.ComparabilityCoverage).OfType<PriceComparisonCoverage>().ToArray();
        var knownComparisons = Normalize(items.SelectMany(item => item.CacheCoverage?.KnownComparisonRanges ??
            (item.Snapshot.ComparabilityCoverage is { IsVerified: true } coverage
                ? Complement(coverage.Gaps, coverage.CoverageStart, coverage.CoverageEnd) : [])));
        var comparisonEvidenceGaps = Normalize(items.SelectMany(item => item.CacheCoverage?.ComparisonEvidenceGaps ??
            item.Snapshot.ComparabilityCoverage?.Gaps ?? []));

        var snapshot = first.Snapshot with
        {
            SnapshotId = "", ContentHash = "", DataAsOf = end,
            RetrievedAtUtc = snapshots.Max(value => value.RetrievedAtUtc),
            DataVersion = Version(snapshots.Select(value => value.DataVersion), sourceId),
            Calendar = first.Snapshot.Calendar with
            {
                IsVerified = calendarGaps.Count == 0, CoverageStart = start, CoverageEnd = end,
                TradingDates = dates, Version = Version(snapshots.Select(value => value.Calendar.Version), sourceId)
            },
            Bars = bars.Values.OrderBy(bar => bar.Date).ToArray(),
            CorporateActions = actions.Distinct().OrderBy(action => action.EffectiveDate)
                .ThenBy(action => action.Kind, StringComparer.Ordinal).ThenBy(action => action.SourceId, StringComparer.Ordinal).ToArray(),
            ActionCoverage = new()
            {
                IsVerified = actionCoverage.Any(coverage => coverage.IsVerified), CoverageStart = start, CoverageEnd = end,
                SourceId = string.Join("|", actionCoverage.Select(coverage => coverage.SourceId).Distinct().Order(StringComparer.Ordinal)),
                Version = Version(actionCoverage.Select(coverage => coverage.Version), sourceId), Gaps = actionGaps
            },
            ComparabilityCoverage = comparisons.Length == 0 ? null : new()
            {
                IsVerified = true, CoverageStart = start, CoverageEnd = end,
                SourceId = string.Join("|", comparisons.Select(coverage => coverage.SourceId).Distinct().Order(StringComparer.Ordinal)),
                Version = Version(comparisons.Select(coverage => coverage.Version), sourceId),
                Gaps = Normalize(Complement(knownComparisons, start, end).Concat(comparisonEvidenceGaps))
            }
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        snapshot = snapshot with { ContentHash = hash, SnapshotId = $"{sourceId.ToLowerInvariant()}-{snapshot.Instrument.Code}-cache-{hash[..16]}" };
        // Recompute only structural diagnostics. Provider notes, unavailable sources and action evidence are retained.
        var generatedCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "CalendarUnverified", "CalendarEmpty", "CorporateActionCoverageUnknown", "CacheCalendarGap",
            "MissingBar", "MissingPrice", "InvalidBar", "NonTradingBar", "MissingVolume"
        };
        var diagnostics = items.SelectMany(item => item.Diagnostics).Where(issue => !generatedCodes.Contains(issue.Code))
            .Concat(SnapshotValidator.Validate(snapshot)).ToList();
        diagnostics.AddRange(calendarGaps.Select(gap => new DataIssue
        {
            Code = "CacheCalendarGap", Date = gap.Start, BlocksResearch = true,
            Message = $"本機尚未確認 {gap.Start:yyyy-MM-dd}–{gap.End:yyyy-MM-dd} 的市場日曆；請補齊此區間後再掃描。"
        }));
        diagnostics.AddRange(conflicts.Select(date => new DataIssue
        {
            Code = "CachedPriceConflict", Date = date,
            Message = "來源此次回傳與已存日線不同；保留原日線，並阻擋跨越此價差證據的窗口。"
        }));
        return new(snapshot, diagnostics.Distinct().ToArray())
        {
            SourceEvidence = sourceEvidence,
            CacheCoverage = new()
            {
                CheckedPriceRanges = Normalize(items.SelectMany(item => Coverage(item).CheckedPriceRanges)),
                VerifiedCalendarRanges = calendarRanges,
                ConfirmedNoPriceDates = items.All(item => item.CacheCoverage?.ConfirmedNoPriceDates is null) ? null :
                    items.SelectMany(item => item.CacheCoverage?.ConfirmedNoPriceDates ?? [])
                        .Distinct().Where(date => bars.TryGetValue(date, out var bar) && !KeepBar(bar))
                        .Order().ToArray(),
                KnownActionRanges = knownActions, ActionEvidenceGaps = actionEvidenceGaps,
                KnownComparisonRanges = knownComparisons, ComparisonEvidenceGaps = comparisonEvidenceGaps
            }
        };
    }

    internal static IReadOnlyList<DateRange> Normalize(IEnumerable<DateRange> ranges)
    {
        var result = new List<DateRange>();
        foreach (var range in ranges.OrderBy(range => range.Start).ThenBy(range => range.End))
        {
            if (result.Count == 0 || (long)range.Start.DayNumber > (long)result[^1].End.DayNumber + 1) result.Add(range);
            else if (range.End > result[^1].End) result[^1] = result[^1] with { End = range.End };
        }
        return result;
    }

    internal static IReadOnlyList<DateRange> Complement(IEnumerable<DateRange> ranges, DateOnly start, DateOnly end)
    {
        var result = new List<DateRange>();
        var next = start.DayNumber;
        foreach (var range in Normalize(ranges))
        {
            if (range.End < start || range.Start > end) continue;
            if (range.Start.DayNumber > next) result.Add(new() { Start = DateOnly.FromDayNumber(next), End = range.Start.AddDays(-1) });
            next = Math.Max(next, range.End.DayNumber + 1);
        }
        if (next <= end.DayNumber) result.Add(new() { Start = DateOnly.FromDayNumber(next), End = end });
        return result;
    }

    internal static bool Contains(IEnumerable<DateRange> ranges, DateOnly date) => ranges.Any(range => range.Start <= date && range.End >= date);
    private static bool SameCoverage(MarketDataCacheCoverage? first, MarketDataCacheCoverage? second) =>
        first is null || second is null ? first is null && second is null :
        first.CheckedPriceRanges.SequenceEqual(second.CheckedPriceRanges) &&
        first.VerifiedCalendarRanges.SequenceEqual(second.VerifiedCalendarRanges) &&
        (first.ConfirmedNoPriceDates ?? []).SequenceEqual(second.ConfirmedNoPriceDates ?? []) &&
        (first.KnownActionRanges ?? []).SequenceEqual(second.KnownActionRanges ?? []) &&
        (first.ActionEvidenceGaps ?? []).SequenceEqual(second.ActionEvidenceGaps ?? []) &&
        (first.KnownComparisonRanges ?? []).SequenceEqual(second.KnownComparisonRanges ?? []) &&
        (first.ComparisonEvidenceGaps ?? []).SequenceEqual(second.ComparisonEvidenceGaps ?? []);
    private static string Version(IEnumerable<string> versions, string sourceId) => sourceId.ToLowerInvariant() + "-cache-v2/" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", versions.Distinct().Order(StringComparer.Ordinal)))));
}
