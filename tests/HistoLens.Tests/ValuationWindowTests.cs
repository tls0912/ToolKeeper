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
public sealed class ValuationWindowTests
{
    [Theory]
    [InlineData("zh-TW")]
    [InlineData("en")]
    [InlineData("ja")]
    public Task ScanFetchesDatedValuationWithoutWaitingForIt(string language) => OnSta(async () =>
    {
        var pending = new TaskCompletionSource<CurrentValuation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new ValuationStub((_, _) => pending.Task);
        var download = Fixture();
        var directory = NewDirectory();
        var window = new MainWindow(directory, new HistoryStub(download), values) { PreferencesPath = null, SelectedLanguage = language };
        try
        {
            Assert.Equal(0, values.Calls);
            await window.DownloadMarketDataAsync(Request(download));
            Assert.Equal(0, values.Calls);
            await window.RunSimilarityAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(window.SimilarityResult!.IsAllowed);
            Assert.Equal(1, values.Calls);
            Assert.False(window.ValuationRefreshTask.IsCompleted);
            Assert.True(Get<Button>(window, "_cancel").IsEnabled);
            var result = window.SimilarityResult;
            pending.SetResult(Quote(28.98m));
            await window.ValuationRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(28.98m, window.Valuation!.PriceEarningsRatio);
            Assert.Contains("2025-01-02", Get<TextBlock>(window, "_valuationInfo").Text);
            Assert.Contains("28.98", Get<TextBlock>(window, "_valuationInfo").Text);
            Assert.Contains(language switch { "zh-TW" => "上市", "ja" => "上場", _ => "Listed" }, Get<TextBlock>(window, "_valuationInfo").Text);
            Assert.Contains("FinMind/TaiwanStockPER", Get<TextBlock>(window, "_valuationInfo").ToolTip!.ToString());
            Assert.DoesNotContain("OGDL", Get<TextBlock>(window, "_valuationInfo").ToolTip!.ToString());
            Assert.Same(result, window.SimilarityResult);
            Assert.False(Get<Button>(window, "_cancel").IsEnabled);
            var frame = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            frame.Measure(new Size(900, 660)); frame.Arrange(new Rect(0, 0, 900, 660)); frame.UpdateLayout();
            Assert.True(Get<TextBlock>(window, "_valuationInfo").ActualHeight > 20);
            if (Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR") is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap(900, 660, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, $"histolens-valuation-{language}.png")); encoder.Save(stream);
            }
            // Subsequent analyses refresh; loading/searching alone never does.
            await window.RunSimilarityAsync();
            await window.ValuationRefreshTask;
            Assert.Equal(2, values.Calls);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task FailedValuationDoesNotFailOrEraseScan() => OnSta(async () =>
    {
        var values = new ValuationStub((_, _) => throw new IOException("offline"));
        var download = Fixture(); var directory = NewDirectory();
        var window = new MainWindow(directory, new HistoryStub(download), values) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.DownloadMarketDataAsync(Request(download));
            await window.RunSimilarityAsync(); await window.ValuationRefreshTask;
            Assert.True(window.SimilarityResult!.IsAllowed);
            Assert.Null(window.Valuation);
            Assert.Contains("查詢失敗", Get<TextBlock>(window, "_valuationInfo").Text);
            Assert.Contains("offline", Get<TextBlock>(window, "_valuationInfo").ToolTip.ToString());
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task SwitchingDataCancelsAndRejectsLateQuote() => OnSta(async () =>
    {
        var pending = new TaskCompletionSource<CurrentValuation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var values = new ValuationStub((_, token) => { received = token; return pending.Task; });
        var download = Fixture(); var directory = NewDirectory();
        var window = new MainWindow(directory, new HistoryStub(download), values) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.DownloadMarketDataAsync(Request(download));
            await window.RunSimilarityAsync();
            var request = window.ValuationRefreshTask;
            window.LoadDemo();
            Assert.True(received.IsCancellationRequested);
            pending.SetResult(Quote(99m));
            await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(window.Valuation);
            Assert.Contains("合成資料不適用", Get<TextBlock>(window, "_valuationInfo").Text);
            await window.RunSimilarityAsync();
            Assert.Equal(1, values.Calls);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task CancelQuoteKeepsCompletedAnalysisAndDoesNotShowLateValue() => OnSta(async () =>
    {
        var pending = new TaskCompletionSource<CurrentValuation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = new ValuationStub((_, _) => pending.Task);
        var download = Fixture(); var directory = NewDirectory();
        var window = new MainWindow(directory, new HistoryStub(download), values) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.DownloadMarketDataAsync(Request(download));
            await window.RunSimilarityAsync();
            var result = window.SimilarityResult;
            Get<Button>(window, "_cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.ValuationRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
            pending.SetResult(Quote(90m));
            Assert.Null(window.Valuation);
            Assert.Same(result, window.SimilarityResult);
            Assert.Contains("已取消查詢", Get<TextBlock>(window, "_valuationInfo").Text);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task UnknownRatioIsShownAsUnavailableNotZero() => OnSta(async () =>
    {
        var values = new ValuationStub((_, _) => Task.FromResult<CurrentValuation?>(Quote(null)));
        var download = Fixture(); var directory = NewDirectory();
        var window = new MainWindow(directory, new HistoryStub(download), values) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.DownloadMarketDataAsync(Request(download));
            await window.RunSimilarityAsync(); await window.ValuationRefreshTask;
            Assert.Null(window.Valuation!.PriceEarningsRatio);
            Assert.Contains("未提供／不適用", Get<TextBlock>(window, "_valuationInfo").Text);
            Assert.Contains("2025-01-02", Get<TextBlock>(window, "_valuationInfo").Text);
            Assert.DoesNotContain("0 倍", Get<TextBlock>(window, "_valuationInfo").Text);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    private static CurrentValuation Quote(decimal? ratio) => new("TWSE", "2330", ratio, new(2025, 1, 2),
        new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero), "FinMind/TaiwanStockPER", FinMindValuationProvider.ResourceUrl);
    private static HistoricalDataRequest Request(HistoricalDataDownload download) => new("2330", download.Snapshot.Calendar.CoverageStart, download.Snapshot.DataAsOf);
    private static HistoricalDataDownload Fixture()
    {
        var synthetic = DemoData.Create();
        var snapshot = synthetic with
        {
            SnapshotId = "", ContentHash = "", IsSynthetic = false, SourceId = "TWSE",
            Instrument = synthetic.Instrument with { InstrumentId = "TWSE:2330", Code = "2330", Name = "台積電", Market = "TWSE", SecurityType = SecurityType.CommonStock },
            Calendar = synthetic.Calendar with { Market = "TWSE", Version = "test-calendar" },
            ActionCoverage = synthetic.ActionCoverage with { SourceId = "TWSE", Version = "test-actions" },
            Bars = synthetic.Bars.Select(bar => bar with { SourceId = "TWSE" }).ToArray(), CorporateActions = []
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        return new(snapshot with { ContentHash = hash, SnapshotId = hash }, []);
    }
    private sealed class HistoryStub(HistoricalDataDownload data) : IHistoricalDataProvider
    {
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request, IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult(data);
    }
    private sealed class ValuationStub(Func<Instrument, CancellationToken, Task<CurrentValuation?>> get) : ICurrentValuationProvider
    {
        public int Calls { get; private set; }
        public Task<CurrentValuation?> GetLatestAsync(Instrument instrument, CancellationToken cancellationToken = default)
        { Calls++; return get(instrument, cancellationToken); }
    }
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "HistoLens.ValuationUiTests", Guid.NewGuid().ToString("N"));
    private static void Cleanup(string directory) { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private static T Get<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
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
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
