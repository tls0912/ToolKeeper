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
public sealed class MarketWindowTests
{
    [Theory]
    [InlineData("zh-TW", "Ink")]
    [InlineData("en", "Dark")]
    [InlineData("ja", "InkDark")]
    public Task ExplicitOtcSelectionDownloadsScansAndShowsItsOwnValuation(string language, string theme) => OnSta(async () =>
    {
        var directory = NewDirectory(); var data = Fixture();
        HistoricalDataRequest? received = null;
        var history = new HistoryStub(request => { received = request; return Task.FromResult(data); });
        var valuation = new ValuationStub();
        var window = new MainWindow(directory, history, valuation) { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
        try
        {
            var market = Get<ComboBox>(window, "_downloadMarket");
            market.SelectedItem = market.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "TPEx");
            Get<TextBox>(window, "_stockCode").Text = "6488";
            Get<DatePicker>(window, "_downloadStart").SelectedDate = data.Snapshot.Calendar.CoverageStart.ToDateTime(TimeOnly.MinValue);
            Get<DatePicker>(window, "_downloadEnd").SelectedDate = data.Snapshot.DataAsOf.ToDateTime(TimeOnly.MinValue);
            Assert.Equal(0, history.Calls); Assert.Equal(0, valuation.Calls);
            await SubmitDownload(window);
            Assert.Equal("TPEx", received!.Market); Assert.Equal("6488", received.Code);
            Assert.Equal("TPEx", window.Snapshot!.Instrument.Market);
            Assert.Contains("6488", Get<TextBlock>(window, "_dataInfo").Text);
            Assert.Contains(language switch { "zh-TW" => "上櫃", "ja" => "店頭", _ => "OTC" }, Get<TextBlock>(window, "_dataInfo").Text);
            var entry = Assert.Single(Get<DataGrid>(window, "_inventory").Items.Cast<MarketDataEntry>(), item => !item.IsSynthetic);
            Assert.Equal("TPEx", entry.Market);
            Assert.StartsWith("FinMind-TPEx-6488-", Path.GetFileName(entry.Path));
            Assert.Contains("FinMind", Get<ComboBox>(window, "_marketData").Text);

            await window.RunSimilarityAsync(); await window.ValuationRefreshTask;
            Assert.True(window.SimilarityResult!.IsAllowed);
            Assert.Equal("TPEx", valuation.Instrument!.Market);
            Assert.Equal("6488", valuation.Instrument.Code);
            Assert.Equal(34.56m, window.Valuation!.PriceEarningsRatio);
            Assert.Equal("TPEx", window.Valuation.Market);
            var chart = Get<SimilarityComparisonChart>(window, "_comparisonChart");
            var action = Assert.Single(data.Snapshot.CorporateActions);
            var eventMarker = Assert.Single(chart.EventMarkers, marker => !marker.IsRecent && marker.Date == action.EffectiveDate && marker.SourceId == action.SourceId);
            var eventLabel = typeof(SimilarityComparisonChart).GetMethod("EventKind", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chart, [eventMarker]);
            Assert.Equal(language switch { "zh-TW" => "除息", "ja" => "配当落ち", _ => "Ex-dividend" }, eventLabel);
            Assert.False(window.Snapshot.ActionCoverage.IsVerified);
            Assert.Contains(window.SimilarityResult.Diagnostics, issue => issue.Code == "CorporateActionCoverageUnknown" && !issue.BlocksResearch);

            var frame = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            frame.Measure(new Size(1000, 760)); frame.Arrange(new Rect(0, 0, 1000, 760)); frame.UpdateLayout();
            SaveImage(frame, $"histolens-tpex-scan-{language}.png");
            Get<Button>(window, "_dataNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); frame.UpdateLayout();
            Assert.True(market.ActualWidth > 100);
            Assert.True(Get<Button>(window, "_download").ActualHeight > 20);
            SaveImage(frame, $"histolens-tpex-data-{language}.png");
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task ReopeningOtcDataRestoresMarketAndNeverDownloadsAutomatically() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var path = await new MarketDataStore(Path.Combine(directory, "market-data")).SaveAsync(Fixture());
        var history = new HistoryStub(_ => throw new InvalidOperationException("Opening must stay offline."));
        var valuation = new ValuationStub();
        var window = new MainWindow(directory, history, valuation) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.LoadMarketDataAsync(path);
            Assert.Equal("TPEx", ((ComboBoxItem)Get<ComboBox>(window, "_downloadMarket").SelectedItem).Tag);
            Assert.Equal("6488", Get<TextBox>(window, "_stockCode").Text);
            Assert.Contains("FinMind", Get<TextBlock>(window, "_notice").Text);
            Assert.DoesNotContain("張數換算為股", Get<TextBlock>(window, "_dataInfo").Text);
            Assert.Equal(0, history.Calls); Assert.Equal(0, valuation.Calls);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task FailedOtcDownloadPreservesPreviousStockAndScan() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var history = new HistoryStub(_ => throw new IOException("FinMind unavailable"));
        var window = new MainWindow(directory, history) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunSimilarityAsync();
            var snapshot = window.Snapshot; var run = window.SimilarityResult;
            await Assert.ThrowsAsync<IOException>(() => window.DownloadMarketDataAsync(new("6488", new(2025, 1, 1), new(2025, 2, 28), "TPEx")));
            Assert.Same(snapshot, window.Snapshot); Assert.Same(run, window.SimilarityResult);
            Assert.Contains("下載失敗", Get<TextBlock>(window, "_status").Text);
            Assert.Empty(new MarketDataStore(Path.Combine(directory, "market-data")).List());
        }
        finally { window.Close(); Cleanup(directory); }
    });

    private static HistoricalDataDownload Fixture()
    {
        var demo = DemoData.Create();
        var snapshot = demo with
        {
            SnapshotId = "", ContentHash = "", IsSynthetic = false, SourceId = "FinMind", DataVersion = "finmind-otc-ui-fixture",
            Instrument = demo.Instrument with { InstrumentId = "TPEx:UI-TEST", Code = "6488", Name = "環球晶", Market = "TPEx", SecurityType = SecurityType.CommonStock },
            Calendar = demo.Calendar with { Market = "TPEx", Version = "test-calendar" },
            ActionCoverage = demo.ActionCoverage with { IsVerified = false, SourceId = "FinMind", Version = "test-unknown-actions" },
            ComparabilityCoverage = null,
            Bars = demo.Bars.Select(bar => bar with { SourceId = "FinMind", OriginalVolumeUnit = "shares" }).ToArray(),
            CorporateActions = [new() { EffectiveDate = demo.Calendar.TradingDates[^5], Kind = "ExDividend", SourceId = "FinMind/test-event" }]
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        snapshot = snapshot with { SnapshotId = hash, ContentHash = hash };
        return new(snapshot, SnapshotValidator.Validate(snapshot));
    }
    private sealed class HistoryStub(Func<HistoricalDataRequest, Task<HistoricalDataDownload>> get) : IHistoricalDataProvider, IHistoricalDataSource
    {
        public int Calls { get; private set; }
        public string DataSourceId => "FinMind";
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        { Calls++; return get(request); }
    }
    private sealed class ValuationStub : ICurrentValuationProvider
    {
        public int Calls { get; private set; }
        public Instrument? Instrument { get; private set; }
        public Task<CurrentValuation?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default)
        {
            Calls++; Instrument = instrument;
            return Task.FromResult<CurrentValuation?>(new("TPEx", "6488", 34.56m, new(2025, 1, 2), DateTimeOffset.UtcNow,
                "FinMind/TaiwanStockPER", FinMindValuationProvider.ResourceUrl));
        }
    }
    private static Task SubmitDownload(MainWindow window) => (Task)typeof(MainWindow).GetMethod("DownloadFromUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
    private static T Get<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "HistoLens.TpexUiTests", Guid.NewGuid().ToString("N"));
    private static void Cleanup(string directory) { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private static void SaveImage(FrameworkElement frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap(1000, 760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
    }
    private static Task OnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
