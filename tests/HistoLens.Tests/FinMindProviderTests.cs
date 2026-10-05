using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

/// <summary>Invented contract fixtures, never copied historical market prices.</summary>
public sealed class FinMindProviderTests
{
    private static readonly HistoricalDataRequest Request = new("2330", new(2025, 1, 2), new(2025, 1, 6));

    [Theory]
    [InlineData("2330", "TWSE")]
    [InlineData("6488", "TPEx")]
    public async Task UsesOnlyFinMindWithSourceScopedIdentityAndRawPriceUnits(string code, string market)
    {
        var urls = new List<Uri>();
        using var http = Client(uri => { urls.Add(uri); return null; });
        var result = await Provider(http).DownloadAsync(Request with { Code = code, Market = market });
        Assert.StartsWith($"FinMind:{market}:{code}:", result.Snapshot.Instrument.InstrumentId);
        Assert.Equal(market, result.Snapshot.Instrument.Market);
        Assert.Equal("FinMind", result.Snapshot.SourceId);
        Assert.Null(result.Snapshot.Instrument.ListedFrom);
        Assert.Contains(result.Diagnostics, issue => issue.Code == "FinMindSecurityLifetimeUnverified");
        var bar = result.Snapshot.Bars[0];
        Assert.Equal(1_234_567m, bar.Volume);
        Assert.Equal(123_456_700m, bar.Turnover);
        Assert.Equal(100m, bar.Close);
        Assert.Equal("shares", bar.OriginalVolumeUnit);
        Assert.Equal("FinMind", bar.SourceId);
        Assert.Single(urls, uri => Dataset(uri) == "TaiwanStockPrice");
        Assert.DoesNotContain(urls, uri => uri.AbsolutePath.Contains("STOCK_DAY") || uri.AbsolutePath.Contains("tradingStock"));
        Assert.Equal(new DateOnly(2025, 1, 6), result.Snapshot.DataAsOf);
        Assert.Equal(SnapshotFingerprint.Compute(result.Snapshot), result.Snapshot.ContentHash);
        Assert.False(result.Snapshot.ActionCoverage.IsVerified);
        Assert.Equal(new DateRange { Start = Request.Start, End = Request.End }, Assert.Single(result.Snapshot.ActionCoverage.Gaps));
        Assert.Null(result.Snapshot.ComparabilityCoverage);
        Assert.All(result.Snapshot.Calendar.TradingDates, date => Assert.InRange(date, Request.Start, Request.End));
        Assert.True(result.Snapshot.Calendar.IsVerified);
        Assert.NotNull(result.SourceEvidence);
        Assert.Equal(7, result.SourceEvidence.Count);
        Assert.All(result.SourceEvidence, evidence =>
        {
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.RawJson))), evidence.Sha256);
            using var json = JsonDocument.Parse(evidence.RawJson);
            Assert.Equal(evidence.RowCount, json.RootElement.GetProperty("data").GetArrayLength());
        });
        Assert.All(urls, uri =>
        {
            Assert.Equal("api.finmindtrade.com", uri.Host);
            Assert.DoesNotContain("token", uri.Query);
            Assert.DoesNotContain("device", uri.Query);
        });
    }

    [Fact]
    public async Task QueriesOnlyPreciseMissingRangesAndCombinesAdjacentDates()
    {
        var urls = new List<Uri>();
        using var http = Client(uri => { urls.Add(uri); return null; });
        var result = await Provider(http).DownloadMissingRangesAsync(Request,
            [new() { Start = new(2025, 1, 2), End = new(2025, 1, 2) },
             new() { Start = new(2025, 1, 3), End = new(2025, 1, 3) },
             new() { Start = new(2025, 1, 6), End = new(2025, 1, 6) }], null, default);
        var prices = urls.Where(uri => Dataset(uri) == "TaiwanStockPrice").ToArray();
        Assert.Equal(2, prices.Length);
        Assert.Equal("2025-01-02", Query(prices[0], "start_date"));
        Assert.Equal("2025-01-03", Query(prices[0], "end_date"));
        Assert.Equal("2025-01-06", Query(prices[1], "start_date"));
        Assert.Equal("2025-01-06", Query(prices[1], "end_date"));
        Assert.Equal(2, result.CacheCoverage!.CheckedPriceRanges.Count);
        Assert.Single(urls, uri => Dataset(uri) == "TaiwanStockInfo");
        Assert.Single(urls, uri => Dataset(uri) == "TaiwanStockTradingDate");
        Assert.Single(urls, uri => Dataset(uri) == "TaiwanStockParValueChange");
        Assert.Single(urls, uri => Dataset(uri) == "TaiwanStockSplitPrice");
    }

    [Fact]
    public async Task KeepsNoPriceRowsSeparateFromMissingRecordsAndSuspensions()
    {
        using var http = Client(uri => Dataset(uri) == "TaiwanStockPrice" ? Prices(uri, rows =>
        {
            rows.RemoveAt(1);
            foreach (var field in new[] { "open", "max", "min", "close" }) rows[0][field] = 0m;
            rows[0]["Trading_Volume"] = 451m; // Valid source row, but no announced OHLC.
        }) : null);
        var result = await Provider(http).DownloadAsync(Request);
        Assert.Equal(3, result.Snapshot.Calendar.TradingDates.Count);
        Assert.Equal(2, result.Snapshot.Bars.Count);
        Assert.Null(result.Snapshot.Bars[0].Open);
        Assert.Null(result.Snapshot.Bars[0].Close);
        Assert.Equal(TradingStatus.Traded, result.Snapshot.Bars[0].Status);
        Assert.Equal(new DateOnly(2025, 1, 2), Assert.Single(result.CacheCoverage!.ConfirmedNoPriceDates!));
        Assert.Contains(result.Diagnostics, issue => issue.Code == "MissingBar" && issue.Date == new DateOnly(2025, 1, 3));
        Assert.Contains(result.Diagnostics, issue => issue.Code == "MissingPrice" && issue.Date == new DateOnly(2025, 1, 2));
        Assert.DoesNotContain(result.Snapshot.Bars, bar => bar.Status == TradingStatus.Suspended);
    }

    [Fact]
    public async Task MapsEventEffectiveDatesWithoutReplacingRawPricesWithReferencePrices()
    {
        using var http = Client(uri => Dataset(uri) switch
        {
            "TaiwanStockDividendResult" => Success([new Dictionary<string, object?>
            {
                ["date"] = "2025-01-03", ["stock_id"] = "2330", ["stock_or_cache_dividend"] = "息",
                ["before_price"] = 100m, ["after_price"] = 94m, ["stock_and_cache_dividend"] = 6m
            }]),
            "TaiwanStockCapitalReductionReferencePrice" => Success([new Dictionary<string, object?>
            {
                ["date"] = "2025-01-06", ["stock_id"] = "2330", ["ReasonforCapitalReduction"] = "Cash refund",
                ["ClosingPriceonTheLastTradingDay"] = 100m, ["PostReductionReferencePrice"] = 200m
            }]),
            "TaiwanStockParValueChange" => Success([new Dictionary<string, object?>
            {
                ["date"] = "2025-01-02", ["stock_id"] = "2330", ["before_close"] = 100m, ["after_ref_close"] = 50m
            }]),
            "TaiwanStockSplitPrice" => Success([new Dictionary<string, object?>
            {
                ["date"] = "2025-01-06", ["stock_id"] = "2330", ["type"] = "反分割", ["before_price"] = 10m, ["after_price"] = 100m
            }, new Dictionary<string, object?>
            {
                ["date"] = "2026-01-02", ["stock_id"] = "6488", ["type"] = "分割", ["before_price"] = 100m, ["after_price"] = 10m
            }]),
            _ => null
        });
        var result = await Provider(http).DownloadAsync(Request);
        Assert.Equal(4, result.Snapshot.CorporateActions.Count);
        Assert.Contains(result.Snapshot.CorporateActions, action => action.Kind == "CashDividend" && action.EffectiveDate == new DateOnly(2025, 1, 3));
        Assert.Contains(result.Snapshot.CorporateActions, action => action.Kind == "CapitalReduction:Cash refund");
        Assert.Contains(result.Snapshot.CorporateActions, action => action.Kind == "DenominationChange");
        Assert.Contains(result.Snapshot.CorporateActions, action => action.Kind == "SplitOrDenominationChange:反分割");
        Assert.Equal(101m, result.Snapshot.Bars[1].Close);
        Assert.All(result.Snapshot.CorporateActions, action => Assert.StartsWith("FinMind/", action.SourceId));
    }

    [Theory]
    [InlineData(402)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task StopsAllLaterRequestsAfterAnEventAccessLimitAndRetainsPrices(int status)
    {
        var datasets = new List<string>();
        using var http = Client(uri =>
        {
            datasets.Add(Dataset(uri));
            return Dataset(uri) == "TaiwanStockDividendResult" ? new((HttpStatusCode)status) : null;
        });
        var result = await Provider(http).DownloadAsync(Request);
        Assert.Equal(3, result.Snapshot.Bars.Count);
        Assert.Equal("TaiwanStockDividendResult", datasets.Last());
        Assert.DoesNotContain("TaiwanStockSuspended", datasets);
        Assert.Contains(result.Diagnostics, issue => issue.Code == "FinMindActionSourceUnavailable" && !issue.BlocksResearch);
        Assert.False(result.Snapshot.ActionCoverage.IsVerified);
    }

    [Fact]
    public async Task StopsLaterRequestsOnAnApiEnvelopeAccessLimit()
    {
        var datasets = new List<string>();
        using var http = Client(uri =>
        {
            datasets.Add(Dataset(uri));
            return Dataset(uri) == "TaiwanStockDividendResult" ? Json(new { status = 402, msg = "limit", data = Array.Empty<object>() }) : null;
        });
        var result = await Provider(http).DownloadAsync(Request);
        Assert.Equal("TaiwanStockDividendResult", datasets.Last());
        Assert.Equal(3, result.Snapshot.Bars.Count);
    }

    [Theory]
    [InlineData("wrong-stock")]
    [InlineData("duplicate-date")]
    [InlineData("outside-request")]
    [InlineData("outside-calendar")]
    [InlineData("missing-field")]
    [InlineData("invalid-number")]
    [InlineData("negative-price")]
    [InlineData("negative-volume")]
    [InlineData("fractional-volume")]
    public async Task RejectsInvalidPriceContracts(string mutation)
    {
        using var http = Client(uri => Dataset(uri) == "TaiwanStockPrice" ? Prices(uri, rows =>
        {
            switch (mutation)
            {
                case "wrong-stock": rows[0]["stock_id"] = "6488"; break;
                case "duplicate-date": rows[1]["date"] = rows[0]["date"]; break;
                case "outside-request": rows[0]["date"] = "2025-01-01"; break;
                case "outside-calendar": rows[0]["date"] = "2025-01-04"; break;
                case "missing-field": rows[0].Remove("Trading_money"); break;
                case "invalid-number": rows[0]["close"] = "oops"; break;
                case "negative-price": rows[0]["close"] = -1m; break;
                case "negative-volume": rows[0]["Trading_Volume"] = -1m; break;
                case "fractional-volume": rows[0]["Trading_Volume"] = 1.5m; break;
            }
        }) : null);
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).DownloadAsync(Request));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    [InlineData("bad-date")]
    public async Task RejectsInvalidIndependentCalendar(string mutation)
    {
        var priceCalled = false;
        using var http = Client(uri =>
        {
            if (Dataset(uri) == "TaiwanStockPrice") priceCalled = true;
            if (Dataset(uri) != "TaiwanStockTradingDate") return null;
            return mutation switch
            {
                "duplicate" => Success(new[] { "2024-12-31", "2025-01-02", "2025-01-02", "2026-12-31" }.Select(DateRow)),
                "truncated" => Success(new[] { "2025-01-02", "2025-01-03" }.Select(DateRow)),
                _ => Success(new[] { "not-a-date" }.Select(DateRow))
            };
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).DownloadAsync(Request));
        Assert.False(priceCalled);
    }

    [Theory]
    [InlineData("wrong-market")]
    [InlineData("unknown-stock")]
    public async Task RefusesFinMindMasterIdentityMismatchBeforePrices(string mutation)
    {
        var priceCalled = false;
        using var http = Client(uri =>
        {
            if (Dataset(uri) == "TaiwanStockPrice") priceCalled = true;
            return Dataset(uri) == "TaiwanStockInfo" ? Success([new { stock_id = mutation == "unknown-stock" ? "6488" : "2330", stock_name = "Fixture", type = "tpex", date = "2025-01-01" }]) : null;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).DownloadAsync(Request));
        Assert.False(priceCalled);
    }

    [Fact]
    public async Task RefusesHistoryAcrossKnownDifferentSourceVersions()
    {
        var priceCalled = false;
        using var http = Client(uri =>
        {
            if (Dataset(uri) == "TaiwanStockPrice") priceCalled = true;
            return Dataset(uri) == "TaiwanStockInfo" ? Success(new[]
            {
                new { stock_id = "2330", stock_name = "OLD", type = "tpex", industry_category = "Semiconductor", date = "2025-01-03" },
                new { stock_id = "2330", stock_name = "NEW", type = "twse", industry_category = "Semiconductor", date = "2026-10-02" }
            }) : null;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).DownloadAsync(Request));
        Assert.False(priceCalled);
    }

    [Fact]
    public async Task AcceptsAnOlderDatedMarketVersionOnlyOutsideTheRequestedHistory()
    {
        using var http = Client(uri => Dataset(uri) == "TaiwanStockInfo" ? Success(new[]
        {
            new { stock_id = "2330", stock_name = "FIXTURE OLD", type = "tpex", industry_category = "Semiconductor", date = "2014-01-01" },
            new { stock_id = "2330", stock_name = "FIXTURE NEW", type = "twse", industry_category = "Semiconductor", date = "2025-01-01" },
            new { stock_id = "2330", stock_name = "FIXTURE NEW", type = "twse", industry_category = "Semiconductor", date = "2025-01-01" }
        }) : null);
        var result = await Provider(http).DownloadAsync(Request);
        Assert.Equal("TWSE", result.Snapshot.Instrument.Market);
        Assert.Equal(3, result.Snapshot.Bars.Count);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("")]
    [InlineData("not-date")]
    public async Task RejectsUnknownMasterVersionDatesRatherThanGuessingTheLatestMarket(string date)
    {
        using var http = Client(uri => Dataset(uri) == "TaiwanStockInfo" ? Success(new[]
        {
            new { stock_id = "2330", stock_name = "FIXTURE", type = "twse", date = "2025-01-01" },
            new { stock_id = "2330", stock_name = "FIXTURE", type = "tpex", date }
        }) : null);
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).DownloadAsync(Request));
    }

    [Theory]
    [InlineData("2330", "TWSE")]
    [InlineData("6488", "TPEx")]
    public async Task EarlierPeriodsUseOnlyFinMindCalendarAndExactMissingDates(string code, string market)
    {
        var dates = new DateOnly[] { new(2015, 10, 2), new(2015, 10, 5), new(2015, 10, 6) };
        var urls = new List<Uri>();
        using var http = Client(uri =>
        {
            urls.Add(uri);
            if (Dataset(uri) == "TaiwanStockTradingDate") return Success(dates.Prepend(new(2015, 1, 1)).Append(new(2026, 12, 31)).Select(date => DateRow(date.ToString("yyyy-MM-dd"))));
            if (Dataset(uri) == "TaiwanStockPrice") return Prices(uri, sourceDates: dates);
            return null;
        });
        var request = new HistoricalDataRequest(code, dates[0], dates[^1], market);
        var result = await Provider(http).DownloadMissingRangesAsync(request,
            [new() { Start = dates[0], End = dates[0] }, new() { Start = dates[^1], End = dates[^1] }], null, default);
        Assert.All(urls, uri => Assert.Equal("api.finmindtrade.com", uri.Host));
        Assert.DoesNotContain(urls, uri => uri.AbsolutePath.Contains("STOCK_DAY") || uri.AbsolutePath.Contains("tradingStock"));
        Assert.Equal(2, result.Snapshot.Bars.Count);
        Assert.Contains(result.Diagnostics, issue => issue.Code == "FinMindCalendarSource");
        Assert.All(result.CacheCoverage!.VerifiedCalendarRanges, range => Assert.True(range.Start == dates[0] || range.Start == dates[^1]));
    }

    [Fact]
    public async Task RejectsPriceDatesAbsentFromTheFinMindCalendar()
    {
        var priceCalled = false;
        using var http = Client(uri =>
        {
            if (Dataset(uri) == "TaiwanStockPrice") priceCalled = true;
            if (Dataset(uri) == "TaiwanStockTradingDate")
                return Success(new[] { "2015-01-01", "2025-01-02", "2025-01-04", "2025-01-06", "2026-12-31" }.Select(DateRow));
            return null;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).DownloadAsync(Request));
        Assert.True(priceCalled);
    }

    [Fact]
    public async Task FinMindCalendarFailureStopsBeforePrices()
    {
        var priceCalled = false;
        using var http = Client(uri =>
        {
            if (Dataset(uri) == "TaiwanStockPrice") priceCalled = true;
            if (Dataset(uri) == "TaiwanStockTradingDate") return new(HttpStatusCode.TooManyRequests);
            return null;
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).DownloadAsync(Request with { Start = new(2015, 10, 2), End = new(2015, 10, 6) }));
        Assert.False(priceCalled);
    }

    [Theory]
    [InlineData("TSE", "2330", "2025-01-02", "2025-01-06")]
    [InlineData("TWSE", "23/0", "2025-01-02", "2025-01-06")]
    [InlineData("TWSE", "2330", "2025-01-07", "2025-01-06")]
    [InlineData("TWSE", "2330", "1999-01-04", "2000-01-06")]
    [InlineData("TWSE", "2330", "2016-01-01", "2026-01-01")]
    [InlineData("TWSE", "2330", "2025-01-01", "2026-10-04")]
    public async Task InvalidRequestsNeverContactSources(string market, string code, string start, string end)
    {
        var calls = 0;
        using var http = Client(_ => { calls++; return null; });
        await Assert.ThrowsAsync<ArgumentException>(() => Provider(http).DownloadAsync(new(code, DateOnly.Parse(start), DateOnly.Parse(end), market)));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InvalidMissingRangeNeverContactsSources()
    {
        var calls = 0;
        using var http = Client(_ => { calls++; return null; });
        await Assert.ThrowsAsync<ArgumentException>(() => Provider(http).DownloadMissingRangesAsync(Request,
            [new() { Start = Request.Start.AddDays(-1), End = Request.End }], null, default));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task MainFeedFailureThrowsWithoutRequestingEvents()
    {
        var datasets = new List<string>();
        using var http = Client(uri =>
        {
            datasets.Add(Dataset(uri));
            return Dataset(uri) == "TaiwanStockPrice" ? new(HttpStatusCode.TooManyRequests) : null;
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => Provider(http).DownloadAsync(Request));
        Assert.Equal("TaiwanStockPrice", datasets.Last());
    }

    [Theory]
    [InlineData("html")]
    [InlineData("invalid-json")]
    [InlineData("string-status")]
    [InlineData("not-array")]
    [InlineData("oversize")]
    public async Task RejectsMalformedOrUnboundedMainResponses(string mutation)
    {
        using var http = Client(uri =>
        {
            if (Dataset(uri) != "TaiwanStockPrice") return null;
            var response = mutation switch
            {
                "html" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>challenge</html>", Encoding.UTF8, "text/html") },
                "invalid-json" => new(HttpStatusCode.OK) { Content = new StringContent("{broken", Encoding.UTF8, "application/json") },
                "string-status" => Json(new { status = "200", data = Array.Empty<object>() }),
                "not-array" => Json(new { status = 200, data = new { date = "2025-01-02" } }),
                _ => Success(Array.Empty<object>())
            };
            if (mutation == "oversize") response.Content.Headers.ContentLength = 9 * 1024 * 1024;
            return response;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(http).DownloadAsync(Request));
    }

    [Fact]
    public async Task CancellationBeforeRequestDoesNotContactSources()
    {
        var calls = 0;
        using var http = Client(_ => { calls++; return null; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(http).DownloadAsync(Request, cancellationToken: new(true)));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task InFlightCancellationPropagatesWithoutBecomingEventWarning()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (message, token) =>
        {
            if (Dataset(message.RequestUri!) == "TaiwanStockDividendResult")
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return DefaultResponse(message.RequestUri!);
        }));
        var download = Provider(http).DownloadAsync(Request, cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download);
    }

    [Fact]
    public async Task PriceTimeoutIsDistinctFromCancellation()
    {
        using var http = new HttpClient(new Handler(async (message, token) =>
        {
            if (Dataset(message.RequestUri!) == "TaiwanStockPrice") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return DefaultResponse(message.RequestUri!);
        }));
        var provider = new FinMindHistoricalDataProvider(http, new FrozenTime(), TimeSpan.Zero, TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAsync<TimeoutException>(() => provider.DownloadAsync(Request));
    }

    [Fact]
    public async Task MasterRefreshDateDoesNotChangeSourceIdentityOrInventListingDate()
    {
        using var firstHttp = Client();
        var first = await Provider(firstHttp).DownloadAsync(Request);
        using var nextHttp = Client(uri => Dataset(uri) == "TaiwanStockInfo" ? Success(new[]
        {
            new { stock_id = "2330", stock_name = "FIXTURE LISTED", type = "twse", industry_category = "a", date = "2026-10-02" }
        }) : null);
        var next = await Provider(nextHttp).DownloadAsync(Request);
        Assert.Equal(first.Snapshot.Instrument.InstrumentId, next.Snapshot.Instrument.InstrumentId);
        Assert.Null(next.Snapshot.Instrument.ListedFrom);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingFinMindCacheExtendsOnlyWhenPreservedMasterEvidenceMatches(bool changeIdentity)
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.FinMindIdentity", Guid.NewGuid().ToString("N"));
        try
        {
            using var firstHttp = Client();
            var first = await Provider(firstHttp).DownloadAsync(Request with { End = Request.Start });
            var snapshot = first.Snapshot with
            {
                Instrument = first.Snapshot.Instrument with { InstrumentId = "TWSE:TW0002330008", ListedFrom = new(1994, 9, 5) },
                ContentHash = "", SnapshotId = ""
            };
            var hash = SnapshotFingerprint.Compute(snapshot);
            first = first with { Snapshot = snapshot with { ContentHash = hash, SnapshotId = hash } };
            var store = new MarketDataStore(directory);
            var path = await store.SaveAsync(first);
            var before = await File.ReadAllBytesAsync(path);
            using var nextHttp = Client(uri => changeIdentity && Dataset(uri) == "TaiwanStockInfo" ? Success(new[]
            {
                new { stock_id = "2330", stock_name = "DIFFERENT SECURITY", type = "twse", industry_category = "a", date = "2026-10-02" }
            }) : null);
            var downloader = new CachedMarketDataDownloader(Provider(nextHttp), store);
            if (changeIdentity)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(Request));
                Assert.Equal(before, await File.ReadAllBytesAsync(path));
            }
            else
            {
                var result = await downloader.DownloadAsync(Request);
                Assert.Equal("TWSE:TW0002330008", result.Download.Snapshot.Instrument.InstrumentId);
                Assert.Equal(3, result.Download.Snapshot.Bars.Count);
                Assert.Equal(1, result.ReusedDays);
                Assert.Equal(1, result.DownloadedRanges);
                Assert.Contains(result.Download.Diagnostics, issue => issue.Code == "SavedInstrumentReferenceRetained");
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static FinMindHistoricalDataProvider Provider(HttpClient http) => new(http, new FrozenTime(), TimeSpan.Zero);
    private static HttpClient Client(Func<Uri, HttpResponseMessage?>? customize = null) => new(new Handler((request, _) =>
        Task.FromResult(customize?.Invoke(request.RequestUri!) ?? DefaultResponse(request.RequestUri!))));
    private static string Query(Uri uri, string key) => uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
        .Where(pair => pair.Length == 2 && pair[0] == key).Select(pair => Uri.UnescapeDataString(pair[1])).SingleOrDefault() ?? "";
    private static string Dataset(Uri uri) => Query(uri, "dataset");
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Success(IEnumerable<object> rows) => Json(new { status = 200, msg = "success", data = rows });
    private static object DateRow(string date) => new { date };
    private static readonly DateOnly[] Dates = [new(2025, 1, 2), new(2025, 1, 3), new(2025, 1, 6)];
    private static HttpResponseMessage Prices(Uri uri, Action<List<Dictionary<string, object?>>>? mutate = null, IReadOnlyList<DateOnly>? sourceDates = null)
    {
        var start = DateOnly.ParseExact(Query(uri, "start_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateOnly.ParseExact(Query(uri, "end_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = (sourceDates ?? Dates).Where(date => date >= start && date <= end).Select((date, index) => new Dictionary<string, object?>
        {
            ["date"] = date.ToString("yyyy-MM-dd"), ["stock_id"] = Query(uri, "data_id"),
            ["open"] = 100m + index, ["max"] = 102m + index, ["min"] = 99m + index, ["close"] = 100m + index,
            ["Trading_Volume"] = 1_234_567m, ["Trading_money"] = 123_456_700m, ["Trading_turnover"] = 99m, ["spread"] = 1m
        }).ToList();
        mutate?.Invoke(rows);
        return Success(rows);
    }

    private static HttpResponseMessage DefaultResponse(Uri uri)
    {
        if (uri.Host != "api.finmindtrade.com" || uri.AbsolutePath != "/api/v4/data")
            throw new InvalidOperationException("Only FinMind public data requests are allowed: " + uri);
        return Dataset(uri) switch
        {
            "TaiwanStockInfo" => Success(new[]
            {
                new { stock_id = "2330", stock_name = "FIXTURE LISTED", type = "twse", industry_category = "a", date = "2025-01-01" },
                new { stock_id = "2330", stock_name = "FIXTURE LISTED", type = "twse", industry_category = "b", date = "2025-01-01" },
                new { stock_id = "6488", stock_name = "FIXTURE OTC", type = "tpex", industry_category = "a", date = "2025-01-01" }
            }),
            "TaiwanStockTradingDate" => Success(new[] { "2015-01-01", "2025-01-02", "2025-01-03", "2025-01-06", "2026-12-31" }.Select(DateRow)),
            "TaiwanStockPrice" => Prices(uri),
            "TaiwanStockDividendResult" or "TaiwanStockCapitalReductionReferencePrice" or "TaiwanStockParValueChange" or "TaiwanStockSplitPrice" => Success(Array.Empty<object>()),
            _ => throw new InvalidOperationException("Unexpected source URL: " + uri)
        };
    }

    private sealed class FrozenTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 3, 16, 0, 0, TimeSpan.Zero);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
