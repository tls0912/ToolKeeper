using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace ToolKeeper.UI;

/// <summary>A fixed one-DIP shadow whose glyph outline can be widened without changing text layout.</summary>
[ContentProperty(nameof(Label))]
public sealed class ChromeTextShadow : Grid
{
    private static readonly Brush LightShadow = ShadowBrush(40);
    private static readonly Brush DarkShadow = ShadowBrush(96);
    private static readonly DependencyPropertyDescriptor[] LabelProperties =
    new DependencyProperty[]
    {
        TextBlock.TextProperty, TextBlock.FontFamilyProperty, TextBlock.FontSizeProperty,
        TextBlock.FontWeightProperty, TextBlock.FontStyleProperty, TextBlock.FontStretchProperty,
        TextBlock.TextAlignmentProperty, TextBlock.TextTrimmingProperty, TextBlock.TextWrappingProperty,
        TextBlock.TextDecorationsProperty, TextBlock.LineHeightProperty, TextBlock.LineStackingStrategyProperty,
        TextBlock.PaddingProperty, FlowDirectionProperty, LanguageProperty,
        HorizontalAlignmentProperty, VerticalAlignmentProperty, MarginProperty,
        WidthProperty, HeightProperty, MinWidthProperty, MinHeightProperty, MaxWidthProperty, MaxHeightProperty,
        VisibilityProperty, OpacityProperty
    }.Select(property => DependencyPropertyDescriptor.FromProperty(property, typeof(TextBlock))).ToArray();

    private TextBlock? _label;
    private OutlineShadowLayer? _shadowLayer;
    private Size _lastLabelSize;
    private Matrix _lastLabelTransform;
    private double _lastDpi;
    private bool _watchingLabel;
    private bool _refreshPending;

    public ChromeTextShadow()
    {
        Loaded += (_, _) => AttachLabelWatchers();
        Unloaded += (_, _) => DetachLabelWatchers();
    }
    public ChromeTextShadow(TextBlock label) : this() => Label = label;

    public TextBlock? Label
    {
        get => _label;
        set
        {
            DetachLabelWatchers();
            Children.Clear();
            _label = value;
            _shadowLayer = null;
            if (value is null) return;

            _lastLabelSize = default;
            _lastLabelTransform = default;
            _lastDpi = 0;
            _shadowLayer = new OutlineShadowLayer(value) { IsHitTestVisible = false, Focusable = false };
            _shadowLayer.SetResourceReference(OutlineShadowLayer.ShadowBrushProperty, "ChromeTextShadowBrush");
            _shadowLayer.SetResourceReference(OutlineShadowLayer.ThicknessProperty, "ChromeTextShadowThickness");
            Children.Add(value);
            Panel.SetZIndex(_shadowLayer, -1);
            Children.Add(_shadowLayer);
            if (IsLoaded) AttachLabelWatchers();
        }
    }

    public static void ApplyResources(ResourceDictionary resources, bool dark, bool enabled, int thickness)
    {
        resources["ChromeTextShadowBrush"] = dark ? DarkShadow : LightShadow;
        resources["ChromeTextShadowThickness"] = enabled ? Math.Clamp(thickness, 1, 4) : 0;
    }

    private void AttachLabelWatchers()
    {
        if (_watchingLabel || _label is null) return;
        foreach (var property in LabelProperties) property.AddValueChanged(_label, OnLabelChanged);
        _label.LayoutUpdated += OnLabelLayoutUpdated;
        _watchingLabel = true;
        ScheduleGlyphRefresh();
    }

    private void DetachLabelWatchers()
    {
        if (!_watchingLabel || _label is null) return;
        _label.LayoutUpdated -= OnLabelLayoutUpdated;
        foreach (var property in LabelProperties) property.RemoveValueChanged(_label, OnLabelChanged);
        _watchingLabel = false;
    }

    private void OnLabelChanged(object? sender, EventArgs e) => ScheduleGlyphRefresh();

    private void ScheduleGlyphRefresh()
    {
        if (_refreshPending) return;
        _refreshPending = true;
        // TextBlock's drawing is refreshed during render; capture it after that pass.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _refreshPending = false;
            if (IsLoaded) _shadowLayer?.InvalidateGlyphs();
        }));
    }

    private void OnLabelLayoutUpdated(object? sender, EventArgs e)
    {
        if (_label is null || _shadowLayer is null || !_label.IsVisible) return;
        if (!OutlineShadowLayer.TryTransform(_label, _shadowLayer, out var transform)) return;
        var dpi = VisualTreeHelper.GetDpi(_label).PixelsPerDip;
        if (_lastLabelSize == _label.RenderSize && _lastLabelTransform == transform && _lastDpi == dpi) return;
        _lastLabelSize = _label.RenderSize;
        _lastLabelTransform = transform;
        _lastDpi = dpi;
        ScheduleGlyphRefresh();
    }

    private static Brush ShadowBrush(byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private sealed class OutlineShadowLayer(TextBlock label) : FrameworkElement
    {
        private readonly Dictionary<GlyphRun, Geometry> _geometryCache = [];

        public static readonly DependencyProperty ShadowBrushProperty = DependencyProperty.Register(
            nameof(ShadowBrush), typeof(Brush), typeof(OutlineShadowLayer),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness), typeof(int), typeof(OutlineShadowLayer),
            new FrameworkPropertyMetadata(1, OnThicknessChanged));

        public Brush ShadowBrush => (Brush)GetValue(ShadowBrushProperty);
        public int Thickness => (int)GetValue(ThicknessProperty);

        public void InvalidateGlyphs()
        {
            _geometryCache.Clear();
            InvalidateVisual();
        }

        public static bool TryTransform(Visual source, Visual target, out Matrix matrix)
        {
            matrix = Matrix.Identity;
            try
            {
                var transform = source.TransformToVisual(target);
                var origin = transform.Transform(new Point(0, 0));
                var x = transform.Transform(new Point(1, 0));
                var y = transform.Transform(new Point(0, 1));
                matrix = new Matrix(x.X - origin.X, x.Y - origin.Y, y.X - origin.X, y.Y - origin.Y, origin.X, origin.Y);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Thickness <= 0 || label.Visibility != Visibility.Visible || ShadowBrush is null) return;
            if (!TryTransform(label, this, out var matrix)) return;
            // Keep the shadow at one DIP in screen space; widening is symmetric around that position.
            matrix.OffsetX += 1;
            matrix.OffsetY += 1;
            drawingContext.PushTransform(new MatrixTransform(matrix));
            if (label.Opacity < 1) drawingContext.PushOpacity(label.Opacity);
            DrawGlyphs(drawingContext, VisualTreeHelper.GetDrawing(label));
            if (label.Opacity < 1) drawingContext.Pop();
            drawingContext.Pop();
        }

        private void DrawGlyphs(DrawingContext context, Drawing drawing)
        {
            if (drawing is DrawingGroup group)
            {
                if (group.Transform is not null) context.PushTransform(group.Transform);
                if (group.ClipGeometry is not null) context.PushClip(group.ClipGeometry);
                if (group.Opacity < 1) context.PushOpacity(group.Opacity);
                foreach (var child in group.Children) DrawGlyphs(context, child);
                if (group.Opacity < 1) context.Pop();
                if (group.ClipGeometry is not null) context.Pop();
                if (group.Transform is not null) context.Pop();
                return;
            }
            if (drawing is not GlyphRunDrawing { GlyphRun: { } run }) return;
            if (!_geometryCache.TryGetValue(run, out var geometry))
            {
                geometry = run.BuildGeometry();
                if (!geometry.IsEmpty() && Thickness > 1)
                {
                    var radius = (Thickness - 1) * 0.35;
                    var pen = new Pen(Brushes.Black, radius * 2) { LineJoin = PenLineJoin.Round };
                    var stroke = geometry.GetWidenedPathGeometry(pen);
                    geometry = Geometry.Combine(geometry, stroke, GeometryCombineMode.Union, null);
                }
                if (geometry.CanFreeze) geometry.Freeze();
                _geometryCache[run] = geometry;
            }
            if (!geometry.IsEmpty()) context.DrawGeometry(ShadowBrush, null, geometry);
        }

        private static void OnThicknessChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var layer = (OutlineShadowLayer)target;
            layer.Visibility = (int)args.NewValue > 0 ? Visibility.Visible : Visibility.Collapsed;
            layer.InvalidateGlyphs();
        }

        protected override AutomationPeer? OnCreateAutomationPeer() => null;
    }
}

public sealed class ChromeButtonTextSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    // Compound button content already wraps its text leaves; never duplicate UIElements.
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is string ? TextTemplate : null;
}
