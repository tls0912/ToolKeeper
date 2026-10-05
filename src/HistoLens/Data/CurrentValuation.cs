using HistoLens.Core;

namespace HistoLens.Data;

/// <summary>A latest published valuation, separate from historical prices and research features.</summary>
public sealed record CurrentValuation(string Market, string Code, decimal? PriceEarningsRatio,
    DateOnly DataDate, DateTimeOffset RetrievedAtUtc, string SourceId, string ResourceUrl);

public interface ICurrentValuationProvider
{
    /// <summary>Returns null when a current security cannot be identified; a missing P/E remains null.</summary>
    Task<CurrentValuation?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default);
}
