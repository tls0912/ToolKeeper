using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

[Collection("HistoLens WPF UI")]
public sealed class FinMindWindowTests
{
    [Fact]
    public Task DefaultWindowUsesFinMindForAllThreeDownloadServices() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.FinMindUi", Guid.NewGuid().ToString("N"));
        var window = new MainWindow(directory) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.StockCatalogLoadTask;
            Assert.IsType<FinMindHistoricalDataProvider>(Get<IHistoricalDataProvider>(window, "_dataProvider"));
            Assert.IsType<FinMindStockCatalogProvider>(Get<IStockCatalogProvider>(window, "_stockCatalogProvider"));
            Assert.IsType<FinMindValuationProvider>(Get<ICurrentValuationProvider>(window, "_valuationProvider"));
            Assert.Null(typeof(MainWindow).GetField("_alternateDataProvider", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.Null(window.Snapshot);
            Assert.Contains("FinMind", Get<TextBlock>(window, "_downloadSourceInfo").Text);
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Theory]
    [InlineData("TWSE", "2330", "zh-TW", "Ink")]
    [InlineData("TPEx", "6488", "en", "Dark")]
    [InlineData("TPEx", "6488", "ja", "InkDark")]
    public Task DownloadCacheReopenAndEventChartUseFinMindIdentity(string market, string code, string language, string theme) => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.FinMindUi", Guid.NewGuid().ToString("N"));
        var fixture = Fixture(market, code, "FinMind");
        var history = new FinMindStub(fixture);
        var window = new MainWindow(directory, history)
        { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
        try
        {
            var source = Get<TextBlock>(window, "_downloadSourceInfo");
            Assert.Contains("FinMind", source.Text);
            Assert.Null(typeof(MainWindow).GetField("_downloadSource", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.Equal(new[] { language switch { "zh-TW" => "上市", "ja" => "上場", _ => "Listed" },
                language switch { "zh-TW" => "上櫃", "ja" => "店頭", _ => "OTC" } },
                Get<ComboBox>(window, "_downloadMarket").Items.Cast<ComboBoxItem>().Select(item => item.Content));
            Assert.Equal(0, history.Calls);
            var request = new HistoricalDataRequest(code, fixture.Snapshot.Calendar.CoverageStart, fixture.Snapshot.DataAsOf, market);
            await window.DownloadMarketDataAsync(request);
            Assert.Equal(1, history.Calls);
            Assert.Equal("FinMind", window.Snapshot!.SourceId);
            Assert.Contains("FinMind", Get<TextBlock>(window, "_notice").Text);
            Assert.Contains("FinMind", Get<TextBlock>(window, "_sourceMethods").Text);
            var inventory = Get<DataGrid>(window, "_inventory");
            var entry = Assert.Single(inventory.Items.Cast<MarketDataEntry>(), e => !e.IsSynthetic);
            Assert.Equal("FinMind", entry.SourceId);
            Assert.Contains("FinMind", Get<ComboBox>(window, "_marketData").Text);

            await window.RunSimilarityAsync();
            Assert.True(window.SimilarityResult!.IsAllowed);
            Assert.Contains(window.SimilarityResult.Diagnostics, d => d.Code == "CorporateActionCoverageUnknown" && !d.BlocksResearch);
            var chart = Get<SimilarityComparisonChart>(window, "_comparisonChart");
            var action = fixture.Snapshot.CorporateActions[0];
            Assert.Contains(chart.EventMarkers, m => m.Date == action.EffectiveDate && m.SourceId == action.SourceId);
            await window.DownloadMarketDataAsync(request);
            Assert.Equal(1, history.Calls); // Fully saved range does not contact the source again.
            Assert.Equal("FinMind", (await MarketDataStore.LoadAsync(entry.Path)).Snapshot.SourceId);

            await window.LoadMarketDataAsync(entry.Path);
            Assert.Contains("FinMind", source.Text);
            Assert.Equal(1, history.Calls);

            var frame = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            Get<Button>(window, "_dataNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            frame.Measure(new Size(1100, 850)); frame.Arrange(new Rect(0, 0, 1100, 850)); frame.UpdateLayout();
            Assert.True(source.ActualHeight > 10);
            Assert.True(Get<Button>(window, "_download").ActualHeight > 20);
            SaveImage(frame, $"histolens-finmind-data-{language}.png");
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Fact]
    public Task LegacyLocalDataStaysDistinguishableWhileAllNewDownloadsUseFinMind() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.FinMindUi", Guid.NewGuid().ToString("N"));
        var store = new MarketDataStore(Path.Combine(directory, "market-data"));
        var old = Fixture("TWSE", "2330", "TWSE");
        var oldPath = await store.SaveAsync(old);
        var history = new FinMindStub(Fixture("TWSE", "2330", "FinMind"));
        var window = new MainWindow(directory, history) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.DownloadMarketDataAsync(new("2330", old.Snapshot.Calendar.CoverageStart, old.Snapshot.DataAsOf));
            Assert.Equal(1, history.Calls); // Official coverage is not reused as FinMind coverage.
            Assert.Equal(2, (await store.ListEntriesAsync()).Count);
            Assert.Equal(old.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(oldPath)).Snapshot.ContentHash);
            await window.LoadMarketDataAsync(oldPath);
            Assert.Equal("TWSE", window.Snapshot!.SourceId); // Stored provenance is retained.
            Assert.Contains("本機舊資料", Get<TextBlock>(window, "_notice").Text);
            Assert.Contains("本機舊資料", Get<ComboBox>(window, "_marketData").Text);
            Assert.Contains("FinMind", Get<TextBlock>(window, "_downloadSourceInfo").Text);
            Assert.Equal(1, history.Calls); // Offline open does not contact any source.
            await window.DownloadMarketDataAsync(new("2330", old.Snapshot.Calendar.CoverageStart, old.Snapshot.DataAsOf));
            Assert.Equal("FinMind", window.Snapshot!.SourceId);
            Assert.Equal(1, history.Calls); // Existing FinMind cache is reused after a legacy open.
            Assert.Equal(old.Snapshot.ContentHash, (await MarketDataStore.LoadAsync(oldPath)).Snapshot.ContentHash);
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    private static HistoricalDataDownload Fixture(string market, string code, string source)
    {
        var demo = DemoData.Create();
        var snapshot = demo with
        {
            SnapshotId = "", ContentHash = "", IsSynthetic = false, SourceId = source, DataVersion = "finmind-ui-fixture",
            Instrument = demo.Instrument with { InstrumentId = market + ":UI-FIXTURE", Code = code, Name = "Offline UI fixture", Market = market, SecurityType = SecurityType.CommonStock },
            Calendar = demo.Calendar with { Market = market, Version = "offline-calendar" },
            ActionCoverage = demo.ActionCoverage with { IsVerified = false, SourceId = source, Version = "unknown-actions" },
            ComparabilityCoverage = null,
            Bars = demo.Bars.Select(b => b with { SourceId = source, OriginalVolumeUnit = "shares" }).ToArray(),
            CorporateActions = [new() { EffectiveDate = demo.Calendar.TradingDates[^5], Kind = "ExDividend", SourceId = source + "/fixture" }]
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        snapshot = snapshot with { SnapshotId = hash, ContentHash = hash };
        return new(snapshot, SnapshotValidator.Validate(snapshot))
        {
            CacheCoverage = new() { CheckedPriceRanges = [new() { Start = snapshot.Calendar.CoverageStart, End = snapshot.DataAsOf }],
                VerifiedCalendarRanges = [new() { Start = snapshot.Calendar.CoverageStart, End = snapshot.DataAsOf }] }
        };
    }
    private sealed class FinMindStub(HistoricalDataDownload download) : IRangeHistoricalDataProvider, IHistoricalDataSource
    {
        public int Calls { get; private set; }
        public string DataSourceId => "FinMind";
        public HistoricalDataRequest ValidateRequest(HistoricalDataRequest request) => request;
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(download); }
        public Task<HistoricalDataDownload> DownloadMissingRangesAsync(HistoricalDataRequest request, IReadOnlyList<DateRange> ranges, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
            => DownloadAsync(request, progress, cancellationToken);
    }
    private static T Get<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void SaveImage(FrameworkElement frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap(1100, 850, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
    private static Task OnSta(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); done.TrySetResult(); }
                catch (Exception error) { done.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
