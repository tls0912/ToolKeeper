using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

/// <summary>Artificial FinMind contract fixtures only. No test performs a network request.</summary>
public sealed class FinMindAuxiliaryProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 5, 0, 0, TimeSpan.Zero);
    private static readonly Instrument Listed = new()
    {
        InstrumentId = "TWSE:fixture", Code = "2330", Name = "測試上市股", Market = "TWSE", ListedFrom = new(2000, 1, 1)
    };
    private static readonly Instrument Otc = Listed with { InstrumentId = "TPEx:fixture", Code = "6488", Market = "TPEx" };

    [Fact]
    public async Task CatalogUsesLatestDatedMarketAndAcceptsMultipleIndustryRows()
    {
        var requests = new List<Uri>();
        using var http = Client((request, _) =>
        {
            requests.Add(request.RequestUri!);
            AssertPublicRequest(request, "TaiwanStockInfo");
            return Task.FromResult(Json(Envelope(
                Info("2330", "測試上市股", "twse", "半導體業"),
                Info("2330", "測試上市股", "twse", "電子工業"),
                Info("6488", "舊市場與名稱", "twse", "其他", "2018-01-01"),
                Info("6488", "測試上櫃股", "tpex", "半導體業"),
                Info("0050", "測試ETF", "twse", "ETF"),
                Info("0201", "測試ETN", "twse", "指數投資證券(ETN)"),
                Info("9105", "測試DR", "twse", "存託憑證"),
                Info("0101", "測試基金", "twse", "受益證券"),
                Info("0001", "測試指數", "twse", "Index"),
                Info("0050A", "長代號", "twse", "其他"),
                Info("8030", "舊上市股", "twse", "其他", "2018-01-01"),
                Info("8030", "其他市場", "emerging", "其他"))));
        });
        var provider = new FinMindStockCatalogProvider(http, new FixedTimeProvider(Now));
        Assert.Empty(requests);
        var catalog = await provider.GetAsync();
        Assert.Single(requests);
        Assert.Equal(Now, catalog.UpdatedAtUtc);
        Assert.Equal(new[] { "TPEx:6488", "TWSE:2330" }, catalog.Entries.Select(row => row.Market + ":" + row.Code));
        Assert.Equal("測試上櫃股", catalog.Entries[0].Name);
    }

    [Theory]
    [InlineData("market")]
    [InlineData("name")]
    public async Task CatalogRejectsAmbiguousLatestRowsBeforeReturningPartialData(string field)
    {
        var conflict = Info("2330", "測試上市股", "twse", "半導體業");
        conflict[field == "market" ? "type" : "stock_name"] = field == "market" ? "tpex" : "另一證券";
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(ValidInfo().Concat(new[] { conflict }).ToArray()))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog(http).GetAsync());
    }

    [Theory]
    [InlineData("stock_id")]
    [InlineData("stock_name")]
    [InlineData("type")]
    [InlineData("industry_category")]
    [InlineData("date")]
    public async Task CatalogRequiresTheFieldsNeededToSelectSupportedStocks(string missingField)
    {
        var rows = ValidInfo();
        rows[0].Remove(missingField);
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(rows))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog(http).GetAsync());
    }

    [Theory]
    [InlineData("stock_id", "")]
    [InlineData("stock_name", " ")]
    [InlineData("stock_name", "bad\nname")]
    [InlineData("industry_category", "")]
    [InlineData("date", "None")]
    [InlineData("date", "2026-10-05")]
    [InlineData("date", "2026-13-01")]
    public async Task CatalogRejectsInvalidRequiredValues(string field, string value)
    {
        var rows = ValidInfo();
        rows[0][field] = value;
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(rows))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog(http).GetAsync());
    }

    [Fact]
    public async Task CatalogCannotReturnOnlyOneMarketAsSuccessfulFullUpdate()
    {
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(Info("2330", "測試上市股", "twse", "半導體業")))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog(http).GetAsync());
    }

    [Theory]
    [InlineData("TWSE", "2330")]
    [InlineData("tpex", "6488")]
    public async Task ValuationQueriesTheRequestedCodeAndKeepsLatestPublishedDate(string market, string code)
    {
        var calls = 0;
        using var http = Client((request, _) =>
        {
            calls++;
            AssertPublicRequest(request, "TaiwanStockPER");
            Assert.Contains("data_id=" + code, request.RequestUri!.Query);
            Assert.Contains("start_date=2026-09-20", request.RequestUri.Query);
            Assert.Contains("end_date=2026-10-04", request.RequestUri.Query);
            return Task.FromResult(Json(Envelope(
                Per(code, "2026-10-02", 29.25m), Per(code, "2026-10-01", "28.50"))));
        });
        var result = await Valuation(http).GetLatestAsync(Listed with { Code = code, Market = market });
        Assert.NotNull(result);
        Assert.Equal(market == "TWSE" ? "TWSE" : "TPEx", result.Market);
        Assert.Equal(code, result.Code);
        Assert.Equal(29.25m, result.PriceEarningsRatio);
        Assert.Equal(new DateOnly(2026, 10, 2), result.DataDate);
        Assert.Equal(Now, result.RetrievedAtUtc);
        Assert.Equal("FinMind/TaiwanStockPER", result.SourceId);
        Assert.StartsWith(FinMindValuationProvider.ResourceUrl, result.ResourceUrl);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--")]
    [InlineData("None")]
    [InlineData("N/A")]
    [InlineData("0")]
    [InlineData("0.00")]
    public async Task LatestUnavailableRatioDoesNotFallBackToAnOlderValue(string? value)
    {
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(
            Per("2330", "2026-10-01", 28.5m), Per("2330", "2026-10-02", value)))));
        var result = await Valuation(http).GetLatestAsync(Listed);
        Assert.NotNull(result);
        Assert.Null(result.PriceEarningsRatio);
        Assert.Equal(new DateOnly(2026, 10, 2), result.DataDate);
    }

    [Fact]
    public async Task NumericZeroRatioIsUnavailableAndAnEmptyResponseHasNoValuation()
    {
        using var zeroHttp = Client((_, _) => Task.FromResult(Json(Envelope(Per("2330", "2026-10-02", 0)))));
        Assert.Null((await Valuation(zeroHttp).GetLatestAsync(Listed))!.PriceEarningsRatio);
        using var emptyHttp = Client((_, _) => Task.FromResult(Json(Envelope())));
        Assert.Null(await Valuation(emptyHttp).GetLatestAsync(Listed));
    }

    [Fact]
    public async Task ValuationDoesNotInventANameCheckAbsentFromTheSourceSchema()
    {
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(Per("2330", "2026-10-02", 29.25m)))));
        var result = await Valuation(http).GetLatestAsync(Listed with { Name = "歷史名稱" });
        Assert.Equal(29.25m, result!.PriceEarningsRatio);
    }

    [Fact]
    public async Task UnsupportedAndExpiredInstrumentsNeverRequestAValuation()
    {
        using var http = Client((_, _) => throw new InvalidOperationException("Must not request."));
        var provider = Valuation(http);
        Assert.Null(await provider.GetLatestAsync(Listed with { Market = "unknown" }));
        Assert.Null(await provider.GetLatestAsync(Listed with { Code = "0050A" }));
        Assert.Null(await provider.GetLatestAsync(Listed with { SecurityType = SecurityType.Synthetic }));
        Assert.Null(await provider.GetLatestAsync(Listed with { ListedUntil = new(2026, 10, 3) }));
        Assert.Null(await provider.GetLatestAsync(Listed with { ListedFrom = new(2026, 10, 5) }));
    }

    [Fact]
    public async Task SourceDateBeforeListingDoesNotBecomeTheCurrentInstrumentValuation()
    {
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(Per("2330", "2026-10-02", 29.25m)))));
        Assert.Null(await Valuation(http).GetLatestAsync(Listed with { ListedFrom = new(2026, 10, 3) }));
    }

    [Theory]
    [InlineData("stock_id", "2317")]
    [InlineData("date", "2026-10-05")]
    [InlineData("date", "2026-09-19")]
    [InlineData("date", "1151002")]
    [InlineData("PER", "-2.5")]
    [InlineData("PER", "NaN")]
    [InlineData("PER", "1,2.5")]
    public async Task ValuationRejectsWrongCodeDateAndRatioInsteadOfClaimingUnavailable(string field, string value)
    {
        var row = Per("2330", "2026-10-02", 29.25m);
        row[field] = value;
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(row))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Valuation(http).GetLatestAsync(Listed));
    }

    [Theory]
    [InlineData("stock_id")]
    [InlineData("date")]
    [InlineData("PER")]
    public async Task ValuationRequiresItsIdentityDateAndRatioFields(string field)
    {
        var row = Per("2330", "2026-10-02", 29.25m);
        row.Remove(field);
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(row))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Valuation(http).GetLatestAsync(Listed));
    }

    [Fact]
    public async Task DuplicateValuationDatesAreRejectedEvenIfValuesAgree()
    {
        var row = Per("2330", "2026-10-02", 29.25m);
        using var http = Client((_, _) => Task.FromResult(Json(Envelope(row, row))));
        await Assert.ThrowsAsync<InvalidDataException>(() => Valuation(http).GetLatestAsync(Listed));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"200\",\"data\":[]}")]
    [InlineData("{\"status\":200,\"data\":[null]}")]
    [InlineData("{\"status\":200,\"data\":{}}")]
    public async Task MalformedEnvelopeFailsBothAuxiliaryProviders(string body)
    {
        using var http = Client((_, _) => Task.FromResult(Json(body)));
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog(http).GetAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => Valuation(http).GetLatestAsync(Listed));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AccessFailureIsSurfacedOnceWithoutRetryFallbackOrOtherHosts(bool catalog)
    {
        var calls = 0;
        using var http = Client((request, _) =>
        {
            Assert.Equal("api.finmindtrade.com", request.RequestUri!.Host);
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Fetch(http, catalog));
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApiAuthorizationFailureAlsoStopsWithoutRetry(bool catalog)
    {
        var calls = 0;
        using var http = Client((_, _) =>
        {
            calls++;
            return Task.FromResult(Json("{\"status\":403,\"msg\":\"fixture denied\",\"data\":[]}"));
        });
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Fetch(http, catalog));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationRemainsCancellationAndPreCanceledCallsDoNotFetch(bool catalog)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var neverHttp = Client((_, _) => throw new InvalidOperationException("Must not request."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fetch(neverHttp, catalog, cancelled.Token));
        using var activeCancellation = new CancellationTokenSource();
        using var http = Client(async (_, token) =>
        {
            activeCancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(Envelope());
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fetch(http, catalog, activeCancellation.Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestTimeoutIsExplicitAndDoesNotRetry(bool catalog)
    {
        var calls = 0;
        using var http = Client(async (_, token) =>
        {
            calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(Envelope());
        });
        var timeout = TimeSpan.FromMilliseconds(25);
        await Assert.ThrowsAsync<TimeoutException>(() => catalog
            ? new FinMindStockCatalogProvider(http, new FixedTimeProvider(Now), timeout).GetAsync()
            : (Task)new FinMindValuationProvider(http, new FixedTimeProvider(Now), timeout).GetLatestAsync(Listed));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BothDeclaredAndStreamingResponseLengthsAreBounded()
    {
        using var declaredHttp = Client((_, _) =>
        {
            var response = Json(Envelope());
            response.Content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
            return Task.FromResult(response);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog(declaredHttp).GetAsync());
        using var streamingHttp = Client((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableStream(new byte[8 * 1024 * 1024 + 1]))
            };
            response.Content.Headers.ContentType = new("application/json");
            return Task.FromResult(response);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Valuation(streamingHttp).GetLatestAsync(Listed));
    }

    [Fact]
    public async Task NonJsonMaintenancePagesDoNotMasqueradeAsAnEmptyDataset()
    {
        using var http = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>fixture maintenance</html>", Encoding.UTF8, "text/html")
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog(http).GetAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => Valuation(http).GetLatestAsync(Listed));
    }

    private static Task Fetch(HttpClient http, bool catalog, CancellationToken cancellationToken = default) =>
        catalog ? Catalog(http).GetAsync(cancellationToken) : Valuation(http).GetLatestAsync(Listed, cancellationToken);
    private static FinMindStockCatalogProvider Catalog(HttpClient http) => new(http, new FixedTimeProvider(Now));
    private static FinMindValuationProvider Valuation(HttpClient http) => new(http, new FixedTimeProvider(Now));
    private static Dictionary<string, object?>[] ValidInfo() => new[]
    {
        Info("2330", "測試上市股", "twse", "半導體業"), Info("6488", "測試上櫃股", "tpex", "半導體業")
    };
    private static Dictionary<string, object?> Info(string code, string name, string type, string industry, string date = "2026-10-04") =>
        new() { ["stock_id"] = code, ["stock_name"] = name, ["type"] = type, ["industry_category"] = industry, ["date"] = date };
    private static Dictionary<string, object?> Per(string code, string date, object? ratio) =>
        new() { ["stock_id"] = code, ["date"] = date, ["PER"] = ratio };
    private static string Envelope(params Dictionary<string, object?>[] rows) => JsonSerializer.Serialize(new { status = 200, msg = "fixture success", data = rows });
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => new(new Handler(respond));

    private static void AssertPublicRequest(HttpRequestMessage request, string dataset)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal("api.finmindtrade.com", request.RequestUri.Host);
        Assert.Equal("/api/v4/data", request.RequestUri.AbsolutePath);
        Assert.Contains("dataset=" + dataset, request.RequestUri.Query);
        Assert.DoesNotContain("token", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("device", request.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Null(request.Headers.Authorization);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
