using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HistoLens.Core;
using ToolKeeper.UI;
using Xunit;

namespace HistoLens.Tests;

[Collection("HistoLens WPF UI")]
public sealed class SimilarityWindowTests
{
    [Fact]
    public Task DefaultWorkflowFiltersScoresAndInvalidatesOnlyItsOwnSettings() => OnSta(async () =>
    {
        var window = NewWindow();
        try
        {
            Assert.False(Get<Expander>(window, "_advancedResearch").IsExpanded);
            window.LoadDemo();
            var chart = Get<SimilarityComparisonChart>(window, "_comparisonChart");
            Assert.Equal(window.Snapshot!.Calendar.TradingDates.Count, chart.HistoryPoints.Count);
            Assert.Empty(chart.Points);
            Assert.Equal(0, chart.ViewStartIndex);
            Assert.Equal(chart.HistoryPoints.Count - 1, chart.ViewEndIndex);
            Assert.Equal("60", Get<TextBox>(window, "_minimumSimilarity").Text);
            await window.RunSimilarityAsync();
            var run = Assert.IsType<SimilarityRun>(window.SimilarityResult);
            Assert.True(run.IsAllowed); Assert.NotEmpty(run.Matches);
            Assert.Equal(0, Get<TabControl>(window, "_resultTabs").SelectedIndex);
            Assert.False(Get<Expander>(window, "_advancedResearch").IsExpanded);
            Assert.Equal(20, run.Definition.Lookback);
            Assert.Equal(60m, run.Definition.MinimumSimilarity);
            Assert.Equal(new SimilarityDefinition().MinimumSimilarity, run.Definition.MinimumSimilarity);
            Assert.All(run.Matches, match => Assert.True(match.Scores.Total >= 60m && match.Features.End < run.Reference!.Start));
            Assert.Equal(5, Get<DataGrid>(window, "_similarityIndicators").Items.Count);
            Assert.Equal(run.Definition.Lookback, chart.Points.Count);
            Assert.Equal(run.Matches[0].Features.Start, chart.Points[0].HistoricalDate);
            Assert.Equal(run.Reference!.Start, chart.Points[0].RecentDate);
            Assert.Equal(100d, chart.Points[0].HistoricalIndex);
            Assert.Equal(100d, chart.Points[0].RecentIndex);
            Assert.Equal(window.Snapshot.Calendar.TradingDates.Max(), chart.HistoryPoints[^1].Date);
            Assert.True(chart.HistoryPoints[^1].Date > run.Definition.ScanEnd);
            Assert.True(chart.HistoryPoints[chart.ViewEndIndex].Date > run.Matches[0].Features.End);
            var grid = Get<DataGrid>(window, "_similarities");
            grid.SelectedIndex = 1;
            Assert.Equal(run.Matches[1].Features.Start, chart.Points[0].HistoricalDate);
            Assert.Equal(run.Matches[1].Features.End, chart.Points[^1].HistoricalDate);
            Assert.Equal(run.Reference.End, chart.Points[^1].RecentDate);
            Assert.True(chart.HistoryPoints[chart.ViewStartIndex].Date <= run.Matches[1].Features.Start);
            Assert.True(chart.HistoryPoints[chart.ViewEndIndex].Date > run.Matches[1].Features.End);
            chart.SetViewStart(0);
            var view = (chart.ViewStartIndex, chart.ViewEndIndex);
            window.SelectedLanguage = "en"; window.SelectedTheme = "Dark";
            Assert.Equal(view, (chart.ViewStartIndex, chart.ViewEndIndex));
            Assert.Equal(run.Matches[1].Features.Start, chart.Points[0].HistoricalDate);
            window.SelectedLanguage = "zh-TW";
            grid.Items.SortDescriptions.Add(new SortDescription("Match.Scores.Total", ListSortDirection.Ascending));
            var values = grid.Items.Cast<object>().Select(row => (SimilarityMatch)row.GetType().GetProperty("Match")!.GetValue(row)!).Select(match => match.Scores.Total).ToArray();
            Assert.Equal(values.Order(), values);
            Get<TextBox>(window, "_minimumSimilarity").Text = "100";
            Assert.True(window.IsSimilarityResultStale); Assert.Same(run, window.SimilarityResult);
            await window.RunSimilarityAsync();
            Assert.Empty(window.SimilarityResult!.Matches); Assert.False(window.IsSimilarityResultStale);
            Assert.Empty(chart.Points);
            Assert.Equal(window.Snapshot.Calendar.TradingDates.Count, chart.HistoryPoints.Count);
            Assert.Equal(0, chart.ViewStartIndex);
            Assert.Equal(chart.HistoryPoints.Count - 1, chart.ViewEndIndex);
            Assert.Contains("沒有區間", Get<TextBlock>(window, "_status").Text);
            var emptyRun = window.SimilarityResult;
            Get<TextBox>(window, "_minimumSimilarity").Text = "101";
            await window.RunSimilarityAsync();
            Assert.Same(emptyRun, window.SimilarityResult);
            Assert.Contains("設定有誤", Get<TextBlock>(window, "_status").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CancelAndEditDoNotCommitObsoleteScanAndReloadClearsIt() => OnSta(async () =>
    {
        var window = NewWindow();
        try
        {
            window.LoadDemo(); Get<TextBox>(window, "_minimumSimilarity").Text = "0";
            await window.RunSimilarityAsync(); var completed = window.SimilarityResult;
            var pending = window.RunSimilarityAsync();
            Get<TextBox>(window, "_recentDays").Text = "30";
            await pending;
            Assert.Same(completed, window.SimilarityResult); Assert.True(window.IsSimilarityResultStale);
            pending = window.RunSimilarityAsync();
            Get<Button>(window, "_cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert.Same(completed, window.SimilarityResult);
            Assert.True(Get<Button>(window, "_scanButton").IsEnabled);
            Assert.False(Get<Button>(window, "_cancel").IsEnabled);
            window.SelectedLanguage = "ja"; window.SelectedTheme = "InkDark";
            Assert.Same(completed, window.SimilarityResult);
            window.LoadDemo();
            Assert.Null(window.SimilarityResult); Assert.Empty(Get<DataGrid>(window, "_similarities").Items);
            Assert.Empty(Get<SimilarityComparisonChart>(window, "_comparisonChart").Points);
            Assert.Equal(window.Snapshot!.Calendar.TradingDates.Count,
                Get<SimilarityComparisonChart>(window, "_comparisonChart").HistoryPoints.Count);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReferenceEventsAndUnknownCoverageCompleteTheScanAndAppearOnTheComparisonChart() => OnSta(async () =>
    {
        var window = NewWindow();
        try
        {
            window.LoadDemo();
            var snapshot = window.Snapshot!;
            var eventDate = snapshot.Calendar.TradingDates[^4];
            snapshot = snapshot with
            {
                ContentHash = "",
                ActionCoverage = snapshot.ActionCoverage with { IsVerified = false },
                CorporateActions = snapshot.CorporateActions.Append(new CorporateAction
                { EffectiveDate = eventDate, Kind = "CashDividend", SourceId = snapshot.SourceId }).ToArray()
            };
            snapshot = snapshot with { ContentHash = SnapshotFingerprint.Compute(snapshot) };
            typeof(MainWindow).GetProperty(nameof(MainWindow.Snapshot))!.SetValue(window, snapshot);
            typeof(MainWindow).GetMethod("ResetSimilarity", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            typeof(MainWindow).GetField("_dataDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window,
                new DataIssue[] { new() { Code = "CorporateActionCoverageUnknown", BlocksResearch = true, Message = "Incomplete fixture event coverage." } });
            Get<TextBox>(window, "_minimumSimilarity").Text = "0";
            await window.RunSimilarityAsync();
            Assert.True(window.SimilarityResult!.IsAllowed);
            Assert.NotEmpty(window.SimilarityResult.Matches);
            Assert.False(window.IsSimilarityResultStale);
            Assert.Contains("掃描完成", Get<TextBlock>(window, "_status").Text);
            Assert.DoesNotContain("無法掃描", Get<TextBlock>(window, "_status").Text);
            Assert.Contains("原始價格，未還原", Get<TextBlock>(window, "_similaritySummary").Text);
            Assert.Contains("公司行動覆蓋未知", Get<TextBlock>(window, "_similaritySummary").Text);
            Assert.Contains(eventDate.ToString("yyyy-MM-dd"), Get<TextBlock>(window, "_similarityDetail").Text);
            var chart = Get<SimilarityComparisonChart>(window, "_comparisonChart");
            Assert.Contains(chart.EventMarkers, marker => marker.IsRecent && marker.Date == eventDate && marker.Kind == "CashDividend");
            Assert.Equal(snapshot.Bars[^1].Close, window.SimilarityResult.Reference!.EndClose);
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(900, 660)); frame.Arrange(new Rect(0, 0, 900, 660)); frame.UpdateLayout();
            Assert.True(chart.ActualHeight > 110);
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap(900, 660, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, "histolens-event-annotated-scan.png")); encoder.Save(file);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ShowingDateComponentsDuringScanDoesNotCancelIt() => OnSta(async () =>
    {
        var window = NewWindow();
        try
        {
            window.LoadDemo();
            var pending = window.RunSimilarityAsync();
            Get<Button>(window, "_dataNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(900, 660)); frame.Arrange(new Rect(0, 0, 900, 660)); frame.UpdateLayout();
            await pending;
            Assert.NotNull(window.SimilarityResult);
            Assert.False(window.IsSimilarityResultStale);
            Assert.Contains("掃描完成", Get<TextBlock>(window, "_status").Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("zh-TW", "Ink", 900, 660)]
    [InlineData("en", "Dark", 1280, 880)]
    [InlineData("ja", "InkDark", 1280, 880)]
    public Task SimpleWorkflowHasReadableLayout(string language, string theme, int width, int height) => OnSta(async () =>
    {
        var window = NewWindow(); window.SelectedLanguage = language; window.SelectedTheme = theme;
        try
        {
            window.LoadDemo();
            await window.RunSimilarityAsync();
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(width, height)); frame.Arrange(new Rect(0, 0, width, height)); frame.UpdateLayout();
            Assert.True(Get<DataGrid>(window, "_similarities").ActualWidth > 300);
            Assert.True(Get<DataGrid>(window, "_similarities").ActualHeight > 75);
            Assert.True(Get<SimilarityComparisonChart>(window, "_comparisonChart").ActualHeight > 110);
            Assert.True(Get<Button>(window, "_scanButton").ActualHeight > 20);
            Assert.Equal(5, Get<DataGrid>(window, "_similarityIndicators").Items.Count);
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"histolens-similarity-{language}-{theme}-{width}.png")); encoder.Save(file);
                var chart = Get<SimilarityComparisonChart>(window, "_comparisonChart");
                chart.ShowAll(); frame.UpdateLayout();
                Assert.Equal(0, chart.ViewStartIndex);
                Assert.Equal(chart.HistoryPoints.Count - 1, chart.ViewEndIndex);
                Assert.NotEmpty(chart.Points);
                var overview = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); overview.Render(frame);
                var overviewEncoder = new PngBitmapEncoder(); overviewEncoder.Frames.Add(BitmapFrame.Create(overview));
                using var overviewFile = File.Create(Path.Combine(folder, $"histolens-history-all-{language}-{theme}-{width}.png")); overviewEncoder.Save(overviewFile);
            }
            Assert.Equal(Visibility.Collapsed, Get<Expander>(window, "_advancedResearch").Visibility);
            var tabs = Get<TabControl>(window, "_resultTabs");
            Assert.All(tabs.Items.Cast<TabItem>().Skip(4), tab => Assert.Equal(Visibility.Visible, tab.Visibility));
            var choices = Get<GroupBox>(window, "_indicatorSelection");
            Assert.True(choices.ActualHeight > 100);
            var results = Assert.IsType<ScrollViewer>(((TabItem)tabs.Items[0]).Content);
            results.ScrollToBottom(); frame.UpdateLayout();
            Assert.True(results.VerticalOffset > 0);
            var sections = Assert.IsType<StackPanel>(results.Content).Children.OfType<GroupBox>().ToArray();
            Assert.Equal(2, sections.Length);
            Assert.All(sections, section => Assert.True(section.ActualHeight > 20));
            Assert.True(Get<DataGrid>(window, "_similarityIndicators").ActualHeight > 100);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var details = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); details.Render(frame);
                var detailsEncoder = new PngBitmapEncoder(); detailsEncoder.Frames.Add(BitmapFrame.Create(details));
                using var detailsFile = File.Create(Path.Combine(folder, $"histolens-details-{language}-{theme}-{width}.png")); detailsEncoder.Save(detailsFile);
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("zh-TW")]
    [InlineData("en")]
    [InlineData("ja")]
    public Task ScanCalendarDatesKeepExactDaysAndRejectInvalidRanges(string language) => OnSta(async () =>
    {
        var window = NewWindow(); window.SelectedLanguage = language;
        try
        {
            window.LoadDemo();
            var start = Get<DatePicker>(window, "_scanStart"); var end = Get<DatePicker>(window, "_scanEnd");
            start.SelectedDate = new DateTime(2023, 2, 1); end.SelectedDate = new DateTime(2024, 2, 29);
            await window.RunSimilarityAsync();
            var run = window.SimilarityResult!; Assert.NotNull(run);
            Assert.Equal(new DateOnly(2023, 2, 1), run.Definition.ScanStart);
            Assert.Equal(new DateOnly(2024, 2, 29), run.Definition.ScanEnd);
            window.SelectedLanguage = language == "en" ? "ja" : "en";
            Assert.Equal(new DateTime(2024, 2, 29), end.SelectedDate);
            end.Text = "2024-02-30"; Assert.Null(end.SelectedDate);
            await window.RunSimilarityAsync(); Assert.Same(run, window.SimilarityResult);
            Assert.True(window.IsSimilarityResultStale);
            end.SelectedDate = new DateTime(2024, 2, 29); start.SelectedDate = null;
            await window.RunSimilarityAsync(); Assert.Same(run, window.SimilarityResult);
            start.SelectedDate = new DateTime(2024, 3, 1);
            await window.RunSimilarityAsync(); Assert.Same(run, window.SimilarityResult);
            start.SelectedDate = new DateTime(2023, 2, 1);
            await window.RunSimilarityAsync(); Assert.NotSame(run, window.SimilarityResult);
            Assert.False(window.IsSimilarityResultStale);
        }
        finally { window.Close(); }
    });

    private static MainWindow NewWindow() => new(Path.Combine(Path.GetTempPath(), "HistoLens-similarity-" + Guid.NewGuid().ToString("N")))
        { PreferencesPath = null, SelectedLanguage = "zh-TW" };
    private static T Get<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
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
