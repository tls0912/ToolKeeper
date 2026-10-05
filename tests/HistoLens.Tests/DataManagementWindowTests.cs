using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HistoLens.Core;
using HistoLens.Data;
using ToolKeeper.UI;
using Xunit;

namespace HistoLens.Tests;

[Collection("HistoLens WPF UI")]
public sealed class DataManagementWindowTests
{
    [Fact]
    public Task NavigationRetainsComparisonAndPlacesActionsBelowStock() => OnSta(async () =>
    {
        var directory = NewDirectory(); var window = new MainWindow(directory) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunSimilarityAsync();
            var run = window.SimilarityResult;
            var chart = Get<SimilarityComparisonChart>(window, "_comparisonChart"); chart.SetViewStart(0);
            var view = (chart.ViewStartIndex, chart.ViewEndIndex);
            var frame = Layout(window, 900, 660);
            var scan = Get<Button>(window, "_scanButton"); var cancel = Get<Button>(window, "_cancel");
            var navigation = Assert.IsType<WrapPanel>(window.HeaderActions);
            Assert.Equal(new[] { "放大鏡", "資料管理" }, navigation.Children.OfType<Button>().Select(button => button.Content));
            Assert.DoesNotContain(scan, navigation.Children.OfType<Button>());
            var stock = Get<ComboBox>(window, "_marketData");
            var recent = Get<TextBox>(window, "_recentDays");
            Assert.True(scan.TranslatePoint(new Point(), frame).Y >= stock.TranslatePoint(new Point(0, stock.ActualHeight), frame).Y);
            Assert.True(scan.TranslatePoint(new Point(0, scan.ActualHeight), frame).Y < recent.TranslatePoint(new Point(), frame).Y);
            Assert.Same(scan.Parent, cancel.Parent);
            Click(window, "_dataNavigation"); frame.UpdateLayout();
            Assert.Equal(Visibility.Visible, Get<Grid>(window, "_dataManagementPage").Visibility);
            Assert.False(scan.IsVisible); Assert.True(cancel.ActualHeight > 20);
            window.SelectedLanguage = "en"; window.SelectedTheme = "Dark";
            Click(window, "_magnifierNavigation"); frame.UpdateLayout();
            Assert.Same(run, window.SimilarityResult); Assert.Equal(view, (chart.ViewStartIndex, chart.ViewEndIndex));
            Assert.Equal("Magnifier", Get<Button>(window, "_magnifierNavigation").Content);
            var scores = Get<DataGrid>(window, "_similarities");
            foreach (var column in scores.Columns.OfType<DataGridTextColumn>().Where(column => column.Visibility == Visibility.Visible).Skip(1))
            {
                var text = Assert.IsType<TextBlock>(column.GetCellContent(scores.Items[0]));
                Assert.Equal(TextAlignment.Right, text.TextAlignment);
                Assert.True(text.ActualWidth > 40);
            }
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task DownloadRefreshAndDeleteSelectedStockUpdateBothPages() => OnSta(async () =>
    {
        var directory = NewDirectory(); var download = Fixture("2330", "台積電");
        var store = new MarketDataStore(Path.Combine(directory, "market-data"));
        var other = await store.SaveAsync(Fixture("2317", "鴻海"));
        var provider = new StubProvider((_, _, _) => Task.FromResult(download));
        var window = new MainWindow(directory, provider) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            Click(window, "_dataNavigation");
            await window.DownloadMarketDataAsync(new("2330", download.Snapshot.Calendar.CoverageStart, download.Snapshot.DataAsOf));
            var entries = Get<DataGrid>(window, "_inventory").Items.Cast<MarketDataEntry>().Where(entry => !entry.IsSynthetic).ToArray();
            Assert.Equal(2, entries.Length); Assert.Equal(3, Get<ComboBox>(window, "_marketData").Items.Count);
            var selected = entries.Single(entry => entry.Code == "2330");
            Assert.Equal(download.Snapshot.Calendar.CoverageStart, selected.CoverageStart);
            Assert.Equal(download.Snapshot.Bars.Count, selected.BarCount);
            Assert.Equal(Visibility.Visible, Get<Grid>(window, "_dataManagementPage").Visibility);
            await window.RunSimilarityAsync(); Assert.NotNull(window.SimilarityResult);
            await window.DeleteMarketDataAsync(selected.Path);
            Assert.False(File.Exists(selected.Path)); Assert.True(File.Exists(other));
            Assert.Null(window.Snapshot); Assert.Null(window.SimilarityResult);
            Assert.Empty(Get<SimilarityComparisonChart>(window, "_comparisonChart").HistoryPoints);
            Assert.False(Get<Button>(window, "_scanButton").IsEnabled);
            Assert.Equal(2, Get<DataGrid>(window, "_inventory").Items.Count);
            Assert.Equal(2, Get<ComboBox>(window, "_marketData").Items.Count);
            Assert.Equal(1, provider.Calls);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task DeletingUnloadedStockAndCancelledDeletionPreserveCurrentResearch() => OnSta(async () =>
    {
        var directory = NewDirectory(); var store = new MarketDataStore(Path.Combine(directory, "market-data"));
        var path = await store.SaveAsync(Fixture("2330", "台積電"));
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            await window.RefreshMarketDataAsync(); window.LoadDemo(); await window.RunSimilarityAsync();
            var snapshot = window.Snapshot; var run = window.SimilarityResult;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.DeleteMarketDataAsync(path, cancelled.Token));
            Assert.True(File.Exists(path)); Assert.Same(snapshot, window.Snapshot); Assert.Same(run, window.SimilarityResult);
            await Assert.ThrowsAsync<ArgumentException>(() => window.DeleteMarketDataAsync(Path.Combine(directory, "outside.twse-data.json")));
            Assert.Same(run, window.SimilarityResult);
            await window.DeleteMarketDataAsync(path);
            Assert.Same(snapshot, window.Snapshot); Assert.Same(run, window.SimilarityResult);
            Assert.True(Assert.Single(Get<DataGrid>(window, "_inventory").Items.Cast<MarketDataEntry>()).IsSynthetic);
            Assert.False(Get<Button>(window, "_inventoryDelete").IsEnabled);
            Assert.True(Get<Button>(window, "_scanButton").IsEnabled);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task CancelRemainsAvailableOnDataPageDuringDownload() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var provider = new StubProvider(async (_, _, token) => { await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); });
        var window = new MainWindow(directory, provider) { PreferencesPath = null };
        try
        {
            var pending = window.DownloadMarketDataAsync(new("2330", new(2024, 1, 1), new(2024, 1, 31)));
            Click(window, "_dataNavigation"); Layout(window, 900, 660);
            Assert.True(Get<Button>(window, "_cancel").IsEnabled);
            Assert.False(Get<Button>(window, "_download").IsEnabled);
            Assert.False(Get<TextBox>(window, "_stockCode").IsEnabled);
            Click(window, "_cancel"); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(Get<Button>(window, "_download").IsEnabled);
            Assert.False(Get<Button>(window, "_cancel").IsEnabled);
            await window.RefreshMarketDataAsync();
            Assert.True(Assert.Single(Get<DataGrid>(window, "_inventory").Items.Cast<MarketDataEntry>()).IsSynthetic);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Theory]
    [InlineData("zh-TW", "Ink", 900, 660)]
    [InlineData("en", "Dark", 1280, 880)]
    [InlineData("ja", "InkDark", 1280, 880)]
    public Task DataManagementPageRendersSavedStocksAndDates(string language, string theme, int width, int height) => OnSta(async () =>
    {
        var directory = NewDirectory(); var store = new MarketDataStore(Path.Combine(directory, "market-data"));
        await store.SaveAsync(Fixture("2330", "台積電")); await store.SaveAsync(Fixture("2317", "鴻海"));
        var window = new MainWindow(directory) { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
        try
        {
            await window.RefreshMarketDataAsync(); Click(window, "_dataNavigation");
            var frame = Layout(window, width, height); var inventory = Get<DataGrid>(window, "_inventory");
            Assert.Equal(3, inventory.Items.Count); Assert.True(inventory.ActualHeight > 180); Assert.True(inventory.ActualWidth > 400);
            Assert.True(Get<Button>(window, "_download").ActualHeight > 20);
            Assert.True(Get<Button>(window, "_inventoryOpen").IsEnabled); Assert.True(Get<Button>(window, "_inventoryDelete").IsEnabled);
            var start = Assert.IsType<TextBlock>(inventory.Columns[2].GetCellContent(inventory.Items[0]));
            Assert.Equal(((MarketDataEntry)inventory.Items[0]).CoverageStart.ToString("yyyy-MM-dd"), start.Text);
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"histolens-data-management-{language}-{theme}-{width}.png")); encoder.Save(file);
            }
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task StockSelectionLoadsAutomaticallyAndRefreshPreservesCompletedComparison() => OnSta(async () =>
    {
        var directory = NewDirectory(); var store = new MarketDataStore(Path.Combine(directory, "market-data"));
        var first = await store.SaveAsync(Fixture("2317", "鴻海"));
        var second = await store.SaveAsync(Fixture("2330", "台積電"));
        var window = new MainWindow(directory) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.RefreshMarketDataAsync();
            var selector = Get<ComboBox>(window, "_marketData");
            Assert.Null(window.Snapshot); Assert.Equal(-1, selector.SelectedIndex);
            SelectStock(selector, first); SelectStock(selector, second);
            await Until(() => window.Snapshot?.Instrument.Code == "2330");
            await window.RunSimilarityAsync(); var run = window.SimilarityResult; var snapshot = window.Snapshot;
            Assert.NotNull(run);
            await window.RefreshMarketDataAsync();
            Assert.Same(snapshot, window.Snapshot); Assert.Same(run, window.SimilarityResult);
            Assert.Equal(second, SelectedPath(selector));
            SelectStock(selector, first);
            await Until(() => window.Snapshot?.Instrument.Code == "2317");
            Assert.Null(window.SimilarityResult);
            await window.RunSimilarityAsync(); run = window.SimilarityResult; snapshot = window.Snapshot;
            await File.WriteAllTextAsync(second, "broken data");
            SelectStock(selector, second);
            await Until(() => Get<TextBlock>(window, "_status").Text.Contains("開啟失敗"));
            Assert.Same(snapshot, window.Snapshot); Assert.Same(run, window.SimilarityResult);
            Assert.Equal(first, SelectedPath(selector));
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task SyntheticSampleUsesInventoryAndStockSelectionWithoutSeparateLoadButton() => OnSta(async () =>
    {
        var directory = NewDirectory(); var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            await window.RefreshMarketDataAsync();
            var sample = Assert.Single(Get<DataGrid>(window, "_inventory").Items.Cast<MarketDataEntry>());
            Assert.True(sample.IsSynthetic); Assert.Contains("DEMO", sample.Code);
            Assert.False(Get<Button>(window, "_inventoryDelete").IsEnabled);
            Assert.Null(typeof(MainWindow).GetField("_demo", BindingFlags.Instance | BindingFlags.NonPublic));
            Assert.Null(typeof(MainWindow).GetField("_loadMarketData", BindingFlags.Instance | BindingFlags.NonPublic));
            var selector = Get<ComboBox>(window, "_marketData"); SelectStock(selector, sample.Path);
            Assert.True(window.Snapshot!.IsSynthetic); Assert.Equal(sample.Code, window.Snapshot.Instrument.Code);
            Assert.Empty(new MarketDataStore(Path.Combine(directory, "market-data")).List());
            await Assert.ThrowsAsync<ArgumentException>(() => window.DeleteMarketDataAsync(sample.Path));
            await window.RunSimilarityAsync(); Assert.NotNull(window.SimilarityResult);
            Click(window, "_dataNavigation"); Click(window, "_inventoryOpen");
            Assert.Equal(Visibility.Visible, Get<Grid>(window, "_magnifierPage").Visibility);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    private static string? SelectedPath(ComboBox selector) => selector.SelectedItem?.GetType().GetProperty("Path")!.GetValue(selector.SelectedItem) as string;
    private static void SelectStock(ComboBox selector, string path) => selector.SelectedItem = selector.Items.Cast<object>()
        .Single(item => (string)item.GetType().GetProperty("Path")!.GetValue(item)! == path);
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private static HistoricalDataDownload Fixture(string code, string name)
    {
        var synthetic = DemoData.Create();
        var snapshot = synthetic with
        {
            SnapshotId = "", ContentHash = "", IsSynthetic = false, SourceId = "TWSE",
            Instrument = synthetic.Instrument with { InstrumentId = "TWSE:" + code, Code = code, Name = name, Market = "TWSE", SecurityType = SecurityType.CommonStock },
            Calendar = synthetic.Calendar with { Market = "TWSE", Version = "test-calendar" },
            ActionCoverage = synthetic.ActionCoverage with { IsVerified = true, SourceId = "TWSE", Version = "test-actions" },
            Bars = synthetic.Bars.Select(bar => bar with { SourceId = "TWSE" }).ToArray(), CorporateActions = []
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        return new(snapshot with { SnapshotId = hash, ContentHash = hash }, []);
    }
    private sealed class StubProvider(Func<HistoricalDataRequest, IProgress<DownloadProgress>?, CancellationToken, Task<HistoricalDataDownload>> download) : IHistoricalDataProvider
    {
        public int Calls { get; private set; }
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        { Calls++; return download(request, progress, cancellationToken); }
    }
    private static WindowFrame Layout(MainWindow window, int width, int height)
    {
        var frame = Assert.IsType<WindowFrame>(window.Content);
        frame.Measure(new Size(width, height)); frame.Arrange(new Rect(0, 0, width, height)); frame.UpdateLayout(); return frame;
    }
    private static void Click(MainWindow window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Get<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "HistoLens.DataPageTests", Guid.NewGuid().ToString("N"));
    private static void Cleanup(string directory) { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
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
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
