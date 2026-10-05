using HistoLens.Core;
using System.Text.Json.Serialization;

namespace HistoLens.Data;

public sealed record HistoricalDataRequest(string Code, DateOnly Start, DateOnly End, string Market = "TWSE");
public sealed record DownloadProgress(int Completed, int Total, string Message);
public sealed record HistoricalDataDownload(DataSnapshot Snapshot, IReadOnlyList<DataIssue> Diagnostics)
{
    // Kept outside DataSnapshot so old research snapshots and their fingerprints remain unchanged.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MarketDataCacheCoverage? CacheCoverage { get; init; }
    // Original source responses are separate from normalized bars and research fingerprints.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SourceResponseEvidence>? SourceEvidence { get; init; }
}

public sealed record SourceResponseEvidence(string ResourceUrl, string Dataset, DateTimeOffset RetrievedAtUtc,
    string Sha256, int RowCount, string RawJson);

/// <summary>Dates actually queried, including confirmed no-row dates; never inferred weekdays.</summary>
public sealed record MarketDataCacheCoverage
{
    public IReadOnlyList<DateRange> CheckedPriceRanges { get; init; } = [];
    public IReadOnlyList<DateRange> VerifiedCalendarRanges { get; init; } = [];
    // Explicit no-price rows from a successful response, not absent records or inferred suspensions.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DateOnly>? ConfirmedNoPriceDates { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DateRange>? KnownActionRanges { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DateRange>? ActionEvidenceGaps { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DateRange>? KnownComparisonRanges { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DateRange>? ComparisonEvidenceGaps { get; init; }
}

public interface IHistoricalDataProvider
{
    Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>An explicit acquisition source, independent from the security's market.</summary>
public interface IHistoricalDataSource
{
    string DataSourceId { get; }
}

/// <summary>Fetches exactly the missing date ranges, without expanding them to whole months.</summary>
public interface IRangeHistoricalDataProvider : IHistoricalDataProvider
{
    HistoricalDataRequest ValidateRequest(HistoricalDataRequest request);
    Task<HistoricalDataDownload> DownloadMissingRangesAsync(HistoricalDataRequest request,
        IReadOnlyList<DateRange> ranges, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>A market source that can validate and fill only the months missing from its local cache.</summary>
public interface IMonthlyHistoricalDataProvider : IHistoricalDataProvider
{
    HistoricalDataRequest ValidateRequest(HistoricalDataRequest request);
    Task<HistoricalDataDownload> DownloadMissingMonthsAsync(HistoricalDataRequest request,
        IReadOnlyList<DateOnly> months, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);
}
