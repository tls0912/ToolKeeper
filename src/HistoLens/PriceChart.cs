using System.Globalization;
using System.Windows;
using System.Windows.Media;
using HistoLens.Core;

namespace HistoLens;

/// <summary>A small case-inspection chart; raw bars remain available in the adjacent table.</summary>
internal sealed class PriceChart : FrameworkElement
{
    private IReadOnlyList<DailyBar> _bars = [];
    private DateOnly? _event;
    public void SetData(IReadOnlyList<DailyBar> bars, DateOnly? eventDate)
    { _bars = bars.OrderBy(bar => bar.Date).ToArray(); _event = eventDate; InvalidateVisual(); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var text = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var accent = TryFindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
        var line = TryFindResource("LineBrush") as Brush ?? Brushes.LightGray;
        if (_bars.Count < 2 || ActualWidth < 90 || ActualHeight < 50) return;
        var values = _bars.Where(b => b.Close.HasValue).Select(b => b.Close!.Value).ToArray();
        if (values.Length < 2) return;
        var min = values.Min(); var max = values.Max(); var range = max == min ? 1 : max - min;
        var left = 60d; var top = 12d; var width = ActualWidth - left - 14; var height = ActualHeight - top - 28;
        var first = _bars[0].Date.DayNumber; var days = Math.Max(1, _bars[^1].Date.DayNumber - first);
        Point Position(DailyBar bar) => new(left + width * (bar.Date.DayNumber - first) / days, top + height * (double)((max - bar.Close!.Value) / range));
        void Label(string value, Point at) => drawing.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, text, VisualTreeHelper.GetDpi(this).PixelsPerDip), at);
        drawing.DrawLine(new Pen(line, 1), new(left, top + height), new(left + width, top + height));
        Label(max.ToString("0.00", CultureInfo.InvariantCulture), new(1, top));
        Label(min.ToString("0.00", CultureInfo.InvariantCulture), new(1, top + height - 12));
        Label(_bars[0].Date.ToString("yyyy-MM-dd"), new(left, top + height + 5));
        Label(_bars[^1].Date.ToString("yyyy-MM-dd"), new(Math.Max(left, ActualWidth - 75), top + height + 5));
        Point? previous = null;
        foreach (var bar in _bars)
        {
            if (bar.Close is null || bar.Status != TradingStatus.Traded) { previous = null; continue; }
            var point = Position(bar);
            if (previous is { } p) drawing.DrawLine(new Pen(accent, 1.6), p, point);
            previous = point;
            if (bar.Date == _event) drawing.DrawLine(new Pen(text, 1) { DashStyle = DashStyles.Dash }, new(point.X, top), new(point.X, top + height));
        }
    }
}
