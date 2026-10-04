using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using HistoLens.Core;

// Offline source compatibility probe, never a production importer or a live-store writer.
if (args.Length != 2) throw new ArgumentException("Usage: FinMindFit <probe directory> <existing official evidence root>");
var input = Path.GetFullPath(args[0]);
var official = Path.GetFullPath(args[1]);
var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input, "requests.json"))).RootElement;
JsonElement Read(string key)
{
    var record = manifest.EnumerateArray().Single(r => S(r, "key") == key);
    var bytes = File.ReadAllBytes(Path.Combine(input, key + ".json"));
    if (record.GetProperty("httpStatus").GetInt32() != 200 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(S(record, "sha256"), StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Failed response/hash: " + key);
    var json = JsonDocument.Parse(bytes).RootElement;
    if (json.GetProperty("status").GetInt32() != 200) throw new InvalidDataException("API error: " + key);
    return json.GetProperty("data");
}
static string S(JsonElement r, string f) => r.GetProperty(f).GetString()!;
static decimal N(JsonElement r, string f) => r.GetProperty(f).GetDecimal();
static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
static decimal Number(string s) => decimal.Parse(s.Replace(",", ""), CultureInfo.InvariantCulture);
static DateOnly Roc(string s)
{
    var p = s.Split('/');
    return new DateOnly(int.Parse(p[0], CultureInfo.InvariantCulture) + 1911, int.Parse(p[1], CultureInfo.InvariantCulture), int.Parse(p[2], CultureInfo.InvariantCulture));
}
static decimal? Price(JsonElement r, string f) => r.GetProperty(f).ValueKind == JsonValueKind.Number && N(r, f) > 0 ? N(r, f) : null;
var end = D("2025-12-31");
var calendar = Read("calendar").EnumerateArray().Select(r => D(S(r, "date"))).ToArray();
var identity = Read("identity");
var results = new List<object>();
var scans = new List<object>();
var comparisons = new List<object>();
var officialCalendar = new List<DateOnly>();
var officialBars = new Dictionary<DateOnly, JsonElement>();
var officialDir = Path.Combine(official, "history-twse-five-year");
if (Directory.Exists(officialDir))
{
    var records = File.ReadLines(Path.Combine(officialDir, "requests.jsonl"))
        .Select(line => JsonDocument.Parse(line).RootElement).ToDictionary(r => S(r, "key"));
    for (var month = new DateOnly(2021, 1, 1); month <= end; month = month.AddMonths(1))
        foreach (var kind in new[] { "calendar", "stock" })
        {
            var key = kind + "-" + month.ToString("yyyyMM", CultureInfo.InvariantCulture);
            var bytes = File.ReadAllBytes(Path.Combine(officialDir, key + ".raw"));
            if (records[key].GetProperty("httpStatus").GetInt32() != 200 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(S(records[key], "sha256"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Official evidence hash/status mismatch: " + key);
            var json = JsonDocument.Parse(bytes).RootElement;
            if (S(json, "stat") != "OK" || S(json, "date") != month.ToString("yyyyMMdd", CultureInfo.InvariantCulture)) throw new InvalidDataException("Official date/status mismatch");
            foreach (var r in json.GetProperty("data").EnumerateArray())
                if (kind == "calendar") officialCalendar.Add(Roc(r[0].GetString()!));
                else officialBars.Add(Roc(r[0].GetString()!), r);
        }
}
var calFive = calendar.Where(d => d >= D("2021-01-01") && d <= end).ToArray();
var officialCalendarMatches = officialCalendar.Count == 1214 && officialCalendar.Distinct().Count() == 1214 && officialCalendar.Order().SequenceEqual(calFive.Order());
foreach (var (code, market, isin) in new[] { ("2330", "TWSE", "TW0002330008"), ("6488", "TPEx", "TW0006488000") })
{
    // Two explicitly verified ISIN fixtures from the earlier official-identity investigation.
    // FinMind's current stock master is NOT treated as a complete historical identity registry.
    var rows = Read("price-" + code).EnumerateArray().ToArray();
    var info = identity.EnumerateArray().Where(r => S(r, "stock_id") == code).ToArray();
    var actions = Read("dividend-" + code).EnumerateArray().ToArray();
    if (info.Length == 0 || info.Any(r => !S(r, "type").Equals(market, StringComparison.OrdinalIgnoreCase)) || rows.Concat(actions).Any(r => S(r, "stock_id") != code))
        throw new InvalidDataException("Sample stock/market mismatch");
    foreach (var start in new[] { D("2021-01-01"), D("2016-01-01") })
    {
        var sample = rows.Where(r => D(S(r, "date")) >= start && D(S(r, "date")) <= end).ToArray();
        var dates = sample.Select(r => D(S(r, "date"))).ToArray();
        var cal = calendar.Where(d => d >= start && d <= end).ToArray();
        var badPrices = sample.Where(r => new[] { "open", "max", "min", "close" }.Any(f => Price(r, f) is null)).ToArray();
        var badRelations = sample.Except(badPrices).Where(r => N(r, "max") < Math.Max(N(r, "open"), N(r, "close")) || N(r, "min") > Math.Min(N(r, "open"), N(r, "close")) || N(r, "max") < N(r, "min")).ToArray();
        results.Add(new
        {
            code, market, requestedStart = start, requestedEnd = end, count = sample.Length, first = dates.Min(), last = dates.Max(),
            duplicateDates = dates.Length - dates.Distinct().Count(), ascending = dates.SequenceEqual(dates.Order()),
            sourceRowsOutsideRequest = rows.Count(r => D(S(r, "date")) < D("2016-01-01") || D(S(r, "date")) > end),
            invalidPrices = badPrices.Select(r => S(r, "date")), invalidOhlcRelations = badRelations.Select(r => S(r, "date")),
            invalidVolumes = sample.Count(r => N(r, "Trading_Volume") < 0 || N(r, "Trading_Volume") != decimal.Truncate(N(r, "Trading_Volume"))),
            zeroVolumes = sample.Count(r => N(r, "Trading_Volume") == 0),
            missingAgainstFinMindCalendar = cal.Except(dates), outsideFinMindCalendar = dates.Except(cal), calendarRows = cal.Length,
            calendarDuplicates = cal.Length - cal.Distinct().Count(), byYear = dates.GroupBy(d => d.Year).Select(g => new { year = g.Key, rows = g.Count() }),
            masterRows = info.Length, dividendsInFiveYears = actions.Length, eventDatesWithoutPrice = actions.Select(r => D(S(r, "date"))).Except(dates)
        });
        var verifiedCalendar = market == "TWSE" && start.Year == 2021 && officialCalendarMatches;
        var snapshot = new DataSnapshot
        {
            SnapshotId = "finmind-fit-" + code + "-" + start.Year, SourceId = "FinMind", DataVersion = "probe-v1", DataAsOf = end,
            RetrievedAtUtc = DateTimeOffset.Parse(S(manifest.EnumerateArray().Single(r => S(r, "key") == "price-" + code), "downloadedAtUtc"), CultureInfo.InvariantCulture),
            Instrument = new Instrument { InstrumentId = market + ":" + isin, Code = code, Name = S(info[0], "stock_name"), Market = market },
            Calendar = new TradingCalendar { Market = market, IsVerified = verifiedCalendar, Version = "FinMind/TaiwanStockTradingDate/probe", CoverageStart = start, CoverageEnd = end, TradingDates = cal },
            ActionCoverage = new CorporateActionCoverage { IsVerified = false, SourceId = "FinMind", CoverageStart = start, CoverageEnd = end, Version = "incomplete-events" },
            CorporateActions = actions.Select(r => new CorporateAction { EffectiveDate = D(S(r, "date")), Kind = S(r, "stock_or_cache_dividend"), SourceId = "FinMind/TaiwanStockDividendResult" }).ToArray(),
            Bars = sample.Select(r => new DailyBar { Date = D(S(r, "date")), Open = Price(r, "open"), High = Price(r, "max"), Low = Price(r, "min"), Close = Price(r, "close"), Volume = N(r, "Trading_Volume"), Turnover = N(r, "Trading_money"), SourceId = "FinMind", OriginalVolumeUnit = "shares" }).ToArray()
        };
        var definition = new SimilarityDefinition { Lookback = 20, ScanStart = start, ScanEnd = end, DataAsOf = end, SelectedIndicators = SimilarityIndicator.All, MinimumSimilarity = 60m };
        AddScan("strict-evidence", snapshot, definition);
        if (!verifiedCalendar)
            // Counterfactual algorithm test only: does NOT establish verified market coverage.
            AddScan("conditional-calendar-assumption-NOT-source-validation", snapshot with { Calendar = snapshot.Calendar with { IsVerified = true } }, definition);
    }
    if (code == "2330" && officialBars.Count > 0)
    {
        var map = rows.ToDictionary(r => D(S(r, "date")));
        comparisons.Add(new { code, checkedRows = officialBars.Count,
            ohlcMismatches = officialBars.Count(p => N(map[p.Key], "open") != Number(p.Value[3].GetString()!) || N(map[p.Key], "max") != Number(p.Value[4].GetString()!) || N(map[p.Key], "min") != Number(p.Value[5].GetString()!) || N(map[p.Key], "close") != Number(p.Value[6].GetString()!)),
            volumeShareMismatches = officialBars.Count(p => N(map[p.Key], "Trading_Volume") != Number(p.Value[1].GetString()!)),
            moneyTwdMismatches = officialBars.Count(p => N(map[p.Key], "Trading_money") != Number(p.Value[2].GetString()!)),
            tradeCountMismatches = officialBars.Count(p => N(map[p.Key], "Trading_turnover") != Number(p.Value[8].GetString()!)) });
    }
}
void AddScan(string mode, DataSnapshot snapshot, SimilarityDefinition definition)
{
    var beforeHash = SnapshotFingerprint.Compute(snapshot);
    var run = new SimilarityEngine().Run(snapshot, definition);
    if (beforeHash != SnapshotFingerprint.Compute(snapshot)) throw new InvalidDataException("Scan changed its raw input");
    if (run.IsAllowed && (run.CandidateCount != run.ComparableCount + run.Exclusions.Sum(e => e.Count) || run.ComparableCount != run.Matches.Count + run.BelowThresholdCount + run.OverlapExcludedCount))
        throw new InvalidDataException("Scan count invariant failed");
    if (run.IsAllowed && (run.Reference is null || !run.Diagnostics.Any(d => d.Code == "CorporateActionCoverageUnknown" && !d.BlocksResearch) ||
        !run.Reference.Warnings.Any(w => w.Code == "CorporateActionInWindow") || run.Matches.Any(m => m.Scores.Total is < 0m or > 100m)))
        throw new InvalidDataException("Expected raw-event/coverage warning or score invariant failed");
    scans.Add(new { code = snapshot.Instrument.Code, start = definition.ScanStart, mode, run.EngineVersion, run.IsAllowed,
        indicators = definition.SelectedIndicators.ToString(), definition.Lookback, definition.MinimumSimilarity, run.CandidateCount, run.ComparableCount,
        matchCount = run.Matches.Count, run.WarningCandidateCount, run.BelowThresholdCount, run.OverlapExcludedCount, run.Exclusions, run.BlockingReasons,
        referenceWarnings = run.Reference?.Warnings.Select(w => new { w.Code, w.Date }), diagnosticCodes = run.Diagnostics.Select(d => d.Code).Distinct(),
        matchesWithKnownEvent = run.Matches.Count(m => m.Features.Warnings.Any(w => w.Code == "CorporateActionInWindow")) });
}
var otcDir = Path.Combine(official, "tpex-personal-20261004");
if (Directory.Exists(otcDir))
{
    var records = File.ReadLines(Path.Combine(otcDir, "requests.jsonl")).Select(s => JsonDocument.Parse(s).RootElement).ToDictionary(r => S(r, "name"));
    JsonElement ReadOtc(string key)
    {
        var bytes = File.ReadAllBytes(Path.Combine(otcDir, key + ".raw"));
        if (records[key].GetProperty("status").GetInt32() != 200 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(S(records[key], "sha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TPEx cached evidence hash/status mismatch");
        return JsonDocument.Parse(bytes).RootElement.GetProperty("tables")[0].GetProperty("data");
    }
    var stock = Read("price-6488").EnumerateArray().ToDictionary(r => D(S(r, "date")));
    var matched = ReadOtc("stock-202507").EnumerateArray().Select(r => new
    {
        date = Roc(r[0].GetString()!),
        ohlcMatch = new[] { (3, "open"), (4, "max"), (5, "min"), (6, "close") }.All(p => Number(r[p.Item1].GetString()!) == N(stock[Roc(r[0].GetString()!)], p.Item2)),
        volumeDifferenceShares = Number(r[1].GetString()!) * 1000 - N(stock[Roc(r[0].GetString()!)], "Trading_Volume"),
        moneyDifferenceTwd = Number(r[2].GetString()!) * 1000 - N(stock[Roc(r[0].GetString()!)], "Trading_money"),
        tradeCountMatch = Number(r[8].GetString()!) == N(stock[Roc(r[0].GetString()!)], "Trading_turnover")
    }).ToArray();
    comparisons.Add(new { code = "6488", checkedRows = matched.Length, ohlcMismatches = matched.Count(r => !r.ohlcMatch),
        maxVolumeDifferenceShares = matched.Max(r => Math.Abs(r.volumeDifferenceShares)), maxMoneyDifferenceTwd = matched.Max(r => Math.Abs(r.moneyDifferenceTwd)),
        tradeCountMismatches = matched.Count(r => !r.tradeCountMatch), volumeUnitNote = "Official monthly data rounded to lots; this is NOT exact volume equality", sample = matched[0] });
    var officialEvent = ReadOtc("exright-20250716").EnumerateArray().Single(r => r[1].GetString() == "6488");
    var finEvent = Read("dividend-6488").EnumerateArray().Single(r => S(r, "date") == "2025-07-16");
    comparisons.Add(new { code = "6488", eventDate = "2025-07-16", typeMatch = officialEvent[8].GetString() == S(finEvent, "stock_or_cache_dividend"),
        cashDividendMatch = Number(officialEvent[13].GetString()!) == N(finEvent, "stock_and_cache_dividend"),
        beforePriceMatch = Number(officialEvent[3].GetString()!) == N(finEvent, "before_price"), referencePriceMatch = Number(officialEvent[4].GetString()!) == N(finEvent, "after_price") });
}
var noPriceCases = new[] { "no-price-2317", "no-price-9929" }.SelectMany(key => Read(key).EnumerateArray().Where(r => N(r, "close") == 0)
    .Select(r => new { code = S(r, "stock_id"), date = S(r, "date"), volume = N(r, "Trading_Volume"), allOhlcZero = new[] { "open", "max", "min", "close" }.All(f => N(r, f) == 0), normalizedClose = Price(r, "close") })).ToArray();
var summary = new { generatedAtUtc = DateTimeOffset.UtcNow, testKind = "Actual unmodified Core; not UI/provider integration",
    officialFiveYearTwseCalendarMatches = officialCalendarMatches, rawCalendarRows = calendar.Length, rawCalendarLatest = calendar.Max(),
    calendarNote = "Future planned dates exist; filter to completed coverage. TPEx-specific and pre-2021 coverage not independently verified.",
    results, comparisons, scans, noPriceCases,
    per = new[] { "2330", "6488" }.Select(code => new { code, rows = Read("per-" + code).GetArrayLength(), latest = Read("per-" + code).EnumerateArray().Select(r => S(r, "date")).Max() }) };
var output = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
File.WriteAllText(Path.Combine(input, "fit-summary.json"), output);
Console.WriteLine(output);
