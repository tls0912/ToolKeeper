using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HistoLens.Core;
using HistoLens.Data;
using ToolKeeper.UI;
using Xunit;

namespace HistoLens.Tests;

[Collection("HistoLens WPF UI")]
public sealed class LegacyMarketDataWindowTests
{
    [Fact]
    public Task WindowStartsWithoutPickingAStockOrDownloading() => OnSta(async () =>
    {
        var provider = new StubProvider((_, _, _) => throw new InvalidOperationException("No automatic network call."));
        var directory = NewDirectory();
        var window = new MainWindow(directory, provider) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            Assert.Equal(0, provider.Calls); Assert.Null(window.Snapshot); Assert.Null(window.Result);
            Assert.Equal("", Get<TextBox>(window, "_stockCode").Text);
            var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time")).DateTime).AddDays(-1);
            Assert.Equal(yesterday.ToDateTime(TimeOnly.MinValue), Get<DatePicker>(window, "_downloadEnd").SelectedDate);
            Assert.Equal(yesterday.AddYears(-1).ToDateTime(TimeOnly.MinValue), Get<DatePicker>(window, "_downloadStart").SelectedDate);
            Assert.Contains("點擊下載才連線", Get<TextBlock>(window, "_notice").Text);
            await Task.CompletedTask;
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Theory]
    [InlineData("zh-TW")]
    [InlineData("en")]
    [InlineData("ja")]
    public Task DownloadDateComponentsSendSelectedDaysWithoutTimeZoneConversion(string language) => OnSta(async () =>
    {
        var directory = NewDirectory();
        var start = new DateOnly(2024, 2, 29); var end = new DateOnly(2025, 1, 1);
        HistoricalDataRequest? received = null;
        var provider = new StubProvider((request, _, _) => { received = request; return Task.FromResult(Fixture()); });
        var window = new MainWindow(directory, provider) { PreferencesPath = null, SelectedLanguage = language };
        try
        {
            Get<TextBox>(window, "_stockCode").Text = "2330";
            var first = Get<DatePicker>(window, "_downloadStart"); var last = Get<DatePicker>(window, "_downloadEnd");
            first.Text = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            last.Text = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert.Equal(start.ToDateTime(TimeOnly.MinValue), first.SelectedDate);
            Assert.Equal(end.ToDateTime(TimeOnly.MinValue), last.SelectedDate);
            await SubmitDownload(window);
            Assert.Equal(1, provider.Calls);
            Assert.Equal("2330", received!.Code); Assert.Equal(start, received.Start); Assert.Equal(end, received.End);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Theory]
    [InlineData("empty-start")]
    [InlineData("empty-end")]
    [InlineData("invalid-start")]
    [InlineData("invalid-end")]
    [InlineData("invalid-focus")]
    [InlineData("reversed")]
    public Task InvalidDownloadDateEditsCannotReusePreviousSelectionOrEraseResearch(string edit) => OnSta(async () =>
    {
        var directory = NewDirectory();
        var provider = new StubProvider((_, _, _) => throw new InvalidOperationException("Invalid dates must not download."));
        var window = new MainWindow(directory, provider) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var snapshot = window.Snapshot; var result = window.Result;
            Get<TextBox>(window, "_stockCode").Text = "2330";
            var start = Get<DatePicker>(window, "_downloadStart"); var end = Get<DatePicker>(window, "_downloadEnd");
            start.SelectedDate = new DateTime(2024, 1, 1); end.SelectedDate = new DateTime(2024, 12, 31);
            switch (edit)
            {
                case "empty-start": start.Text = ""; break;
                case "empty-end": end.Text = ""; break;
                case "invalid-start": start.Text = "2024-02-30"; Assert.Null(start.SelectedDate); break;
                case "invalid-end": end.Text = "not-a-date"; Assert.Null(end.SelectedDate); break;
                case "invalid-focus":
                    start.ApplyTemplate();
                    var editor = Assert.IsType<DatePickerTextBox>(start.Template.FindName("PART_TextBox", start));
                    editor.Text = "2024-02-30";
                    Assert.Equal(new DateTime(2024, 1, 1), start.SelectedDate);
                    editor.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                    Assert.Null(start.SelectedDate);
                    break;
                case "reversed": start.SelectedDate = new DateTime(2025, 1, 1); break;
            }
            await SubmitDownload(window);
            Assert.Equal(0, provider.Calls); Assert.Same(snapshot, window.Snapshot); Assert.Same(result, window.Result);
            Assert.False(window.IsResultStale); Assert.True(Get<Button>(window, "_save").IsEnabled);
            Assert.Contains("下載設定有誤", Get<TextBlock>(window, "_status").Text);
            Assert.Empty(new MarketDataStore(Path.Combine(directory, "market-data")).List());
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Theory]
    [InlineData("zh-TW", "Ink")]
    [InlineData("en", "Dark")]
    [InlineData("ja", "InkDark")]
    public Task DownloadCalendarUsesThemeAndKeepsDatesAcrossPreferences(string language, string theme) => OnSta(async () =>
    {
        var directory = NewDirectory();
        var window = new MainWindow(directory, new StubProvider((_, _, _) => throw new InvalidOperationException("No network.")))
            { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
        try
        {
            Get<Button>(window, "_dataNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(900, 660)); frame.Arrange(new Rect(0, 0, 900, 660)); frame.UpdateLayout();
            var input = Get<DatePicker>(window, "_downloadStart");
            var selected = new DateTime(2024, 2, 29); input.SelectedDate = selected;
            Assert.True(input.ActualHeight >= 28); Assert.True(input.ActualWidth > 100);
            Assert.IsType<Button>(input.Template.FindName("PART_Button", input));
            var editor = Assert.IsType<DatePickerTextBox>(input.Template.FindName("PART_TextBox", input));
            Assert.Equal(((SolidColorBrush)window.FindResource("TextBrush")).Color, ((SolidColorBrush)editor.Foreground).Color);
            var popup = Assert.IsType<Popup>(input.Template.FindName("PART_Popup", input));
            var calendar = Assert.IsType<System.Windows.Controls.Calendar>(popup.Child);
            calendar.Measure(new Size(300, 300)); calendar.Arrange(new Rect(0, 0, 300, 300)); calendar.UpdateLayout();
            Assert.Equal(42, Descendants(calendar).OfType<CalendarDayButton>().Count());
            Assert.All(Descendants(calendar).OfType<CalendarDayButton>(), day =>
                Assert.Equal(((SolidColorBrush)window.FindResource(day.IsInactive ? "MutedBrush" : "TextBrush")).Color, ((SolidColorBrush)day.Foreground).Color));
            var item = Descendants(calendar).OfType<CalendarItem>().Single();
            var before = calendar.DisplayDate;
            Assert.IsType<Button>(item.Template.FindName("PART_NextButton", item)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(before.AddMonths(1).Month, calendar.DisplayDate.Month);
            var picked = new DateTime(2024, 3, 12);
            var dayButton = Descendants(calendar).OfType<CalendarDayButton>().Single(day => Equals(day.DataContext, picked));
            dayButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(picked, input.SelectedDate);
            calendar.UpdateLayout();
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap(300, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(calendar);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, $"histolens-calendar-{language}-{theme}.png")); encoder.Save(stream);
            }
            window.SelectedLanguage = language == "en" ? "ja" : "en";
            window.SelectedTheme = theme == "Dark" ? "Ink" : "Dark";
            Assert.Equal(picked, input.SelectedDate);
            Assert.Equal(picked, DateTime.Parse(input.Text, input.Language.GetSpecificCulture()));
            await Task.CompletedTask;
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task OpeningAnotherWindowDoesNotBlockOnAnActiveDownloadLock() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new MainWindow(directory, new StubProvider(async (_, _, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The test only cancels this request.");
        })) { PreferencesPath = null };
        using var cancellation = new CancellationTokenSource();
        MainWindow? second = null;
        var pending = first.DownloadMarketDataAsync(Request(Fixture()), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // A watchdog releases the first lock if construction accidentally becomes synchronous again.
            cancellation.CancelAfter(TimeSpan.FromSeconds(5));
            var offline = new StubProvider((_, _, _) => throw new InvalidOperationException("No automatic download."));
            second = new MainWindow(directory, offline) { PreferencesPath = null };
            Assert.False(cancellation.IsCancellationRequested);
            Assert.Equal(0, offline.Calls);
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            if (second is not null)
            {
                await second.RefreshMarketDataAsync();
                second.Close();
            }
            first.Close(); Cleanup(directory);
        }
    });

    [Fact]
    public Task CorruptCacheOnStartupKeepsItsVisibleErrorInsteadOfTheDefaultPrompt() => OnSta(async () =>
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(Path.Combine(directory, "market-data"));
        await File.WriteAllTextAsync(Path.Combine(directory, "market-data", "corrupt.twse-data.json"), "{invalid");
        var provider = new StubProvider((_, _, _) => throw new InvalidOperationException("No automatic download."));
        var window = new MainWindow(directory, provider) { PreferencesPath = null };
        try
        {
            await window.RefreshMarketDataAsync();
            Assert.Contains("Market data JSON is invalid", Get<TextBlock>(window, "_status").Text);
            Assert.Equal(0, provider.Calls);
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "market-data"), "*.twse-data.json"));
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task LegacySourceFixtureIsSavedBeforeDisplayAndReopenedOffline() => OnSta(async () =>
    {
        var download = Fixture();
        var directory = NewDirectory();
        var provider = new StubProvider((request, progress, _) =>
        {
            Assert.Equal("2330", request.Code);
            progress?.Report(new(1, 2, "行情"));
            return Task.FromResult(download);
        });
        var window = new MainWindow(directory, provider) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            Assert.NotNull(window.Result);
            await window.DownloadMarketDataAsync(Request(download));
            Assert.Equal(1, provider.Calls); Assert.Same(download.Snapshot, window.Snapshot);
            Assert.Null(window.Result); Assert.False(window.IsResultStale);
            var files = new MarketDataStore(Path.Combine(directory, "market-data")).List();
            Assert.Single(files); Assert.Equal(2, Get<ComboBox>(window, "_marketData").Items.Count);
            Assert.False(window.Snapshot!.IsSynthetic);
            Assert.Contains("本機舊資料", Get<TextBlock>(window, "_notice").Text);
            Assert.Contains("2330 · 台積電", Get<TextBlock>(window, "_dataInfo").Text);
            Assert.Contains("本機舊資料", Get<TextBlock>(window, "_summary").Text);
            Assert.Contains("本機舊資料", Get<TextBlock>(window, "_sourceMethods").Text);
            Assert.Contains(Get<DataGrid>(window, "_quality").Items.Cast<DataIssue>(), issue => issue.Code == "ProviderNote");
            Assert.Equal(download.Snapshot.Calendar.TradingDates[4].ToString("yyyy-MM-dd"), Get<TextBox>(window, "_start").Text);
            await window.RunResearchAsync();
            Assert.True(window.Result!.IsResearchAllowed); Assert.False(window.Result.IsSynthetic);
            Assert.NotEmpty(window.Result.Cases);
            var path = await window.SaveResearchAsync();
            window.LoadDemo();
            Assert.Contains("合成測試資料", Get<TextBlock>(window, "_notice").Text);
            await window.LoadMarketDataAsync(files.Single());
            Assert.Equal(1, provider.Calls); Assert.Null(window.Result);
            Assert.Equal(download.Snapshot.ContentHash, window.Snapshot!.ContentHash);
            Assert.Equal(download.Snapshot.Calendar.CoverageStart.ToDateTime(TimeOnly.MinValue), Get<DatePicker>(window, "_downloadStart").SelectedDate);
            Assert.Equal(download.Snapshot.Calendar.CoverageEnd.ToDateTime(TimeOnly.MinValue), Get<DatePicker>(window, "_downloadEnd").SelectedDate);
            Assert.Contains(Get<DataGrid>(window, "_quality").Items.Cast<DataIssue>(), issue => issue.Code == "ProviderNote");
            await window.LoadResearchAsync(path);
            Assert.False(window.Result!.IsSynthetic);
            Assert.Contains("本機舊資料", Get<TextBlock>(window, "_notice").Text);
            window.SelectedLanguage = "en";
            Assert.Contains("LEGACY LOCAL DATA", Get<TextBlock>(window, "_summary").Text);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task IncompleteCoverageWarnsForScanningAndStillBlocksAdvancedResearchAfterOfflineOpen() => OnSta(async () =>
    {
        var download = Fixture(verifiedActions: false);
        var directory = NewDirectory();
        var window = new MainWindow(directory, new StubProvider((_, _, _) => Task.FromResult(download)))
            { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            await window.DownloadMarketDataAsync(Request(download));
            Assert.Null(window.Result);
            Assert.Contains(Get<DataGrid>(window, "_quality").Items.Cast<DataIssue>(),
                issue => issue.Code == "CorporateActionCoverageUnknown" && issue.BlocksResearch);
            Assert.Contains("可用原始價格掃描", Get<TextBlock>(window, "_status").Text);
            Assert.Equal(download.Snapshot.Bars.Count, Get<DataGrid>(window, "_marketBars").Items.Count);
            Assert.Equal(download.Snapshot.Bars.OrderBy(bar => bar.Date).Select(bar => bar.Date),
                Get<DataGrid>(window, "_marketBars").Items.Cast<DailyBar>().Select(bar => bar.Date));
            var path = new MarketDataStore(Path.Combine(directory, "market-data")).List().Single();
            await window.RunSimilarityAsync();
            Assert.True(window.SimilarityResult!.IsAllowed);
            Assert.Contains(window.SimilarityResult.Reference!.Warnings, issue => issue.Code == "CorporateActionCoverageUnknown");
            await window.RunResearchAsync();
            Assert.Null(window.Result); Assert.False(Get<Button>(window, "_save").IsEnabled);
            window.LoadDemo(); await window.LoadMarketDataAsync(path); await window.RunResearchAsync();
            Assert.Null(window.Result);
            Assert.Contains(Get<DataGrid>(window, "_quality").Items.Cast<DataIssue>(),
                issue => issue.Code == "CorporateActionCoverageUnknown" && issue.BlocksResearch);
            Assert.False(Get<Button>(window, "_save").IsEnabled);
            await window.RunSimilarityAsync();
            Assert.True(window.SimilarityResult!.IsAllowed);
            Assert.Contains("公司行動覆蓋未知", Get<TextBlock>(window, "_similaritySummary").Text);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task BlockingProviderDiagnosticCannotBeIgnoredEvenWithVerifiedSnapshot() => OnSta(async () =>
    {
        var download = Fixture() with { Diagnostics = [new() { Code = "ProviderBlocked", Message = "尚未確認", BlocksResearch = true }] };
        var directory = NewDirectory();
        var window = new MainWindow(directory, new StubProvider((_, _, _) => Task.FromResult(download))) { PreferencesPath = null };
        try
        {
            await window.DownloadMarketDataAsync(Request(download)); await window.RunResearchAsync();
            Assert.Null(window.Result); Assert.False(Get<Button>(window, "_save").IsEnabled);
            Assert.Contains(Get<DataGrid>(window, "_quality").Items.Cast<DataIssue>(), issue => issue.Code == "ProviderBlocked");
            var path = new MarketDataStore(Path.Combine(directory, "market-data")).List().Single();
            window.LoadDemo(); await window.LoadMarketDataAsync(path); await window.RunResearchAsync();
            Assert.Null(window.Result);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DownloadOrSnapshotSaveFailureKeepsTheCompletedResearch(bool saveFailure) => OnSta(async () =>
    {
        var download = Fixture();
        var directory = NewDirectory();
        var provider = new StubProvider((_, _, _) => saveFailure ? Task.FromResult(download) : Task.FromException<HistoricalDataDownload>(new IOException("測試失敗")));
        var window = new MainWindow(directory, provider) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var snapshot = window.Snapshot; var result = window.Result; var cases = Get<DataGrid>(window, "_cases").ItemsSource;
            if (saveFailure)
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "market-data"), "This file prevents creating the data directory.");
            }
            await Assert.ThrowsAnyAsync<IOException>(() => window.DownloadMarketDataAsync(Request(download)));
            Assert.Same(snapshot, window.Snapshot); Assert.Same(result, window.Result);
            Assert.Same(cases, Get<DataGrid>(window, "_cases").ItemsSource); Assert.False(window.IsResultStale);
            Assert.Contains("保留原資料與研究", Get<TextBlock>(window, "_status").Text);
            Assert.True(Get<Button>(window, "_save").IsEnabled); Assert.False(Get<Button>(window, "_cancel").IsEnabled);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Theory]
    [InlineData("cancel")]
    [InlineData("download-edit")]
    [InlineData("date-edit")]
    [InlineData("research-edit")]
    [InlineData("close")]
    public Task ObsoleteProviderCompletionNeverReplacesThePreviousWork(string trigger) => OnSta(async () =>
    {
        var download = Fixture();
        var completion = new TaskCompletionSource<HistoricalDataDownload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var directory = NewDirectory();
        var window = new MainWindow(directory, new StubProvider((_, _, _) => completion.Task)) { PreferencesPath = null };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var snapshot = window.Snapshot; var result = window.Result; var cases = Get<DataGrid>(window, "_cases").ItemsSource;
            var pending = window.DownloadMarketDataAsync(Request(download));
            Assert.True(Get<Button>(window, "_cancel").IsEnabled);
            Assert.False(Get<DatePicker>(window, "_downloadStart").IsEnabled);
            Assert.False(Get<DatePicker>(window, "_downloadEnd").IsEnabled);
            switch (trigger)
            {
                case "cancel": Get<Button>(window, "_cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case "download-edit": Get<TextBox>(window, "_stockCode").Text = "2317"; break;
                case "date-edit": Get<DatePicker>(window, "_downloadStart").SelectedDate = new DateTime(2024, 2, 29); break;
                case "research-edit": Get<TextBox>(window, "_upper").Text = "7"; break;
                case "close": window.Close(); break;
            }
            completion.SetResult(download); // Deliberately ignores the cancellation token in the fake provider.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Same(snapshot, window.Snapshot); Assert.Same(result, window.Result);
            Assert.Same(cases, Get<DataGrid>(window, "_cases").ItemsSource);
            Assert.Equal(trigger == "research-edit", window.IsResultStale);
            Assert.Empty(new MarketDataStore(Path.Combine(directory, "market-data")).List());
            if (trigger != "close")
            {
                Assert.False(Get<Button>(window, "_cancel").IsEnabled);
                Assert.True(Get<DatePicker>(window, "_downloadStart").IsEnabled);
                Assert.True(Get<DatePicker>(window, "_downloadEnd").IsEnabled);
            }
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Fact]
    public Task FailedOrCancelledOfflineOpenPreservesTheExistingWork() => OnSta(async () =>
    {
        var download = Fixture();
        var directory = NewDirectory();
        var window = new MainWindow(directory, new StubProvider((_, _, _) => Task.FromResult(download))) { PreferencesPath = null };
        try
        {
            await window.DownloadMarketDataAsync(Request(download)); await window.RunResearchAsync();
            var snapshot = window.Snapshot; var result = window.Result;
            var path = new MarketDataStore(Path.Combine(directory, "market-data")).List().Single();
            var broken = Path.Combine(directory, "broken.twse-data.json");
            await File.WriteAllTextAsync(broken, "{}");
            await Assert.ThrowsAnyAsync<InvalidDataException>(() => window.LoadMarketDataAsync(broken));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.LoadMarketDataAsync(path, cancellation.Token));
            Assert.Same(snapshot, window.Snapshot); Assert.Same(result, window.Result);
            Assert.True(Get<Button>(window, "_save").IsEnabled); Assert.False(Get<Button>(window, "_cancel").IsEnabled);
        }
        finally { window.Close(); Cleanup(directory); }
    });

    [Theory]
    [InlineData("zh-TW", "Ink", 900, 660)]
    [InlineData("en", "Dark", 1280, 880)]
    public Task LegacyDataAndRawBarsRenderInTheSharedShell(string language, string theme, int width, int height) => OnSta(async () =>
    {
        var directory = NewDirectory(); var download = Fixture(verifiedActions: false);
        var window = new MainWindow(directory, new StubProvider((_, _, _) => Task.FromResult(download)))
            { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
        try
        {
            await window.DownloadMarketDataAsync(Request(download));
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(width, height)); frame.Arrange(new Rect(0, 0, width, height)); frame.UpdateLayout();
            var tabs = Descendants(frame).OfType<TabControl>().Single();
            Get<Expander>(window, "_advancedResearch").IsExpanded = true;
            tabs.SelectedIndex = tabs.Items.Count - 1; frame.UpdateLayout();
            var raw = Get<DataGrid>(window, "_marketBars");
            Assert.True(raw.ActualHeight > 100); Assert.True(raw.ActualWidth > 200);
            Assert.True(raw.EnableRowVirtualization); Assert.Equal(7, raw.Columns.Count);
            Assert.Equal(download.Snapshot.Bars.Count, raw.Items.Count);
            Assert.True(Get<Button>(window, "_scanButton").ActualHeight > 0);
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(folder, $"histolens-legacy-{language}-{theme}-{width}.png")); encoder.Save(stream);
            }
        }
        finally { window.Close(); Cleanup(directory); }
    });

    private static HistoricalDataRequest Request(HistoricalDataDownload download) =>
        new("2330", download.Snapshot.Calendar.CoverageStart, download.Snapshot.DataAsOf);

    private static Task SubmitDownload(MainWindow window) => (Task)typeof(MainWindow)
        .GetMethod("DownloadFromUi", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;

    private static HistoricalDataDownload Fixture(bool verifiedActions = true)
    {
        var synthetic = DemoData.Create();
        var snapshot = synthetic with
        {
            SnapshotId = "", ContentHash = "", IsSynthetic = false, SourceId = "TWSE",
            Instrument = synthetic.Instrument with { InstrumentId = "TWSE:2330", Code = "2330", Name = "台積電", Market = "TWSE", SecurityType = SecurityType.CommonStock },
            Calendar = synthetic.Calendar with { Market = "TWSE", Version = "test-calendar" },
            ActionCoverage = synthetic.ActionCoverage with { IsVerified = verifiedActions, SourceId = "TWSE", Version = "test-actions" },
            Bars = synthetic.Bars.Select(bar => bar with { SourceId = "TWSE" }).ToArray(),
            CorporateActions = []
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        snapshot = snapshot with { SnapshotId = hash, ContentHash = hash };
        return new(snapshot, [new() { Code = "ProviderNote", Message = "測試供應商備註" }]);
    }

    private sealed class StubProvider(Func<HistoricalDataRequest, IProgress<DownloadProgress>?, CancellationToken, Task<HistoricalDataDownload>> download) : IHistoricalDataProvider
    {
        public int Calls { get; private set; }
        public Task<HistoricalDataDownload> DownloadAsync(HistoricalDataRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        { Calls++; return download(request, progress, cancellationToken); }
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "HistoLens.TwseUiTests", Guid.NewGuid().ToString("N"));
    private static void Cleanup(string directory) { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private static T Get<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
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
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
