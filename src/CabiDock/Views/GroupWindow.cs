using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CabiDock.Models;
using Controls = System.Windows.Controls;
using Primitives = System.Windows.Controls.Primitives;

namespace CabiDock.Views;

/// <summary>The visual card can be hosted in the explicit preview without creating a desktop window.</summary>
public sealed class GroupWindow : Window
{
    private const string ItemDragFormat = "CabiDock.DesktopItem";
    private const double TileWidth = 138;
    private const double TileHeight = 160;
    private readonly CategoryDefinition _category;
    private readonly Controls.Border _card;
    private readonly Controls.Grid _body;
    private readonly Controls.TextBlock _count;
    private readonly Primitives.Thumb _resize;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _hoverTimer;
    private readonly ShellIconProvider _icons = new();
    private List<DesktopItem> _items = [];
    private List<CategoryDefinition> _categories = [];
    private Rect _bounds = new(0, 0, 1920, 1080);
    private double _expandedWidth;
    private double _expandedHeight;
    private int _operations;
    private bool _dragOver;
    private bool _headerMoved;
    private bool _pendingCollapse;
    private bool _itemDragActive;
    private bool _renderPending;
    private System.Windows.Point _itemDragStart;

    public string CategoryId => _category.Id;
    public bool IsExpanded { get; private set; }
    public FrameworkElement CardContent => _card;
    public event Action<DesktopItem>? ItemOpenRequested;
    public event Action<DesktopItem, string>? ManualAssignmentRequested;
    public event Action<GroupLayout>? LayoutChanged;
    public event EventHandler? Expanded;

    public GroupWindow(CategoryDefinition category, GroupLayout layout)
    {
        _category = category;
        Title = category.Name;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = false;
        Width = TileWidth;
        Height = TileHeight;
        Left = double.IsFinite(layout.X) ? layout.X : 20;
        Top = double.IsFinite(layout.Y) ? layout.Y : 20;
        _expandedWidth = double.IsFinite(layout.Width) ? Math.Max(260, layout.Width) : 360;
        _expandedHeight = double.IsFinite(layout.Height) ? Math.Max(210, layout.Height) : 320;
        ViewTheme.Apply(this);

        _card = new Controls.Border
        {
            Background = ViewTheme.Brush("#F7FAFA"), BorderBrush = ViewTheme.Brush("#BDD3D0"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            SnapsToDevicePixels = true, AllowDrop = true, ClipToBounds = true
        };

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!_card.IsMouseOver && _operations == 0 && !_dragOver) Collapse();
        };
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); if (_dragOver) Expand(); };

        Content = _card;
        var grid = new Controls.Grid();
        grid.RowDefinitions.Add(new() { Height = new GridLength(37) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(26) });
        _card.Child = grid;
        var header = new Primitives.Thumb { Cursor = Cursors.SizeAll, ToolTip = "拖曳分類名稱以移動群組；按一下展開" };
        var headerBorder = new FrameworkElementFactory(typeof(Controls.Border));
        headerBorder.SetValue(Controls.Border.BackgroundProperty, ViewTheme.Brush("#EAF2F1"));
        headerBorder.SetValue(Controls.Border.PaddingProperty, new Thickness(12, 0, 12, 0));
        var headerText = new FrameworkElementFactory(typeof(Controls.TextBlock));
        headerText.SetValue(Controls.TextBlock.TextProperty, category.Name);
        headerText.SetValue(Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        headerText.SetValue(Controls.TextBlock.FontWeightProperty, FontWeights.SemiBold);
        headerText.SetValue(Controls.TextBlock.ForegroundProperty, ViewTheme.Ink);
        headerText.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        headerBorder.AppendChild(headerText);
        header.Template = new Controls.ControlTemplate(typeof(Primitives.Thumb)) { VisualTree = headerBorder };
        header.DragStarted += (_, _) => { BeginOperation(); _headerMoved = false; };
        header.DragDelta += (_, e) =>
        {
            if (Math.Abs(e.HorizontalChange) + Math.Abs(e.VerticalChange) > 0.5) _headerMoved = true;
            Left += e.HorizontalChange;
            Top += e.VerticalChange;
            ClampPosition();
        };
        header.DragCompleted += (_, _) =>
        {
            if (!_headerMoved) Expand(); else PublishLayout();
            EndOperation();
        };
        grid.Children.Add(header);
        _body = new Controls.Grid { Margin = new Thickness(8, 7, 8, 0) };
        Controls.Grid.SetRow(_body, 1);
        grid.Children.Add(_body);
        var footer = new Controls.Grid { Margin = new Thickness(10, 0, 8, 4) };
        _count = ViewTheme.Text("0 個項目", 11, ViewTheme.Muted);
        footer.Children.Add(_count);
        _resize = new Primitives.Thumb
        {
            Width = 20, Height = 20, HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed, ToolTip = "拖曳以調整展開大小"
        };
        var resizeText = new FrameworkElementFactory(typeof(Controls.TextBlock));
        resizeText.SetValue(Controls.TextBlock.TextProperty, "◢");
        resizeText.SetValue(Controls.TextBlock.ForegroundProperty, ViewTheme.Muted);
        _resize.Template = new Controls.ControlTemplate(typeof(Primitives.Thumb)) { VisualTree = resizeText };
        _resize.DragStarted += (_, _) => BeginOperation();
        _resize.DragDelta += (_, e) =>
        {
            _expandedWidth = Math.Clamp(Width + e.HorizontalChange, Math.Min(260, _bounds.Width), _bounds.Width);
            _expandedHeight = Math.Clamp(Height + e.VerticalChange, Math.Min(210, _bounds.Height), _bounds.Height);
            Width = _expandedWidth;
            Height = _expandedHeight;
            ClampPosition();
        };
        _resize.DragCompleted += (_, _) => { PublishLayout(); EndOperation(); };
        footer.Children.Add(_resize);
        Controls.Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        _card.MouseEnter += (_, _) => { _collapseTimer.Stop(); _pendingCollapse = false; };
        _card.MouseLeave += (_, _) => ScheduleCollapse();
        _card.DragEnter += CardDragEnter;
        _card.DragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(ItemDragFormat)) return;
            _dragOver = true;
            e.Effects = System.Windows.DragDropEffects.Move;
            e.Handled = true;
        };
        _card.DragLeave += (_, e) =>
        {
            var point = e.GetPosition(_card);
            if (point.X >= 0 && point.Y >= 0 && point.X <= _card.ActualWidth && point.Y <= _card.ActualHeight) return;
            _dragOver = false;
            _hoverTimer.Stop();
            ScheduleCollapse();
        };
        _card.Drop += (_, e) =>
        {
            _dragOver = false;
            _hoverTimer.Stop();
            if (e.Data.GetData(ItemDragFormat) is DesktopItem item)
            {
                ManualAssignmentRequested?.Invoke(item, CategoryId);
                e.Effects = System.Windows.DragDropEffects.Move;
                e.Handled = true;
            }
            ScheduleCollapse();
        };
        Closed += (_, _) => { _collapseTimer.Stop(); _hoverTimer.Stop(); };
        RenderBody();
    }

    public void UpdateItems(IEnumerable<DesktopItem> items, IEnumerable<CategoryDefinition> categories)
    {
        _items = items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _categories = categories.ToList();
        _count.Text = $"{_items.Count} 個項目";
        RenderBody();
    }

    public void SetPrimaryBounds(Rect bounds)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
        var originalLeft = Left;
        var originalTop = Top;
        _bounds = bounds;
        _expandedWidth = Math.Min(_expandedWidth, bounds.Width);
        _expandedHeight = Math.Min(_expandedHeight, bounds.Height);
        if (IsExpanded) { Width = _expandedWidth; Height = _expandedHeight; }
        ClampPosition();
        if (Left != originalLeft || Top != originalTop) PublishLayout();
    }

    public void Expand()
    {
        _collapseTimer.Stop();
        if (IsExpanded) return;
        IsExpanded = true;
        Width = Math.Min(_expandedWidth, _bounds.Width);
        Height = Math.Min(_expandedHeight, _bounds.Height);
        _resize.Visibility = Visibility.Visible;
        ClampPosition();
        RenderBody();
        Expanded?.Invoke(this, EventArgs.Empty);
    }

    public void Collapse()
    {
        if (_operations > 0 || _dragOver) { _pendingCollapse = true; return; }
        _collapseTimer.Stop();
        _pendingCollapse = false;
        if (!IsExpanded) return;
        IsExpanded = false;
        Width = TileWidth;
        Height = TileHeight;
        _resize.Visibility = Visibility.Collapsed;
        ClampPosition();
        RenderBody();
    }

    private void RenderBody()
    {
        // Keep the originating item alive until its drag or context menu has closed.
        if (_operations > 0) { _renderPending = true; return; }
        _renderPending = false;
        _body.Children.Clear();
        if (!IsExpanded)
        {
            var previews = new Primitives.UniformGrid { Rows = 2, Columns = 2, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            foreach (var item in _items.Take(4))
            {
                var box = new Controls.Border { Padding = new Thickness(6), ToolTip = item.Name };
                box.Child = BuildIcon(item, 30);
                previews.Children.Add(box);
            }
            previews.MouseLeftButtonUp += (_, e) => { Expand(); e.Handled = true; };
            _body.Children.Add(previews);
            return;
        }
        var wrap = new Controls.WrapPanel();
        foreach (var item in _items) wrap.Children.Add(CreateItem(item));
        _body.Children.Add(new Controls.ScrollViewer
        {
            Content = wrap, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled,
            CanContentScroll = false, PanningMode = Controls.PanningMode.VerticalOnly
        });
    }

    private FrameworkElement CreateItem(DesktopItem item)
    {
        var tile = new Controls.Border
        {
            Width = 100, Height = 102, Margin = new Thickness(2), Padding = new Thickness(4),
            Background = Brushes.Transparent, CornerRadius = new CornerRadius(6),
            ToolTip = $"{item.Name}\n{item.FullPath}", Cursor = Cursors.Arrow, Focusable = true
        };
        var stack = new Controls.StackPanel();
        stack.Children.Add(BuildIcon(item, 36));
        stack.Children.Add(new Controls.TextBlock
        {
            Text = item.Name, FontSize = 12, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 38, Margin = new Thickness(0, 6, 0, 0)
        });
        tile.Child = stack;
        tile.MouseEnter += (_, _) => tile.Background = ViewTheme.Brush("#E5EFED");
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonDown += (_, e) =>
        {
            _itemDragStart = e.GetPosition(tile);
            tile.Focus();
            if (e.ClickCount == 2) { ItemOpenRequested?.Invoke(item); e.Handled = true; }
        };
        tile.MouseMove += (_, e) =>
        {
            if (_itemDragActive || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(tile) - _itemDragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _itemDragActive = true;
            BeginOperation();
            try { System.Windows.DragDrop.DoDragDrop(tile, new System.Windows.DataObject(ItemDragFormat, item), System.Windows.DragDropEffects.Move); }
            finally { _itemDragActive = false; EndOperation(); }
        };
        tile.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ItemOpenRequested?.Invoke(item); e.Handled = true; } };
        var menu = new Controls.ContextMenu();
        var open = new Controls.MenuItem { Header = "開啟" };
        open.Click += (_, _) => ItemOpenRequested?.Invoke(item);
        menu.Items.Add(open);
        var move = new Controls.MenuItem { Header = "手動指定分類" };
        foreach (var category in _categories)
        {
            var choice = new Controls.MenuItem { Header = category.Name, IsChecked = category.Id == CategoryId };
            choice.Click += (_, _) => ManualAssignmentRequested?.Invoke(item, category.Id);
            move.Items.Add(choice);
        }
        menu.Items.Add(move);
        menu.Opened += (_, _) => BeginOperation();
        menu.Closed += (_, _) => EndOperation();
        tile.ContextMenu = menu;
        return tile;
    }

    private FrameworkElement BuildIcon(DesktopItem item, double size)
    {
        var source = _icons.Get(item);
        if (source is not null) return new Controls.Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform };
        return new Controls.TextBlock
        {
            Text = item.IsDirectory ? "▣" : "▤", Foreground = ViewTheme.Accent,
            FontSize = size, TextAlignment = TextAlignment.Center, Height = size + 4
        };
    }

    private void CardDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ItemDragFormat)) return;
        _dragOver = true;
        _collapseTimer.Stop();
        if (!IsExpanded) _hoverTimer.Start();
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
    }

    private void BeginOperation() { _operations++; _collapseTimer.Stop(); }
    private void EndOperation()
    {
        _operations = Math.Max(0, _operations - 1);
        if (_operations == 0 && _renderPending) RenderBody();
        if (_operations == 0 && _pendingCollapse && !_card.IsMouseOver && !_dragOver) Collapse();
        else ScheduleCollapse();
    }
    private void ScheduleCollapse()
    {
        if (IsExpanded && _operations == 0 && !_dragOver) { _collapseTimer.Stop(); _collapseTimer.Start(); }
    }
    private void ClampPosition()
    {
        Left = Math.Clamp(Left, _bounds.Left, Math.Max(_bounds.Left, _bounds.Right - Width));
        Top = Math.Clamp(Top, _bounds.Top, Math.Max(_bounds.Top, _bounds.Bottom - Height));
    }
    private void PublishLayout() => LayoutChanged?.Invoke(new GroupLayout
    {
        X = Left, Y = Top, Width = _expandedWidth, Height = _expandedHeight
    });
}
