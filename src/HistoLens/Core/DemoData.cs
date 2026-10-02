namespace HistoLens.Core;

public static class DemoData
{
    /// <summary>A deterministic invented instrument and calendar, never a proxy for TWSE/TPEx history.</summary>
    public static DataSnapshot Create()
    {
        const string source = "HistoLens.SyntheticGenerator.v1";
        var dates = new List<DateOnly>();
        var day = new DateOnly(2023, 1, 2);
        while (dates.Count < 780)
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) dates.Add(day);
            day = day.AddDays(1);
        }
        var bars = new List<DailyBar>(dates.Count);
        var previous = 100m;
        for (var i = 0; i < dates.Count; i++)
        {
            if (i == 390) previous /= 2;
            var phase = i % 90;
            var movement = phase switch
            {
                < 20 => -0.4m, < 45 => 0.55m, < 55 => -0.3m, < 75 => 0.4m, _ => -0.2m
            };
            var open = previous + (i % 3 - 1) * 0.1m;
            var close = previous + movement + (i % 7 - 3) * 0.03m;
            bars.Add(new DailyBar
            {
                Date = dates[i], Open = open, High = Math.Max(open, close) + 0.2m + i % 5 * 0.1m,
                Low = Math.Min(open, close) - 0.2m - i % 4 * 0.1m, Close = close,
                Volume = (100_000m + i % 13 * 5_000m) * (i % 17 == 0 ? 3 : 1), SourceId = source
            });
            previous = close;
        }
        var snapshot = new DataSnapshot
        {
            Instrument = new Instrument
            {
                InstrumentId = "HistoLens:SYNTHETIC:DEMO-006:2023", Code = "DEMO-006",
                Name = "合成示範股（非真實行情）", Market = "SYNTHETIC", SecurityType = SecurityType.Synthetic,
                ListedFrom = dates[0]
            },
            SourceId = source, DataVersion = "synthetic-v1", IsSynthetic = true,
            DataAsOf = dates[^1], RetrievedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Calendar = new TradingCalendar
            {
                Market = "SYNTHETIC", Version = "synthetic-weekdays-v1", IsVerified = true,
                CoverageStart = dates[0], CoverageEnd = dates[^1], TradingDates = dates.ToArray()
            },
            ActionCoverage = new CorporateActionCoverage
            {
                IsVerified = true, SourceId = source, Version = "synthetic-actions-v1",
                CoverageStart = dates[0], CoverageEnd = dates[^1]
            },
            CorporateActions =
            [
                new CorporateAction { EffectiveDate = dates[390], Kind = "SyntheticSplit2For1", SourceId = source }
            ],
            Bars = bars.ToArray()
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        return snapshot with { SnapshotId = "synthetic-" + hash[..16], ContentHash = hash };
    }
}
