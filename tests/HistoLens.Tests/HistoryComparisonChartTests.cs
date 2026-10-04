using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HistoLens.Core;
using Xunit;

namespace HistoLens.Tests;

[Collection("HistoLens WPF UI")]
public sealed class HistoryComparisonChartTests
{
    [Fact]
    public Task SelectionPreservesFullTimelineAndOnlyOverlaysMatchedDays() => OnSta(() =>
    {
        var snapshot = Snapshot(220);
        var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        Assert.Equal(snapshot.Calendar.TradingDates, chart.HistoryPoints.Select(point => point.Date));
        Assert.Empty(chart.Points); Assert.Equal(0, chart.ViewStartIndex); Assert.Equal(219, chart.ViewEndIndex);
        chart.SetComparison(snapshot.Bars[5].Date, snapshot.Bars[24].Date, snapshot.Bars.Skip(200).ToArray());
        Assert.Equal(220, chart.HistoryPoints.Count); Assert.Equal(snapshot.Bars[^1].Date, chart.HistoryPoints[^1].Date);
        Assert.Equal(20, chart.Points.Count);
        Assert.Equal(snapshot.Bars[24].Date, chart.Points[^1].HistoricalDate);
        Assert.Equal(snapshot.Bars[219].Date, chart.Points[^1].RecentDate);
        Assert.DoesNotContain(chart.Points, point => point.HistoricalDate > snapshot.Bars[24].Date);
        Assert.Contains(chart.HistoryPoints, point => point.Date > snapshot.Bars[24].Date);
        Assert.Equal(0, chart.ViewStartIndex); Assert.Equal(84, chart.ViewEndIndex);
    });

    [Fact]
    public Task DifferentPriceLevelsShareMatchStartScaleAndRetainRawCloses() => OnSta(() =>
    {
        var snapshot = Snapshot(80);
        snapshot = snapshot with { Bars = snapshot.Bars.Select((bar, index) => index switch
        { 0 => Bar(bar.Date, 100), 1 => Bar(bar.Date, 110), 2 => Bar(bar.Date, 90), 3 => Bar(bar.Date, 140), _ => bar }).ToArray() };
        var recent = new[] { Bar(new(2025, 1, 13), 200), Bar(new(2025, 1, 14), 240), Bar(new(2025, 1, 17), 220) };
        var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        chart.SetComparison(snapshot.Bars[0].Date, snapshot.Bars[2].Date, recent);
        Assert.Equal(new[] { 100d, 110d, 90d }, chart.Points.Select(point => point.HistoricalIndex));
        Assert.Equal(new[] { 100d, 120d, 110d }, chart.Points.Select(point => point.RecentIndex));
        Assert.Equal(240m, chart.Points[1].RecentClose); Assert.Equal(110m, chart.Points[1].HistoricalClose);
        Assert.Equal(new DateOnly(2025, 1, 14), chart.Points[1].RecentDate);
        Assert.Equal(140d, chart.HistoryPoints[3].Index); Assert.Equal(140m, snapshot.Bars[3].Close);
        Assert.True(SimilarityComparisonChart.RecentStrokeThickness > SimilarityComparisonChart.HistoricalStrokeThickness);
        Assert.NotEqual(((SolidColorBrush)chart.RecentBrush).Color, ((SolidColorBrush)chart.HistoricalBrush).Color);
    });

    [Fact]
    public Task CalendarGapsInvalidPricesAndNonTradingDaysRemainTimelineGaps() => OnSta(() =>
    {
        var snapshot = Snapshot(12);
        var bars = snapshot.Bars.Where((_, index) => index != 1).Select(bar =>
            bar.Date == snapshot.Bars[3].Date ? bar with { High = bar.Close - 1 } :
            bar.Date == snapshot.Bars[5].Date ? bar with { Status = TradingStatus.Suspended } :
            bar.Date == snapshot.Bars[7].Date ? bar with { Close = 0 } :
            bar.Date == snapshot.Bars[9].Date ? bar with { Open = null } : bar).ToList();
        bars.Add(snapshot.Bars[11]); // An unresolved duplicate is a gap rather than an arbitrary chosen quote.
        bars.Add(Bar(snapshot.DataAsOf.AddDays(1), 999));
        var futureMarketDate = snapshot.DataAsOf.AddDays(3);
        snapshot = snapshot with
        {
            Bars = bars,
            Calendar = snapshot.Calendar with { TradingDates = snapshot.Calendar.TradingDates.Append(futureMarketDate).ToArray() }
        };
        var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        Assert.Equal(12, chart.HistoryPoints.Count);
        foreach (var index in new[] { 1, 3, 5, 7, 9, 11 }) Assert.Null(chart.HistoryPoints[index].Index);
        Assert.Equal(snapshot.Calendar.TradingDates.Take(12), chart.HistoryPoints.Select(point => point.Date));
        Assert.NotNull(chart.HistoryPoints[0].Index); Assert.NotNull(chart.HistoryPoints[2].Index);
        Assert.DoesNotContain(chart.HistoryPoints, point => point.Date > snapshot.DataAsOf);
        Assert.Throws<ArgumentException>(() => chart.SetComparison(snapshot.Calendar.TradingDates[0], snapshot.Calendar.TradingDates[2], Snapshot(3).Bars));
    });

    [Fact]
    public Task KnownEventsMarkExactHistoricalAndRecentDatesWithoutChangingPrices() => OnSta(() =>
    {
        var snapshot = Snapshot(80);
        var historicalDate = snapshot.Bars[3].Date; var recentDate = snapshot.Bars[76].Date;
        snapshot = snapshot with { CorporateActions = [
            new() { EffectiveDate = historicalDate, Kind = "CashDividend", SourceId = "Official/fixture" },
            new() { EffectiveDate = recentDate, Kind = "NonComparablePrice", SourceId = "TWSE/STOCK_DAY" },
            new() { EffectiveDate = snapshot.DataAsOf.AddDays(1), Kind = "FutureEvent", SourceId = "Official/fixture" }
        ] };
        var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        Assert.Equal(2, chart.EventMarkers.Count);
        chart.SetComparison(snapshot.Bars[0].Date, snapshot.Bars[4].Date, snapshot.Bars.Skip(75).ToArray());
        Assert.Contains(chart.EventMarkers, marker => marker.Date == historicalDate && marker.MarketIndex == 3 && !marker.IsRecent);
        Assert.Contains(chart.EventMarkers, marker => marker.Date == recentDate && marker.MarketIndex == 76 && !marker.IsRecent);
        Assert.Contains(chart.EventMarkers, marker => marker.Date == recentDate && marker.MarketIndex == 1 && marker.IsRecent);
        Assert.DoesNotContain(chart.EventMarkers, marker => marker.Kind == "FutureEvent");
        Assert.Equal(snapshot.Bars[1].Close, chart.Points[1].HistoricalClose);
        Assert.Equal(snapshot.Bars[76].Close, chart.Points[1].RecentClose);
        Render(chart); chart.Resources["SurfaceBrush"] = Brushes.Black; chart.SetLanguage("zh-TW"); Render(chart);
        chart.ClearComparison(); Assert.Equal(2, chart.EventMarkers.Count); Assert.All(chart.EventMarkers, marker => Assert.False(marker.IsRecent));
        chart.SetHistory(null); Assert.Empty(chart.EventMarkers);
    });

    [Fact]
    public Task NonMarketDateEventsKeepTheirDateBetweenMarketPointsAndAreNotInventedFromPrices() => OnSta(() =>
    {
        var snapshot = Snapshot(20);
        var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        Assert.Empty(chart.EventMarkers);
        var eventDate = snapshot.Bars[4].Date.AddDays(1); // Saturday between Friday and Monday.
        snapshot = snapshot with { CorporateActions = [new() { EffectiveDate = eventDate, Kind = "OfficialEvent", SourceId = "Official/fixture" }] };
        chart.SetHistory(snapshot);
        var marker = Assert.Single(chart.EventMarkers);
        Assert.Equal(eventDate, marker.Date);
        Assert.InRange(marker.MarketIndex, 4.01, 4.99);
        Assert.Equal(20, chart.HistoryPoints.Count); // A market holiday never receives a fabricated price point.
        Render(chart);
    });

    [Theory]
    [InlineData(80, 30, 65, 199)]
    [InlineData(0, 20, 0, 79)]
    [InlineData(180, 20, 170, 199)]
    public Task LocateIncludesFollowingHistoryAndClampsAtAvailableEdges(int start, int length, int expectedStart, int expectedEnd) => OnSta(() =>
    {
        var snapshot = Snapshot(200); var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        chart.SetComparison(snapshot.Bars[start].Date, snapshot.Bars[start + length - 1].Date, snapshot.Bars.Take(length).ToArray());
        Assert.Equal(expectedStart, chart.ViewStartIndex); Assert.Equal(expectedEnd, chart.ViewEndIndex);
        chart.ShowAll(); chart.FocusSelection();
        Assert.Equal(expectedStart, chart.ViewStartIndex); Assert.Equal(expectedEnd, chart.ViewEndIndex);
    });

    [Fact]
    public Task ZoomScrollAndAllHistoryReachBothEndsWithoutChangingTheOverlay() => OnSta(() =>
    {
        var snapshot = Snapshot(300); var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        chart.SetComparison(snapshot.Bars[100].Date, snapshot.Bars[119].Date, snapshot.Bars.Take(20).ToArray());
        var points = chart.Points;
        chart.Zoom(2); var width = chart.ViewEndIndex - chart.ViewStartIndex + 1;
        chart.SetViewStart(int.MaxValue); Assert.Equal(299, chart.ViewEndIndex);
        Assert.Equal(width, chart.ViewEndIndex - chart.ViewStartIndex + 1);
        chart.SetViewStart(int.MinValue); Assert.Equal(0, chart.ViewStartIndex);
        var scroll = Assert.IsType<ScrollBar>(chart.Children[2]); scroll.Value = scroll.Maximum;
        Assert.Equal(299, chart.ViewEndIndex);
        chart.Zoom(.01); Assert.Equal(0, chart.ViewStartIndex); Assert.Equal(299, chart.ViewEndIndex);
        chart.Zoom(1000); Assert.Equal(5, chart.ViewEndIndex - chart.ViewStartIndex + 1);
        chart.ShowAll(); Assert.Equal(0, chart.ViewStartIndex); Assert.Equal(299, chart.ViewEndIndex);
        Assert.Same(points, chart.Points);
        Assert.Throws<ArgumentOutOfRangeException>(() => chart.Zoom(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => chart.Zoom(double.NaN));
    });

    [Fact]
    public Task SameHistorySelectionAndLocalizationPreserveUserViewport() => OnSta(() =>
    {
        var snapshot = Snapshot(240); var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        var recent = snapshot.Bars.Take(20).ToArray();
        chart.SetComparison(snapshot.Bars[60].Date, snapshot.Bars[79].Date, recent);
        chart.Zoom(2); chart.SetViewStart(0); var last = chart.ViewEndIndex;
        chart.SetLanguage("zh-TW"); chart.SetHistory(snapshot);
        chart.SetComparison(snapshot.Bars[60].Date, snapshot.Bars[79].Date, recent.ToArray());
        chart.Resources["SurfaceBrush"] = Brushes.Black;
        chart.SetLanguage("ja");
        Assert.Equal(0, chart.ViewStartIndex); Assert.Equal(last, chart.ViewEndIndex); Assert.Equal(20, chart.Points.Count);
        chart.ClearComparison(); Assert.Empty(chart.Points); Assert.Equal(239, chart.ViewEndIndex);
        chart.Zoom(2); chart.SetViewStart(0); last = chart.ViewEndIndex;
        chart.ClearComparison(); Assert.Equal(0, chart.ViewStartIndex); Assert.Equal(last, chart.ViewEndIndex);
        chart.SetHistory(Snapshot(50)); Assert.Equal(0, chart.ViewStartIndex); Assert.Equal(49, chart.ViewEndIndex);
        chart.SetHistory(null); Assert.Empty(chart.HistoryPoints); Assert.Empty(chart.Points); Assert.Equal(-1, chart.ViewEndIndex);
    });

    [Fact]
    public Task InvalidComparisonPreservesPreviousSelectionAndItsView() => OnSta(() =>
    {
        var snapshot = Snapshot(160); var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        var recent = snapshot.Bars.Take(20).ToArray();
        chart.SetComparison(snapshot.Bars[40].Date, snapshot.Bars[59].Date, recent);
        chart.Zoom(2); var points = chart.Points; var start = chart.ViewStartIndex; var end = chart.ViewEndIndex;
        Assert.Throws<ArgumentException>(() => chart.SetComparison(snapshot.Bars[40].Date, snapshot.Bars[58].Date, recent));
        Assert.Throws<ArgumentException>(() => chart.SetComparison(snapshot.Bars[40].Date, snapshot.Bars[59].Date, recent.Select((bar, index) => index == 2 ? bar with { Close = 0 } : bar).ToArray()));
        Assert.Throws<ArgumentException>(() => chart.SetComparison(snapshot.Bars[40].Date, snapshot.Bars[59].Date, recent.Select((bar, index) => index == 2 ? recent[0] : bar).ToArray()));
        Assert.Same(points, chart.Points); Assert.Equal(start, chart.ViewStartIndex); Assert.Equal(end, chart.ViewEndIndex);
    });

    [Fact]
    public Task CompactChartRendersFullHistorySelectionAndAnAllGapView() => OnSta(() =>
    {
        var snapshot = Snapshot(180); var chart = new SimilarityComparisonChart(); chart.SetHistory(snapshot);
        Render(chart); chart.SetComparison(snapshot.Bars[30].Date, snapshot.Bars[49].Date, snapshot.Bars.Take(20).ToArray());
        Render(chart); chart.Resources["SurfaceBrush"] = Brushes.Black; chart.SetLanguage("zh-TW"); Render(chart);
        var surface = Assert.IsAssignableFrom<FrameworkElement>(chart.Children[1]); Assert.True(surface.ActualHeight >= 80);
        chart.SetHistory(snapshot with { Bars = [] }); Render(chart); Assert.All(chart.HistoryPoints, point => Assert.Null(point.Index));
        chart.SetHistory(null); Render(chart);
    });

    private static void Render(SimilarityComparisonChart chart)
    {
        chart.Measure(new Size(530, 120)); chart.Arrange(new Rect(0, 0, 530, 120)); chart.UpdateLayout();
        var bitmap = new RenderTargetBitmap(530, 120, 96, 96, PixelFormats.Pbgra32); bitmap.Render(chart);
    }

    private static DataSnapshot Snapshot(int count)
    {
        var dates = Enumerable.Range(0, count * 2).Select(day => new DateOnly(2024, 1, 1).AddDays(day))
            .Where(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).Take(count).ToArray();
        return new()
        {
            DataAsOf = dates[^1], Calendar = new() { CoverageStart = dates[0], CoverageEnd = dates[^1], TradingDates = dates },
            Bars = dates.Select((date, index) => Bar(date, 100 + index)).ToArray()
        };
    }

    private static DailyBar Bar(DateOnly date, decimal close) => new()
    { Date = date, Open = close, High = close, Low = close, Close = close };

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
