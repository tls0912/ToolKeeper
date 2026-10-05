using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HistoLens.Core;
using ToolKeeper.UI;
using Xunit;

namespace HistoLens.Tests;

[Collection("HistoLens WPF UI")]
public sealed class MainWindowTests
{
    [Fact]
    public Task DemoResearchCanBeSavedReopenedAndEditedWithoutMixingResults() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.UiTests", Guid.NewGuid().ToString("N"));
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            Assert.IsType<WindowFrame>(window.Content);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal("Ink", window.SelectedTheme);
            Assert.Null(window.Snapshot); Assert.Null(window.Result);
            window.LoadDemo();
            Assert.True(window.Snapshot!.IsSynthetic);
            await window.RunResearchAsync();
            Assert.True(window.Result!.IsResearchAllowed);
            Assert.NotEmpty(window.Result.Cases);
            var original = window.Result;
            var path = await window.SaveResearchAsync();
            var output = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                File.Copy(path, Path.Combine(output, "demo.histolens.json"), overwrite: true);
                await File.WriteAllTextAsync(Path.Combine(output, "demo.snapshot.json"),
                    System.Text.Json.JsonSerializer.Serialize(window.Snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            Get<TextBox>(window, "_upper").Text = "7";
            Assert.True(window.IsResultStale); Assert.Same(original, window.Result);
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.SaveResearchAsync());
            window.SelectedLanguage = "ja"; window.SelectedTheme = "InkDark";
            Assert.Same(original, window.Result); Assert.Equal("7", Get<TextBox>(window, "_upper").Text);
            await window.LoadResearchAsync(path);
            Assert.False(window.IsResultStale);
            Assert.Equal(5m, decimal.Parse(Get<TextBox>(window, "_upper").Text, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(original.DataContentHash, window.Result!.DataContentHash);
            Assert.Equal(original.Definition.Conditions, window.Result.Definition.Conditions);
            await window.RunResearchAsync();
            Assert.Equal(original.Funnel, window.Result.Funnel);

            // A failed file open must not discard the last valid result.
            var valid = window.Result;
            var broken = Path.Combine(directory, "broken.histolens.json");
            await File.WriteAllTextAsync(broken, "{}");
            await Assert.ThrowsAnyAsync<Exception>(() => window.LoadResearchAsync(broken));
            Assert.Same(valid, window.Result);
            // Edit while a worker is queued: its obsolete result may not replace the previous run.
            var pending = window.RunResearchAsync();
            Get<TextBox>(window, "_upper").Text = "9";
            await pending;
            Assert.True(window.IsResultStale); Assert.Same(valid, window.Result);
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Fact]
    public Task EveryTemplateRoundTripsItsEditableParameters() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.UiTests", Guid.NewGuid().ToString("N"));
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            window.LoadDemo();
            for (var index = 0; index < ResearchTemplates.All.Count; index++)
            {
                Get<ComboBox>(window, "_template").SelectedIndex = index;
                Get<TextBox>(window, "_lookback").Text = index == 1 ? "4" : "30";
                if (index is 0 or 2) Get<TextBox>(window, "_threshold").Text = index == 0 ? "12" : "1.8";
                await window.RunResearchAsync();
                var original = window.Result!;
                Assert.True(original.IsResearchAllowed);
                var file = await window.SaveResearchAsync();
                window.LoadDemo(); await window.LoadResearchAsync(file); await window.RunResearchAsync();
                Assert.Equal(original.Definition.Conditions, window.Result!.Definition.Conditions);
                Assert.Equal(original.Funnel, window.Result.Funnel);
            }
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Theory]
    [InlineData("zh-TW", "Ink", 900, 660)]
    [InlineData("en", "Dark", 1280, 880)]
    [InlineData("ja", "InkDark", 1280, 880)]
    public Task SharedShellRendersAndThemeChangesKeepResearch(string language, string theme, int width, int height) => OnSta(async () =>
    {
        var window = new MainWindow(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))) { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var result = window.Result;
            Assert.NotNull(result);
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(width, height)); frame.Arrange(new Rect(0, 0, width, height)); frame.UpdateLayout();
            Assert.True(Get<DataGrid>(window, "_stats").ActualWidth > 200);
            Assert.True(Get<DataGrid>(window, "_stats").ActualHeight > 100);
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                Render(frame, folder, $"histolens-{language}-{theme}-{width}.png", width, height);
            }
            var tabs = Descendants(frame).OfType<TabControl>().Single();
            tabs.SelectedIndex = 2; frame.UpdateLayout();
            Assert.True(Get<DataGrid>(window, "_cases").ActualHeight >= 45);
            Assert.True(Get<DataGrid>(window, "_bars").ActualHeight >= 45);
            if (!string.IsNullOrEmpty(folder)) Render(frame, folder, $"histolens-cases-{language}-{theme}-{width}.png", width, height);
            foreach (var next in new[] { "Light", "Dark", "Ink", "InkDark", "System" })
            { window.SelectedTheme = next; Assert.Same(result, window.Result); }
        }
        finally { window.Close(); }
    });

    private static T Get<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    [Fact]
    public Task HistoricalCutoffAlsoLimitsTheCaseChartAndRawTable() => OnSta(async () =>
    {
        var window = new MainWindow(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))) { PreferencesPath = null };
        try
        {
            window.LoadDemo();
            var cutoff = window.Snapshot!.Calendar.TradingDates[160];
            Get<TextBox>(window, "_end").Text = cutoff.ToString("yyyy-MM-dd");
            Get<TextBox>(window, "_asOf").Text = cutoff.ToString("yyyy-MM-dd");
            Get<ComboBox>(window, "_sampling").SelectedItem = SamplingPolicy.EveryMatch;
            await window.RunResearchAsync();
            var cases = Get<DataGrid>(window, "_cases");
            var excluded = cases.Items.Cast<object>().First(item =>
                ((HorizonOutcome)item.GetType().GetProperty("Outcome")!.GetValue(item)!).PrimaryExclusion == ExclusionReason.InsufficientFutureData);
            cases.SelectedItem = excluded;
            var bars = Get<DataGrid>(window, "_bars").Items.Cast<DailyBar>().ToArray();
            Assert.NotEmpty(bars);
            Assert.All(bars, bar => Assert.True(bar.Date <= cutoff));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task NumericColumnsSortOriginalValuesAndKeepTheCompletedResearch() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            var (snapshot, run) = SortingFixture();
            var path = await new ResearchStore(directory).SaveAsync(snapshot, run);
            await window.LoadResearchAsync(path);
            var original = window.Result;
            var cases = Get<DataGrid>(window, "_cases");
            var rawChanges = cases.Items.Cast<object>().Select(Outcome).Select(o => o.PriceChange).ToArray();
            Assert.Contains(rawChanges, value => value < 0);
            Assert.Contains(rawChanges, value => value > 0);
            Assert.Contains(0m, rawChanges); Assert.Contains(null, rawChanges);
            foreach (var (column, value) in new (string Column, Func<HorizonOutcome, decimal?> Value)[]
            {
                ("Change", o => o.PriceChange), ("High", o => o.HighestPriceChange),
                ("Low", o => o.LowestPriceChange), ("Mdd", o => o.CloseMaxDrawdown),
                ("Upper", o => o.UpperFirstHitTradingDay), ("Lower", o => o.LowerFirstHitTradingDay)
            })
            {
                Sort(cases, column);
                var ordered = cases.Items.Cast<object>().Select(Outcome).Select(value).ToArray();
                Assert.Equal(ordered.OrderBy(item => item), ordered);
            }
            var hitDays = cases.Items.Cast<object>().Select(Outcome).Select(o => o.UpperFirstHitTradingDay).ToArray();
            Assert.Contains(2, hitDays); Assert.Contains(10, hitDays);
            var stats = Get<DataGrid>(window, "_stats");
            foreach (var (column, value) in new (string Column, Func<HorizonStatistics, decimal?> Value)[]
            {
                ("Mean", s => s.MeanPriceChange), ("Median", s => s.MedianPriceChange), ("Up", s => s.UpRate),
                ("Flat", s => s.FlatRate), ("Down", s => s.DownRate), ("Best", s => s.BestPriceChange),
                ("Worst", s => s.WorstPriceChange), ("Upper", s => s.UpperHitRate), ("Lower", s => s.LowerHitRate)
            })
            {
                Sort(stats, column);
                var ordered = stats.Items.Cast<object>().Select(item => (HorizonStatistics)item.GetType().GetProperty("Statistics")!.GetValue(item)!).Select(value).ToArray();
                Assert.Equal(ordered.OrderBy(item => item), ordered);
            }
            Assert.False(Column(stats, "Order").CanUserSort);
            Assert.Same(original, window.Result);
            Assert.Equal(run.Statistics.Select(s => s.ValidCount), window.Result!.Statistics.Select(s => s.ValidCount));
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    });

    [Fact]
    public Task BlockedRunKeepsPreviousCasesAndShowsItsOwnQualityReasons() => OnSta(async () =>
    {
        var window = new MainWindow(NewDirectory()) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var original = window.Result;
            var cases = Get<DataGrid>(window, "_cases");
            cases.SelectedIndex = Math.Min(2, cases.Items.Count - 1);
            var rows = cases.ItemsSource; var selection = cases.SelectedItem;
            Get<TextBox>(window, "_asOf").Text = window.Snapshot!.DataAsOf.AddDays(1).ToString("yyyy-MM-dd");
            await window.RunResearchAsync();
            Assert.Same(original, window.Result); Assert.True(window.IsResultStale);
            Assert.Same(rows, cases.ItemsSource); Assert.Same(selection, cases.SelectedItem);
            Assert.Contains(Get<DataGrid>(window, "_quality").Items.Cast<DataIssue>(), issue => issue.Code == "AsOfBeyondSnapshot" && issue.BlocksResearch);
            Assert.Contains("保留前次結果", Get<TextBlock>(window, "_status").Text);
            Assert.False(Get<Button>(window, "_save").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("null-statistics")]
    [InlineData("extreme-threshold")]
    [InlineData("unsupported-template")]
    [InlineData("unsupported-template-version")]
    public Task InvalidSavedContentRetainsAllWorkAndEditableState(string failure) => OnSta(async () =>
    {
        var directory = NewDirectory();
        var window = new MainWindow(directory) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var original = window.Result!; var snapshot = window.Snapshot!;
            var cases = Get<DataGrid>(window, "_cases");
            cases.SelectedIndex = Math.Min(2, cases.Items.Count - 1);
            var rows = cases.ItemsSource; var selection = cases.SelectedItem;
            var stats = Get<DataGrid>(window, "_stats").ItemsSource;
            var bars = Get<DataGrid>(window, "_bars").ItemsSource;
            var detail = Get<TextBlock>(window, "_detail").Text;
            var malformed = failure switch
            {
                "null-statistics" => original with { Statistics = null! },
                "extreme-threshold" => original with { Definition = original.Definition with { UpperThreshold = decimal.MaxValue } },
                "unsupported-template" => original with { Definition = original.Definition with { TemplateId = "unsupported" } },
                _ => original with { Definition = original.Definition with { TemplateVersion = "unknown" } }
            };
            var path = await WriteResearchFile(directory, snapshot, malformed);
            await Assert.ThrowsAnyAsync<Exception>(() => window.LoadResearchAsync(path));
            Assert.Same(snapshot, window.Snapshot); Assert.Same(original, window.Result);
            Assert.Same(rows, cases.ItemsSource); Assert.Same(selection, cases.SelectedItem);
            Assert.Same(stats, Get<DataGrid>(window, "_stats").ItemsSource);
            Assert.Same(bars, Get<DataGrid>(window, "_bars").ItemsSource);
            Assert.Equal(detail, Get<TextBlock>(window, "_detail").Text);
            Assert.False(Get<bool>(window, "_updating")); Assert.False(window.IsResultStale);
            Get<TextBox>(window, "_upper").Text = "8";
            Assert.True(window.IsResultStale); Assert.Same(original, window.Result);
            Assert.True(Get<Button>(window, "_runButton").IsEnabled);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    });

    [Fact]
    public Task ReorderedSnapshotKeepsCaseAndChartDatesInChronologicalOrder() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var original = window.Result!;
            var expected = Get<DataGrid>(window, "_bars").Items.Cast<DailyBar>().Select(bar => bar.Date).ToArray();
            var snapshot = window.Snapshot! with { Bars = window.Snapshot!.Bars.Reverse().ToArray() };
            var path = await new ResearchStore(directory).SaveAsync(snapshot, original);
            await window.LoadResearchAsync(path);
            Assert.Equal(original.DataContentHash, window.Result!.DataContentHash);
            Assert.Equal(JsonSerializer.Serialize(original.Statistics), JsonSerializer.Serialize(window.Result.Statistics));
            var dates = Get<DataGrid>(window, "_bars").Items.Cast<DailyBar>().Select(bar => bar.Date).ToArray();
            Assert.Equal(expected, dates); Assert.Equal(dates.Order(), dates);
            var chart = Get<object>(window, "_chart");
            var chartBars = (IReadOnlyList<DailyBar>)chart.GetType().GetField("_bars", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;
            Assert.Equal(dates, chartBars.Select(bar => bar.Date));
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    });

    [Fact]
    public Task FirstResearchCancelledByEditReturnsToReadyState() => OnSta(async () =>
    {
        var window = new MainWindow(NewDirectory()) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); var pending = window.RunResearchAsync();
            Get<TextBox>(window, "_upper").Text = "7";
            await pending;
            Assert.Null(window.Result); Assert.False(window.IsResultStale);
            Assert.Contains("設定已變更", Get<TextBlock>(window, "_status").Text);
            Assert.DoesNotContain("研究中", Get<TextBlock>(window, "_status").Text);
            Assert.True(Get<Button>(window, "_runButton").IsEnabled);
            Assert.False(Get<Button>(window, "_cancel").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LanguageChangesRefreshSelectedCaseWithoutRunningResearch() => OnSta(async () =>
    {
        var window = new MainWindow(NewDirectory()) { PreferencesPath = null, SelectedLanguage = "zh-TW" };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var result = window.Result; var cases = Get<DataGrid>(window, "_cases");
            cases.SelectedIndex = Math.Min(2, cases.Items.Count - 1); var selection = cases.SelectedItem;
            foreach (var (language, expected) in new[] { ("en", "Close-price path"), ("ja", "終値の推移"), ("zh-TW", "收盤價格路徑") })
            {
                window.SelectedLanguage = language;
                Assert.Contains(expected, Get<TextBlock>(window, "_detail").Text);
                Assert.Same(result, window.Result); Assert.Same(selection, cases.SelectedItem);
                Assert.False(window.IsResultStale);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CancelledSaveAndLoadReleaseBusyStateAndRetainResearch() => OnSta(async () =>
    {
        var directory = NewDirectory();
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var original = window.Result; var snapshot = window.Snapshot;
            var path = await window.SaveResearchAsync();
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.SaveResearchAsync(cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => window.LoadResearchAsync(path, cancellation.Token));
            Assert.Same(original, window.Result); Assert.Same(snapshot, window.Snapshot);
            Assert.True(Get<Button>(window, "_save").IsEnabled);
            Assert.True(Get<Button>(window, "_open").IsEnabled);
            Assert.False(Get<Button>(window, "_cancel").IsEnabled);
            var pending = window.LoadResearchAsync(path);
            Get<TextBox>(window, "_upper").Text = "9";
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(window.IsResultStale); Assert.Same(original, window.Result);
            Assert.Equal("9", Get<TextBox>(window, "_upper").Text);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    });

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "HistoLens.UiTests", Guid.NewGuid().ToString("N"));
    private static HorizonOutcome Outcome(object row) => (HorizonOutcome)row.GetType().GetProperty("Outcome")!.GetValue(row)!;
    private static DataGridTextColumn Column(DataGrid grid, string property) => grid.Columns.Cast<DataGridTextColumn>().Single(column => ((Binding)column.Binding).Path.Path == property);
    private static void Sort(DataGrid grid, string property)
    {
        var column = Column(grid, property);
        Assert.True(column.CanUserSort);
        grid.Items.SortDescriptions.Clear(); grid.Items.SortDescriptions.Add(new SortDescription(column.SortMemberPath, ListSortDirection.Ascending));
        grid.Items.Refresh();
    }
    private static (DataSnapshot Snapshot, ResearchRun Run) SortingFixture()
    {
        var snapshot = DemoData.Create();
        decimal[] prices = [100, 100, 100, 110, 100, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 100, 90, 100, 100];
        snapshot = snapshot with
        {
            ContentHash = "", CorporateActions = [],
            Bars = snapshot.Bars.Select((bar, index) => bar with { Open = prices[index % prices.Length], High = prices[index % prices.Length], Low = prices[index % prices.Length], Close = prices[index % prices.Length], Volume = 1000 }).ToArray()
        };
        var hash = SnapshotFingerprint.Compute(snapshot); snapshot = snapshot with { ContentHash = hash, SnapshotId = hash };
        var definition = ResearchTemplates.CreateDefinition("HL-R001", snapshot.Calendar.TradingDates[1], snapshot.DataAsOf, snapshot.DataAsOf, 1, 1) with
        { Horizons = [1, 10], SamplingPolicy = SamplingPolicy.EveryMatch, UpperThreshold = 0.10m };
        return (snapshot, new ResearchEngine().Run(snapshot, definition));
    }
    private static async Task<string> WriteResearchFile(string directory, DataSnapshot snapshot, ResearchRun run)
    {
        Directory.CreateDirectory(directory);
        var payload = JsonSerializer.Serialize(new SavedResearch { SavedAtUtc = DateTimeOffset.UtcNow, Snapshot = snapshot, Run = run });
        var envelope = JsonSerializer.Serialize(new { FormatVersion = 1, Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))), Payload = payload });
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".histolens.json");
        await File.WriteAllTextAsync(path, envelope); return path;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Render(Visual frame, string folder, string file, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, file)); encoder.Save(stream);
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
