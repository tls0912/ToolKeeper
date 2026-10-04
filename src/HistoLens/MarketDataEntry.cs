namespace HistoLens;

/// <summary>Verified local inventory metadata; the saved coverage range does not imply every date has a price.</summary>
public sealed record MarketDataEntry(string Path, string Code, string Name,
    DateOnly CoverageStart, DateOnly CoverageEnd, DateOnly? FirstPriceDate, DateOnly? LastPriceDate,
    int BarCount, bool BlocksResearch, bool IsSynthetic = false, string Market = "TWSE", string SourceId = "");
