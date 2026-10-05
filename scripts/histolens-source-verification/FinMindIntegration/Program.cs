using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using HistoLens;
using HistoLens.Core;
using HistoLens.Data;

// Explicit, bounded live integration check. Never writes to the user's active profile.
if (args.Length != 2 || args[0] != "--live")
    throw new ArgumentException("Usage: FinMindIntegration --live <new evidence directory under repository artifacts>");
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "ToolKeeper.sln"))) root = root.Parent;
if (root is null) throw new InvalidOperationException("Run this project from its repository build output.");
var output = Path.GetFullPath(args[1]);
var artifacts = Path.Combine(root.FullName, "artifacts") + Path.DirectorySeparatorChar;
if (!output.StartsWith(artifacts, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
    throw new ArgumentException("Choose a new directory strictly under repository artifacts; existing evidence is preserved.");
Directory.CreateDirectory(output);
var handler = new RecordingHandler();
using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
var provider = new FinMindHistoricalDataProvider(client);
var store = new MarketDataStore(Path.Combine(output, "market-data"));
var downloader = new CachedMarketDataDownloader(provider, store);
var results = new List<object>();
var auxiliary = new List<object>();
Exception? failure = null;
try
{
    var catalog = await new FinMindStockCatalogProvider(client).GetAsync(timeout.Token);
    auxiliary.Add(new { kind = "stock-list", rows = catalog.Entries.Count, catalog.UpdatedAtUtc });
    foreach (var (code, market) in new[] { ("2330", "TWSE"), ("6488", "TPEx") })
    {
        Instrument? instrument = null;
        foreach (var year in new[] { 2021, 2016 })
        {
            var request = new HistoricalDataRequest(code, new(year, 1, 1), new(2025, 12, 31), market);
            var before = handler.Requests.Count;
            var result = await downloader.DownloadAsync(request, new LogProgress(), timeout.Token);
            var snapshot = (await MarketDataStore.LoadAsync(result.Path, timeout.Token)).Snapshot;
            instrument = snapshot.Instrument;
            var scan = new SimilarityEngine().Run(snapshot, new()
            {
                Lookback = 20, ScanStart = request.Start, ScanEnd = request.End, DataAsOf = request.End,
                MinimumSimilarity = 60, SelectedIndicators = SimilarityIndicator.All
            }, timeout.Token);
            var priceRequests = handler.Requests.Skip(before).Where(r => r.Url.Contains("dataset=TaiwanStockPrice&", StringComparison.Ordinal)).ToArray();
            var expectedEnd = year == 2021 ? "2025-12-31" : "2020-12-31";
            if (priceRequests.Length != 1 || !priceRequests[0].Url.Contains($"start_date={year}-01-01", StringComparison.Ordinal) ||
                !priceRequests[0].Url.Contains("end_date=" + expectedEnd, StringComparison.Ordinal))
                throw new InvalidDataException("The live provider did not request exactly the missing price range.");
            if (snapshot.Bars.Count != (year == 2021 ? 1214 : 2438) || !snapshot.Calendar.IsVerified || !scan.IsAllowed)
                throw new InvalidDataException("Unexpected sample coverage or blocked scan: " + string.Join(";", scan.BlockingReasons));
            var events = snapshot.CorporateActions.Where(a => a.EffectiveDate >= request.Start && a.EffectiveDate <= request.End).ToArray();
            if (events.Length == 0 || events.Any(e => !snapshot.Calendar.TradingDates.Contains(e.EffectiveDate)))
                throw new InvalidDataException("Expected dated events aligned with the market calendar.");
            var bytes = await File.ReadAllBytesAsync(result.Path, timeout.Token);
            var requestsBeforeReuse = handler.Requests.Count;
            var reused = await downloader.DownloadAsync(request, null, timeout.Token);
            var reusedBytes = await File.ReadAllBytesAsync(result.Path, timeout.Token);
            if (handler.Requests.Count != requestsBeforeReuse || reused.DownloadedRanges != 0 ||
                !bytes.SequenceEqual(reusedBytes))
                throw new InvalidDataException("A complete cached range performed network access or changed the saved bytes.");
            results.Add(new
            {
                code, market, request.Start, request.End, source = snapshot.SourceId, snapshot.Instrument.InstrumentId,
                rows = snapshot.Bars.Count, first = snapshot.Bars.Min(b => b.Date), last = snapshot.Bars.Max(b => b.Date),
                validPriceRows = snapshot.Bars.Count(b => b.Open > 0 && b.High > 0 && b.Low > 0 && b.Close > 0),
                result.DownloadedRanges, result.ReusedDays, priceRequests, requestCount = requestsBeforeReuse - before,
                fullCacheRepeatRequests = 0, cacheBytesUnchanged = true,
                calendarVerified = snapshot.Calendar.IsVerified, calendarRows = snapshot.Calendar.TradingDates.Count,
                eventCount = events.Length, actionCoverageVerified = snapshot.ActionCoverage.IsVerified,
                scan.IsAllowed, scan.CandidateCount, scan.ComparableCount, matches = scan.Matches.Count, scan.Exclusions,
                sourceEvidenceCount = result.Download.SourceEvidence?.Count,
                fileSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes))
            });
            Console.WriteLine($"PASS {market} {code} {year}..2025: {snapshot.Bars.Count} rows, {scan.ComparableCount} comparable, {scan.Matches.Count} matches; cache repeat=0 requests");
        }
        var quote = await new FinMindValuationProvider(client).GetLatestAsync(instrument!, timeout.Token);
        if (quote is null || quote.SourceId != "FinMind/TaiwanStockPER") throw new InvalidDataException("Sample valuation was unavailable.");
        auxiliary.Add(new { kind = "valuation", quote.Market, quote.Code, quote.PriceEarningsRatio, quote.DataDate, quote.SourceId });
    }
}
catch (Exception error) { failure = error; throw; }
finally
{
    var json = new JsonSerializerOptions { WriteIndented = true };
    await File.WriteAllTextAsync(Path.Combine(output, "requests.json"), JsonSerializer.Serialize(handler.Requests, json));
    await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
    {
        finishedAtUtc = DateTimeOffset.UtcNow, success = failure is null, error = failure?.ToString(), results, auxiliary
    }, json));
}

sealed class LogProgress : IProgress<DownloadProgress>
{
    public void Report(DownloadProgress value) => Console.WriteLine(value.Message);
}
sealed record RequestRecord(string Url, DateTimeOffset RequestedAtUtc, int? HttpStatus, string? Error);
sealed class RecordingHandler : DelegatingHandler
{
    public List<RequestRecord> Requests { get; } = [];
    public RecordingHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { Scheme: "https", Host: "api.finmindtrade.com", AbsolutePath: "/api/v4/data" })
            throw new InvalidOperationException("Only the FinMind public data endpoint is allowed in this verification.");
        var time = DateTimeOffset.UtcNow;
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            Requests.Add(new(request.RequestUri!.AbsoluteUri, time, (int)response.StatusCode, null));
            return response;
        }
        catch (Exception error) { Requests.Add(new(request.RequestUri!.AbsoluteUri, time, null, error.Message)); throw; }
    }
}
