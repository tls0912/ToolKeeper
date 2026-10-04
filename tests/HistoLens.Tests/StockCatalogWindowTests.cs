using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

[Collection("HistoLens WPF UI")]
public sealed class StockCatalogWindowTests
{
    [Theory]
    [InlineData("zh-TW", "Ink")]
    [InlineData("en", "Dark")]
    [InlineData("ja", "InkDark")]
    public Task ExplicitUpdateAndTwoWaySelectionNeverFetchPrices(string language, string theme) => OnSta(async () =>
    {
        var directory = NewDirectory(); var history = new HistoryStub();
        var catalog = new CatalogStub(_ => Task.FromResult(Fixture()));
        var window = Window(directory, history, catalog, language, theme);
        try
        {
            await window.StockCatalogLoadTask;
            Assert.Equal(0, catalog.Calls); Assert.Equal(0, history.Calls);
            Assert.Empty(Selector(window).Items); Assert.Equal("", Code(window).Text);
            Click(window, "_dataNavigation"); Click(window, "_refreshStockCatalog");
            await window.StockCatalogRefreshTask;
            Assert.Equal(1, catalog.Calls); Assert.Equal(3, Selector(window).Items.Count);
            Assert.Null(Selector(window).SelectedItem); Assert.Equal("", Code(window).Text);
            Assert.True(File.Exists(Path.Combine(directory, "stock-catalog.json")));
            Code(window).Text = "2330";
            Assert.Equal("台積電", Assert.IsType<StockCatalogEntry>(Selector(window).SelectedItem).Name);
            Assert.Equal("TWSE", Market(window));
            Code(window).Text = "6488";
            Assert.Equal("TPEx", Market(window));
            Assert.Equal("環球晶", Assert.IsType<StockCatalogEntry>(Selector(window).SelectedItem).Name);
            Layout(window);
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            Layout(window);
            var selectedLabel = "6488 · 環球晶 · " + (language switch { "zh-TW" => "上櫃", "ja" => "店頭", _ => "OTC" });
            Assert.Contains(VisualDescendants<TextBlock>(Selector(window)), text => text.Text == selectedLabel);
            Assert.DoesNotContain("TPEx", Selector(window).ToolTip!.ToString());
            Selector(window).SelectedItem = Selector(window).Items.Cast<StockCatalogEntry>().Single(entry => entry.Code == "2317");
            Assert.Equal("2317", Code(window).Text); Assert.Equal("TWSE", Market(window));
            foreach (var value in new[] { "23", "9999", "", "ABC" })
            {
                Code(window).Text = value;
                Assert.Equal(value, Code(window).Text); Assert.Null(Selector(window).SelectedItem);
            }
            Code(window).Text = " 6488 ";
            Assert.Equal("6488", Assert.IsType<StockCatalogEntry>(Selector(window).SelectedItem).Code);
            Assert.Equal(" 6488 ", Code(window).Text);
            Assert.Equal(0, history.Calls); Assert.Equal(1, catalog.Calls); Assert.Null(window.Snapshot);
            var selected = Selector(window).SelectedItem;
            window.SelectedLanguage = language == "en" ? "zh-TW" : "en";
            window.SelectedTheme = "Light";
            Assert.Same(selected, Selector(window).SelectedItem); Assert.Equal(" 6488 ", Code(window).Text);
            window.SelectedLanguage = language; window.SelectedTheme = theme;
            var frame = Layout(window);
            var codeAt = Code(window).TranslatePoint(new Point(), frame);
            var selectorAt = Selector(window).TranslatePoint(new Point(), frame);
            Assert.Equal(codeAt.Y, selectorAt.Y, precision: 1);
            Assert.True(selectorAt.X >= codeAt.X + Code(window).ActualWidth);
            Assert.True(Selector(window).ActualWidth > 100);
            Assert.Contains("2026-10-04", Get<TextBlock>(window, "_stockCatalogInfo").Text);
            SaveImage(frame, "histolens-stock-catalog-" + language + ".png");
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task SavedListReopensOfflineAndTypingUsesCurrentMarketForSharedCode() => OnSta(async () =>
    {
        var directory = NewDirectory(); var history = new HistoryStub();
        var catalog = new CatalogStub(_ => throw new IOException("Must remain offline."));
        var data = Fixture() with { Entries = [.. Fixture().Entries, new("TPEx", "2330", "Synthetic shared code")] };
        await new StockCatalogStore(Path.Combine(directory, "stock-catalog.json")).SaveAsync(data);
        var window = Window(directory, history, catalog);
        try
        {
            await window.StockCatalogLoadTask;
            Code(window).Text = "2330";
            Assert.Equal("TWSE", Assert.IsType<StockCatalogEntry>(Selector(window).SelectedItem).Market);
            SelectMarket(window, "TPEx");
            Assert.Equal("2330", Code(window).Text);
            Assert.Equal("TPEx", Assert.IsType<StockCatalogEntry>(Selector(window).SelectedItem).Market);
            Code(window).Text = "6488"; SelectMarket(window, "TWSE");
            Assert.Null(Selector(window).SelectedItem); Assert.Equal("6488", Code(window).Text);
            Assert.Equal("TWSE", Market(window));
            Assert.Equal(0, catalog.Calls); Assert.Equal(0, history.Calls);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task UpdateFailureRetainsListSelectionFileAndCompletedScan() => OnSta(async () =>
    {
        var directory = NewDirectory(); var history = new HistoryStub();
        var catalog = new CatalogStub(_ => Task.FromResult(Fixture()));
        var window = Window(directory, history, catalog);
        try
        {
            await window.RefreshStockCatalogAsync(); Code(window).Text = "2330";
            window.LoadDemo(); await window.RunSimilarityAsync();
            var run = window.SimilarityResult; var snapshot = window.Snapshot;
            var selected = Selector(window).SelectedItem;
            var file = await File.ReadAllBytesAsync(Path.Combine(directory, "stock-catalog.json"));
            catalog.Get = _ => throw new IOException("TPEx stock list unavailable");
            await Assert.ThrowsAsync<IOException>(() => window.RefreshStockCatalogAsync());
            Assert.Equal(file, await File.ReadAllBytesAsync(Path.Combine(directory, "stock-catalog.json")));
            Assert.Same(selected, Selector(window).SelectedItem);
            Assert.Same(snapshot, window.Snapshot); Assert.Same(run, window.SimilarityResult);
            Assert.Contains("更新失敗", Get<TextBlock>(window, "_stockCatalogInfo").Text);
            Assert.True(Get<Button>(window, "_refreshStockCatalog").IsEnabled);
            Assert.Equal(0, history.Calls);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task CancelledLateResponseCannotReplaceListOrCode() => OnSta(async () =>
    {
        var directory = NewDirectory(); var catalog = new CatalogStub(_ => Task.FromResult(Fixture()));
        var window = Window(directory, new(), catalog);
        try
        {
            await window.RefreshStockCatalogAsync(); Code(window).Text = "2330";
            var selected = Selector(window).SelectedItem;
            var file = await File.ReadAllBytesAsync(Path.Combine(directory, "stock-catalog.json"));
            var pending = new TaskCompletionSource<StockCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
            catalog.Get = _ => pending.Task;
            var update = window.RefreshStockCatalogAsync();
            Assert.False(Get<Button>(window, "_refreshStockCatalog").IsEnabled);
            Assert.False(Code(window).IsEnabled); Assert.False(Selector(window).IsEnabled);
            Assert.True(Get<Button>(window, "_cancel").IsEnabled);
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.RefreshStockCatalogAsync());
            Click(window, "_cancel");
            pending.SetResult(Fixture() with { Entries = [new("TWSE", "1111", "Changed"), new("TPEx", "2222", "Changed")] });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
            Assert.Same(selected, Selector(window).SelectedItem); Assert.Equal("2330", Code(window).Text);
            Assert.Equal(file, await File.ReadAllBytesAsync(Path.Combine(directory, "stock-catalog.json")));
            Assert.True(Selector(window).IsEnabled); Assert.True(Get<Button>(window, "_refreshStockCatalog").IsEnabled);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task RefreshRemovalKeepsManualCodeAndDoesNotSelectAnotherStock() => OnSta(async () =>
    {
        var directory = NewDirectory(); var catalog = new CatalogStub(_ => Task.FromResult(Fixture()));
        var window = Window(directory, new(), catalog);
        try
        {
            await window.RefreshStockCatalogAsync(); Code(window).Text = "2330";
            catalog.Get = _ => Task.FromResult(Fixture() with { Entries = Fixture().Entries.Where(entry => entry.Code != "2330").ToArray() });
            await window.RefreshStockCatalogAsync();
            Assert.Equal("2330", Code(window).Text); Assert.Null(Selector(window).SelectedItem);
            Assert.Equal("TWSE", Market(window)); Assert.True(Get<Button>(window, "_download").IsEnabled);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task BrokenLocalListIsReportedAndExplicitUpdateCanRecover() => OnSta(async () =>
    {
        var directory = NewDirectory(); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "stock-catalog.json"), "broken");
        var catalog = new CatalogStub(_ => Task.FromResult(Fixture()));
        var window = Window(directory, new(), catalog);
        try
        {
            await window.StockCatalogLoadTask;
            Assert.Equal(0, catalog.Calls); Assert.True(Code(window).IsEnabled);
            Assert.Contains("清單讀取或更新失敗", Get<TextBlock>(window, "_stockCatalogInfo").Text);
            await window.RefreshStockCatalogAsync();
            Assert.Equal(3, Selector(window).Items.Count);
            Assert.DoesNotContain("失敗", Get<TextBlock>(window, "_stockCatalogInfo").Text);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task ClosingDuringUpdateDiscardsLateResult() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var pending = new TaskCompletionSource<StockCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new CatalogStub(_ => pending.Task);
        var window = Window(directory, new(), catalog);
        try
        {
            await window.StockCatalogLoadTask;
            var update = window.RefreshStockCatalogAsync(); window.Close();
            pending.SetResult(Fixture());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
            Assert.False(File.Exists(Path.Combine(directory, "stock-catalog.json")));
        }
        finally { window.Close(); Cleanup(directory); }
    });

    private static StockCatalog Fixture() => new(new DateTimeOffset(2026, 10, 4, 7, 0, 0, TimeSpan.Zero),
        [new("TWSE", "2330", "台積電"), new("TWSE", "2317", "鴻海"), new("TPEx", "6488", "環球晶")]);
    private sealed class CatalogStub(Func<CancellationToken, Task<StockCatalog>> get) : IStockCatalogProvider
    {
        public int Calls { get; private set; }
        public Func<CancellationToken, Task<StockCatalog>> Get { get; set; } = get;
        public Task<StockCatalog> GetAsync(CancellationToken cancellationToken = default) { Calls++; return Get(cancellationToken); }
    }
    private sealed class HistoryStub : IHistoricalDataProvider
    {
        public int Calls { get; private set; }
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Stock-list operations must not request historical prices."); }
    }
    private static MainWindow Window(string directory, HistoryStub history, CatalogStub catalog, string language = "zh-TW", string theme = "Ink") =>
        new(directory, history, stockCatalogProvider: catalog) { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
    private static TextBox Code(MainWindow window) => Get<TextBox>(window, "_stockCode");
    private static ComboBox Selector(MainWindow window) => Get<ComboBox>(window, "_stockSelector");
    private static string Market(MainWindow window) => (string)((ComboBoxItem)Get<ComboBox>(window, "_downloadMarket").SelectedItem).Tag;
    private static void SelectMarket(MainWindow window, string market) => Get<ComboBox>(window, "_downloadMarket").SelectedItem =
        Get<ComboBox>(window, "_downloadMarket").Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == market);
    private static void Click(MainWindow window, string field) => Get<Button>(window, field).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Get<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "HistoLens.CatalogUiTests", Guid.NewGuid().ToString("N"));
    private static void Cleanup(string directory) { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private static FrameworkElement Layout(MainWindow window)
    {
        var frame = (FrameworkElement)window.Content;
        frame.Measure(new Size(900, 660)); frame.Arrange(new Rect(0, 0, 900, 660)); frame.UpdateLayout();
        return frame;
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }
    private static void SaveImage(FrameworkElement frame, string name)
    {
        if (Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap(900, 660, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
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
