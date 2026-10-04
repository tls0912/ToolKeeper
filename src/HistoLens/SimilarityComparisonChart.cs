using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HistoLens.Core;

namespace HistoLens;

/// <summary>Browse the full market timeline and align a recent window with a selected historical window.</summary>
internal sealed class SimilarityComparisonChart : Grid
{
    internal const double HistoricalStrokeThickness = 1.6;
    internal const double RecentStrokeThickness = 3.4;
    private readonly ChartSurface _surface;
    private readonly ScrollBar _scroll = new() { Orientation = Orientation.Horizontal, Height = 12, MinHeight = 0 };
    private readonly Button _all = CommandButton(), _locate = CommandButton(), _zoomIn = CommandButton(), _zoomOut = CommandButton();
    private DataSnapshot? _snapshot;
    private IReadOnlyList<HistoryPoint> _history = [];
    private IReadOnlyList<ComparisonPoint> _points = [];
    private IReadOnlyList<EventMarker> _eventMarkers = [];
    private int _selectionStart = -1, _selectionEnd = -1;
    private int? _hoverIndex;
    private bool _updatingScroll;
    private string _language = "en";

    internal IReadOnlyList<ComparisonPoint> Points => _points;
    internal IReadOnlyList<HistoryPoint> HistoryPoints => _history;
    internal IReadOnlyList<EventMarker> EventMarkers => _eventMarkers;
    internal int ViewStartIndex { get; private set; }
    internal int ViewEndIndex { get; private set; } = -1;
    internal Brush HistoricalBrush => IsDark ? Brushes.MediumTurquoise : Brushes.Teal;
    internal Brush RecentBrush => IsDark ? Brushes.Orange : Brushes.Chocolate;
    private bool IsDark => TryFindResource("SurfaceBrush") is SolidColorBrush surface &&
        surface.Color.R * 0.299 + surface.Color.G * 0.587 + surface.Color.B * 0.114 < 128;
    private bool HasSelection => _points.Count > 0;
    private int ViewCount => Math.Max(0, ViewEndIndex - ViewStartIndex + 1);
    private string T(string en, string zh, string ja) => _language switch { "zh-TW" => zh, "ja" => ja, _ => en };

    internal SimilarityComparisonChart()
    {
        ClipToBounds = true;
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { _all, _locate, _zoomIn, _zoomOut }) commands.Children.Add(button);
        Children.Add(commands);
        _surface = new(this) { ClipToBounds = true };
        SetRow(_surface, 1); Children.Add(_surface);
        SetRow(_scroll, 2); Children.Add(_scroll);
        _scroll.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        _scroll.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _all.Click += (_, _) => ShowAll();
        _locate.Click += (_, _) => FocusSelection();
        _zoomIn.Click += (_, _) => Zoom(1.5);
        _zoomOut.Click += (_, _) => Zoom(1 / 1.5);
        _scroll.ValueChanged += (_, _) => { if (!_updatingScroll) SetViewStart((int)Math.Round(_scroll.Value)); };
        ToolTipService.SetInitialShowDelay(_surface, 0);
        ToolTipService.SetShowDuration(_surface, 60000);
        SetLanguage("en");
        UpdateNavigation();
    }

    private static Button CommandButton() => new()
    {
        Height = 22, MinHeight = 0, MinWidth = 28, Padding = new Thickness(6, 0, 6, 0),
        Margin = new Thickness(0, 0, 4, 1), FontSize = 11
    };

    internal void SetLanguage(string language)
    {
        _language = language;
        _all.Content = T("All history", "完整歷史", "全履歴");
        _locate.Content = T("Locate match", "定位相似區間", "類似期間へ");
        _zoomIn.Content = "+"; _zoomOut.Content = "−";
        _zoomIn.ToolTip = T("Zoom in", "放大", "拡大"); _zoomOut.ToolTip = T("Zoom out", "縮小", "縮小");
        foreach (var button in new[] { _all, _locate }) AutomationProperties.SetName(button, button.Content.ToString());
        AutomationProperties.SetName(_zoomIn, (string)_zoomIn.ToolTip); AutomationProperties.SetName(_zoomOut, (string)_zoomOut.ToolTip);
        AutomationProperties.SetName(this, T("Full historical prices with recent trend overlay", "完整歷史行情與近期走勢疊圖", "全履歴と直近の値動きの比較"));
        AutomationProperties.SetName(_scroll, T("Historical timeline", "歷史時間軸", "履歴の時間軸"));
        ClearHover(); _surface.InvalidateVisual();
    }

    internal void SetHistory(DataSnapshot? snapshot)
    {
        if (ReferenceEquals(snapshot, _snapshot)) { _surface.InvalidateVisual(); return; }
        _snapshot = snapshot; _points = []; _selectionStart = _selectionEnd = -1;
        if (snapshot is null) _history = [];
        else
        {
            var bars = snapshot.Bars.GroupBy(bar => bar.Date).ToDictionary(group => group.Key,
                group => group.Count() == 1 ? group.Single() : null);
            _history = snapshot.Calendar.TradingDates.Where(date => date <= snapshot.DataAsOf).Distinct().Order()
                .Select(date =>
                {
                    bars.TryGetValue(date, out var bar);
                    return new HistoryPoint(date, bar?.Close,
                        SnapshotValidator.GetBarReasons(bar, false).Count == 0 ? (double?)bar!.Close!.Value : null);
                }).ToArray();
            NormalizeHistory();
        }
        UpdateEventMarkers(); ShowAll();
    }

    internal void SetComparison(DateOnly historicalStart, DateOnly historicalEnd, IReadOnlyList<DailyBar> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);
        var start = FindDate(historicalStart); var end = FindDate(historicalEnd);
        if (start < 0 || end < start || end - start + 1 < 2 || end - start + 1 != recent.Count)
            throw new ArgumentException("The compared windows must cover the same completed market trading days.");
        var reference = recent.OrderBy(bar => bar.Date).ToArray();
        if (_history.Skip(start).Take(reference.Length).Any(point => point.Index is null) ||
            reference.Any(bar => SnapshotValidator.GetBarReasons(bar, false).Count > 0) ||
            reference.Select(bar => bar.Date).Distinct().Count() != reference.Length)
            throw new ArgumentException("Comparison requires unique dates and valid traded prices.");
        var historicalBase = (double)_history[start].Close!.Value;
        var recentBase = (double)reference[0].Close!.Value;
        var points = reference.Select((bar, index) => new ComparisonPoint(_history[start + index].Date, bar.Date,
            _history[start + index].Close!.Value, bar.Close!.Value,
            100d * (double)_history[start + index].Close!.Value / historicalBase,
            100d * (double)bar.Close.Value / recentBase)).ToArray();
        if (_selectionStart == start && _selectionEnd == end && _points.SequenceEqual(points))
        { _surface.InvalidateVisual(); return; }
        _selectionStart = start; _selectionEnd = end; _points = points;
        UpdateEventMarkers(); NormalizeHistory(); FocusSelection();
    }

    internal void ClearComparison()
    {
        if (!HasSelection) return;
        _points = []; _selectionStart = _selectionEnd = -1;
        UpdateEventMarkers(); NormalizeHistory(); ShowAll();
    }

    private void UpdateEventMarkers()
    {
        var markers = new List<EventMarker>();
        var historyDates = _history.Select(point => point.Date).ToArray();
        var recentDates = _points.Select(point => point.RecentDate).ToArray();
        foreach (var action in _snapshot?.CorporateActions ?? [])
        {
            if (EventIndex(action.EffectiveDate, historyDates) is { } historyIndex)
                markers.Add(new(action.EffectiveDate, historyIndex, false, action.Kind, action.SourceId));
            if (EventIndex(action.EffectiveDate, recentDates) is { } recentIndex)
                markers.Add(new(action.EffectiveDate, _selectionStart + recentIndex, true, action.Kind, action.SourceId));
        }
        _eventMarkers = markers.OrderBy(marker => marker.MarketIndex).ThenBy(marker => marker.IsRecent)
            .ThenBy(marker => marker.Kind, StringComparer.Ordinal).ThenBy(marker => marker.SourceId, StringComparer.Ordinal).ToArray();
    }

    // Preserve an event's actual date even when it falls between the displayed market dates.
    private static double? EventIndex(DateOnly date, IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0 || date < dates[0] || date > dates[^1]) return null;
        var lo = 0; var hi = dates.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (dates[mid] == date) return mid;
            if (dates[mid] < date) lo = mid + 1; else hi = mid - 1;
        }
        return hi + (double)(date.DayNumber - dates[hi].DayNumber) / (dates[lo].DayNumber - dates[hi].DayNumber);
    }

    private void NormalizeHistory()
    {
        var baseline = HasSelection ? _history[_selectionStart].Close : _history.FirstOrDefault(point => point.Index is not null)?.Close;
        if (baseline is not > 0) return;
        _history = _history.Select(point => point with
        { Index = point.Index is null ? null : 100d * (double)point.Close!.Value / (double)baseline.Value }).ToArray();
    }

    internal void ShowAll() => SetView(0, _history.Count - 1);

    internal void FocusSelection()
    {
        if (!HasSelection) return;
        var before = Math.Max(1, _points.Count / 2);
        var after = Math.Max(60, 3 * _points.Count);
        SetView(Math.Max(0, _selectionStart - before), Math.Min(_history.Count - 1, _selectionEnd + after));
    }

    /// <summary>A factor greater than one zooms in around the current viewport center.</summary>
    internal void Zoom(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        if (_history.Count < 2) return;
        var count = (int)Math.Clamp(Math.Round(ViewCount / factor), Math.Min(5, _history.Count), _history.Count);
        var center = (ViewStartIndex + ViewEndIndex) / 2d;
        var start = Math.Clamp((int)Math.Round(center - (count - 1) / 2d), 0, _history.Count - count);
        SetView(start, start + count - 1);
    }

    internal void SetViewStart(int start)
    {
        if (_history.Count == 0) return;
        start = Math.Clamp(start, 0, _history.Count - ViewCount);
        SetView(start, start + ViewCount - 1);
    }

    private void SetView(int start, int end)
    {
        ViewStartIndex = start; ViewEndIndex = end;
        ClearHover(); UpdateNavigation(); _surface.InvalidateVisual();
    }

    private void UpdateNavigation()
    {
        _all.IsEnabled = _history.Count > 0; _locate.IsEnabled = HasSelection;
        _zoomIn.IsEnabled = ViewCount > Math.Min(5, _history.Count); _zoomOut.IsEnabled = ViewCount < _history.Count;
        _updatingScroll = true;
        try
        {
            _scroll.Maximum = Math.Max(0, _history.Count - ViewCount);
            _scroll.ViewportSize = ViewCount; _scroll.SmallChange = 1; _scroll.LargeChange = Math.Max(1, ViewCount / 2);
            _scroll.Value = ViewStartIndex; _scroll.IsEnabled = ViewCount < _history.Count;
        }
        finally { _updatingScroll = false; }
    }

    private int FindDate(DateOnly date)
    {
        var lo = 0; var hi = _history.Count - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2; var comparison = _history[mid].Date.CompareTo(date);
            if (comparison == 0) return mid;
            if (comparison < 0) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    private void Render(DrawingContext drawing)
    {
        var foreground = TryFindResource("TextBrush") as Brush ?? Brushes.Black;
        var muted = TryFindResource("MutedBrush") as Brush ?? Brushes.Gray;
        var gridBrush = TryFindResource("LineBrush") as Brush ?? Brushes.LightGray;
        var background = TryFindResource("SurfaceBrush") as Brush ?? Brushes.White;
        drawing.DrawRectangle(background, null, new Rect(_surface.RenderSize));
        if (_surface.ActualWidth < 180 || _surface.ActualHeight < 60) return;
        if (_history.Count == 0)
        {
            Label(drawing, T("Load stock data to view its history.", "載入股票資料後可查看完整歷史。", "データを読み込むと全履歴を表示します。"), new(10, 10), muted, _surface.ActualWidth - 20);
            return;
        }
        var historyPen = new Pen(HistoricalBrush, HistoricalStrokeThickness);
        var recentPen = new Pen(RecentBrush, RecentStrokeThickness);
        var first = _history[0]; var last = _history[^1];
        drawing.DrawLine(historyPen, new(4, 8), new(24, 8));
        Label(drawing, T("History", "歷史", "履歴") + $" {first.Date:yyyy-MM-dd} → {last.Date:yyyy-MM-dd}", new(29, 0), foreground, _surface.ActualWidth - 33);
        if (HasSelection)
        {
            drawing.DrawLine(recentPen, new(4, 23), new(24, 23));
            Label(drawing, T("Recent · thick", "近期・粗線", "直近・太線") + $" {_points[0].RecentDate:yyyy-MM-dd} → {_points[^1].RecentDate:yyyy-MM-dd}" +
                T(" · matched starts = 100", "・比對起點＝100", "・比較開始＝100"), new(29, 15), foreground, _surface.ActualWidth - 33);
        }
        if (_eventMarkers.Count > 0)
            Label(drawing, T("▼ History event  ▲ Recent event · hover for date, type and source",
                "▼ 歷史事件　▲ 近期事件・移至標記查看日期、類型及來源",
                "▼ 過去のイベント　▲ 直近のイベント・日付、種類、出典はヒントに表示"),
                new(4, HasSelection ? 30 : 15), muted, _surface.ActualWidth - 8);
        var plot = PlotArea();
        if (plot.Height < 12 || plot.Width < 40) return;
        var values = _history.Skip(ViewStartIndex).Take(ViewCount).Where(point => point.Index is not null).Select(point => point.Index!.Value)
            .Concat(_points.Select((point, index) => (point, index: _selectionStart + index))
                .Where(item => item.index >= ViewStartIndex && item.index <= ViewEndIndex).Select(item => item.point.RecentIndex)).ToArray();
        if (values.Length == 0)
        {
            Label(drawing, T("Prices unavailable in this view", "此範圍缺少有效價格", "この表示範囲に有効な価格がありません"), new(plot.Left, plot.Top), muted, plot.Width);
            DrawEvents(drawing, plot);
            DrawDates(drawing, plot, muted); return;
        }
        var min = values.Min(); var max = values.Max(); var padding = Math.Max(.2, (max - min) * .1);
        min -= padding; max += padding;
        Point Position(int index, double value) => new(plot.Left + plot.Width * (index - ViewStartIndex) / Math.Max(1, ViewCount - 1),
            plot.Bottom - plot.Height * (value - min) / (max - min));
        for (var tick = 0; tick <= 2; tick++)
        {
            var value = min + (max - min) * tick / 2; var y = Position(ViewStartIndex, value).Y;
            drawing.DrawLine(new Pen(gridBrush, .6), new(plot.Left, y), new(plot.Right, y));
            Label(drawing, value.ToString("0.0", CultureInfo.InvariantCulture), new(1, y - 7), muted, plot.Left - 5);
        }
        drawing.PushClip(new RectangleGeometry(plot));
        if (HasSelection)
        {
            var from = Position(_selectionStart, min).X; var to = Position(_selectionEnd, min).X;
            var shade = RecentBrush.Clone(); shade.Opacity = .10;
            drawing.DrawRectangle(shade, null, new Rect(new Point(from, plot.Top), new Point(to, plot.Bottom)));
            drawing.DrawLine(new Pen(RecentBrush, .9) { DashStyle = DashStyles.Dash }, new(to, plot.Top), new(to, plot.Bottom));
        }
        if (min <= 100 && max >= 100)
            drawing.DrawLine(new Pen(muted, .7) { DashStyle = DashStyles.Dash }, Position(ViewStartIndex, 100), Position(ViewEndIndex, 100));
        for (var index = ViewStartIndex; index <= ViewEndIndex; index++)
        {
            if (_history[index].Index is not { } value) continue;
            if (index > ViewStartIndex && _history[index - 1].Index is { } previous)
                drawing.DrawLine(historyPen, Position(index - 1, previous), Position(index, value));
            if ((index == ViewStartIndex || _history[index - 1].Index is null) && (index == ViewEndIndex || _history[index + 1].Index is null))
                drawing.DrawEllipse(HistoricalBrush, null, Position(index, value), 1.5, 1.5);
        }
        // The recent series only occupies the selected market days and ends exactly at the match boundary.
        for (var index = 1; index < _points.Count; index++)
            if (_selectionStart + index >= ViewStartIndex && _selectionStart + index - 1 <= ViewEndIndex)
                drawing.DrawLine(recentPen, Position(_selectionStart + index - 1, _points[index - 1].RecentIndex), Position(_selectionStart + index, _points[index].RecentIndex));
        DrawEvents(drawing, plot);
        if (_hoverIndex is { } hovered)
        {
            var x = Position(hovered, min).X;
            drawing.DrawLine(new Pen(muted, .8) { DashStyle = DashStyles.Dot }, new(x, plot.Top), new(x, plot.Bottom));
            if (_history[hovered].Index is { } historical) drawing.DrawEllipse(background, historyPen, Position(hovered, historical), 3, 3);
            if (hovered >= _selectionStart && hovered <= _selectionEnd)
                drawing.DrawEllipse(RecentBrush, null, Position(hovered, _points[hovered - _selectionStart].RecentIndex), 3.5, 3.5);
        }
        drawing.Pop();
        if (HasSelection && _selectionEnd >= ViewStartIndex && _selectionEnd <= ViewEndIndex)
            Label(drawing, T("Match end", "比對末日", "比較終了"), new(Math.Clamp(Position(_selectionEnd, min).X + 3, plot.Left, Math.Max(plot.Left, plot.Right - 62)), plot.Top), RecentBrush, 62);
        DrawDates(drawing, plot, muted);
    }

    private void DrawEvents(DrawingContext drawing, Rect plot)
    {
        foreach (var marker in _eventMarkers.Where(marker => marker.MarketIndex >= ViewStartIndex && marker.MarketIndex <= ViewEndIndex))
        {
            var x = plot.Left + plot.Width * (marker.MarketIndex - ViewStartIndex) / Math.Max(1, ViewCount - 1);
            var brush = marker.IsRecent ? RecentBrush : HistoricalBrush;
            drawing.DrawLine(new Pen(brush, .8) { DashStyle = DashStyles.Dot }, new(x, plot.Top), new(x, plot.Bottom));
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var y = marker.IsRecent ? plot.Bottom : plot.Top;
                var direction = marker.IsRecent ? -1 : 1;
                context.BeginFigure(new(x - 4, y), true, true);
                context.LineTo(new(x + 4, y), true, false);
                context.LineTo(new(x, y + 6 * direction), true, false);
            }
            drawing.DrawGeometry(brush, null, geometry);
        }
    }

    private void DrawDates(DrawingContext drawing, Rect plot, Brush muted)
    {
        Label(drawing, _history[ViewStartIndex].Date.ToString("yyyy-MM-dd"), new(plot.Left, plot.Bottom + 1), muted, 78);
        Label(drawing, _history[ViewEndIndex].Date.ToString("yyyy-MM-dd"), new(Math.Max(plot.Left, plot.Right - 74), plot.Bottom + 1), muted, 75);
        if (!HasSelection)
            Label(drawing, T("First valid close = 100", "首筆有效收盤＝100", "最初の有効終値＝100"), new(plot.Left + 85, plot.Bottom + 1), muted, Math.Max(1, plot.Width - 165));
    }

    private Rect PlotArea()
    {
        var top = (HasSelection ? 34d : 20d) + (_eventMarkers.Count > 0 ? 15 : 0);
        return new(46, top, Math.Max(0, _surface.ActualWidth - 57), Math.Max(0, _surface.ActualHeight - top - 17));
    }

    private void Hover(Point position)
    {
        var plot = PlotArea();
        if (_history.Count == 0 || plot.Width <= 0 || !plot.Contains(position)) { ClearHover(); return; }
        var index = Math.Clamp(ViewStartIndex + (int)Math.Round((position.X - plot.Left) / plot.Width * Math.Max(0, ViewCount - 1)), ViewStartIndex, ViewEndIndex);
        if (_hoverIndex == index) return;
        _hoverIndex = index; var historical = _history[index];
        _surface.ToolTip = T("History", "歷史", "履歴") + $" {historical.Date:yyyy-MM-dd}  " +
            (historical.Index is { } value ? $"{historical.Close:0.####}  ({value:0.00})" : T("Missing / invalid / no trade", "缺值／異常／未成交", "欠損・異常・未取引"));
        if (index >= _selectionStart && index <= _selectionEnd)
        {
            var point = _points[index - _selectionStart];
            _surface.ToolTip += "\n" + T("Recent", "近期", "直近") + $" {point.RecentDate:yyyy-MM-dd}  {point.RecentClose:0.####}  ({point.RecentIndex:0.00})";
        }
        foreach (var marker in _eventMarkers.Where(marker => (int)Math.Round(marker.MarketIndex) == index))
            _surface.ToolTip += "\n" + (marker.IsRecent ? T("Recent event", "近期事件", "直近のイベント") : T("History event", "歷史事件", "過去のイベント")) +
                $" {marker.Date:yyyy-MM-dd} · {EventKind(marker)} · {marker.SourceId}";
        _surface.InvalidateVisual();
    }

    private string EventKind(EventMarker marker) => marker.Kind switch
    {
        "CashDividend" or "ExDividend" => T("Ex-dividend", "除息", "配当落ち"),
        "ExRights" or "ExRight" => T("Ex-rights", "除權", "権利落ち"),
        "ExRightsAndDividend" or "ExRightsOrDividend" or "ExRightAndDividend" or "ExRightOrDividend" => T("Ex-rights / dividend", "除權／除息", "権利・配当落ち"),
        "NonComparablePrice" => T("Raw-price comparison annotation", "原價可比性註記", "原価格の比較可能性注記"),
        _ when marker.Kind.StartsWith("CapitalReduction:", StringComparison.Ordinal) => T("Capital reduction", "減資", "減資") + " · " + marker.Kind,
        _ when marker.SourceId == "TWSE/STOCK_DAY" => T("Raw-price comparison annotation", "原價可比性註記", "原価格の比較可能性注記") + " · " + marker.Kind,
        _ => marker.Kind
    };

    private void ClearHover() { _hoverIndex = null; _surface.ToolTip = null; _surface.InvalidateVisual(); }
    private void Label(DrawingContext drawing, string content, Point at, Brush brush, double width)
    {
        var text = new FormattedText(content, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 10, brush, VisualTreeHelper.GetDpi(_surface).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        drawing.DrawText(text, at);
    }

    private sealed class ChartSurface(SimilarityComparisonChart owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing) { base.OnRender(drawing); owner.Render(drawing); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); owner.Hover(e.GetPosition(this)); }
        protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); owner.ClearHover(); }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (owner._history.Count < 2) return;
            owner.Zoom(e.Delta > 0 ? 1.5 : 1 / 1.5); e.Handled = true;
        }
    }

    internal sealed record HistoryPoint(DateOnly Date, decimal? Close, double? Index);
    internal sealed record ComparisonPoint(DateOnly HistoricalDate, DateOnly RecentDate, decimal HistoricalClose,
        decimal RecentClose, double HistoricalIndex, double RecentIndex);
    internal sealed record EventMarker(DateOnly Date, double MarketIndex, bool IsRecent, string Kind, string SourceId);
}
