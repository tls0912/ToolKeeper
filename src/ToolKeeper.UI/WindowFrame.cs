using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace ToolKeeper.UI;

/// <summary>
/// Shared application frame. Products supply their workspace and optional caption controls;
/// document state, persistence, and full-screen monitor bounds remain with the owner.
/// </summary>
public sealed class WindowFrame : Grid
{
    private readonly Window _owner;
    private readonly Grid _root;
    private readonly RowDefinition _captionRow;
    private readonly Grid _brand;
    private readonly ColumnDefinition _brandColumn;
    private readonly ColumnDefinition _contentColumn;
    private readonly DependencyPropertyDescriptor _resizeModeProperty =
        DependencyPropertyDescriptor.FromProperty(Window.ResizeModeProperty, typeof(Window))!;
    private readonly Border _caption;
    private readonly Border _workspace = new();
    private readonly Border _captionContent = new();
    private readonly Border _captionActions = new();
    private readonly Border _topReveal;
    private readonly Button _minimize;
    private readonly Button _maximize;
    private readonly Button _close;
    private HwndSource? _source;
    private bool _isFullScreen;
    private bool _closed;
    private string _language = "en";

    public WindowFrame(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        Name = "SharedWindowFrame";
        UseLayoutRounding = true;
        UiAppearance.EnsureResources(owner.Resources);
        owner.WindowStyle = WindowStyle.None;

        _root = new Grid { Name = "SharedWindowRoot" };
        _captionRow = new RowDefinition { Height = new GridLength(CaptionHeight) };
        _root.RowDefinitions.Add(_captionRow);
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var outer = new Border { BorderThickness = new Thickness(1), Child = _root };
        outer.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        outer.SetResourceReference(Border.BackgroundProperty, "ChromeBackgroundBrush");
        Children.Add(outer);

        var captionGrid = new Grid();
        _brandColumn = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        _contentColumn = new ColumnDefinition { Width = GridLength.Auto };
        captionGrid.ColumnDefinitions.Add(_brandColumn);
        captionGrid.ColumnDefinitions.Add(_contentColumn);
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        captionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _brand = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 12, 0) };
        _brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0) };
        icon.SetBinding(Image.SourceProperty, new Binding(nameof(Window.Icon)) { Source = owner });
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        var iconStyle = new Style(typeof(Image));
        var hideMissingIcon = new DataTrigger { Binding = new Binding(nameof(Window.Icon)) { Source = owner }, Value = null };
        hideMissingIcon.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        iconStyle.Triggers.Add(hideMissingIcon);
        icon.Style = iconStyle;
        _brand.Children.Add(icon);
        var title = new TextBlock
        {
            Name = "WindowTitleText", FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = owner });
        title.SetBinding(ToolTipProperty, new Binding(nameof(Window.Title)) { Source = owner });
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var titleShadow = new ChromeTextShadow(title);
        SetColumn(titleShadow, 1);
        _brand.Children.Add(titleShadow);
        captionGrid.Children.Add(_brand);

        Grid.SetColumn(_captionContent, 1);
        captionGrid.Children.Add(_captionContent);
        Grid.SetColumn(_captionActions, 2);
        captionGrid.Children.Add(_captionActions);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        _minimize = CaptionButton("MinimizeButton", "\uE921", () => owner.WindowState = WindowState.Minimized);
        _maximize = CaptionButton("MaximizeButton", "\uE922", () =>
            owner.WindowState = owner.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        _close = CaptionButton("WindowCloseButton", "\uE8BB", owner.Close);
        controls.Children.Add(_minimize);
        controls.Children.Add(_maximize);
        controls.Children.Add(_close);
        Grid.SetColumn(controls, 3);
        captionGrid.Children.Add(controls);
        var inkStroke = new Border
        {
            Name = "InkTitleStroke", Height = 2, Width = 78,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(13, 0, 0, 0), IsHitTestVisible = false
        };
        inkStroke.SetResourceReference(Border.BackgroundProperty, "InkTitleStrokeBrush");
        inkStroke.SetResourceReference(VisibilityProperty, "InkTitleStrokeVisibility");
        captionGrid.Children.Add(inkStroke);
        _caption = new Border
        {
            Name = "TitleBar", BorderThickness = new Thickness(0, 0, 0, 1), Child = captionGrid
        };
        _caption.SizeChanged += (_, _) => UpdateBrandWidth();
        _captionActions.SizeChanged += (_, _) => UpdateBrandWidth();
        _caption.SetResourceReference(Border.BackgroundProperty, "ChromeTitleBrush");
        _caption.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        SetZIndex(_caption, 30);
        _root.Children.Add(_caption);
        SetRow(_workspace, 1);
        _root.Children.Add(_workspace);
        _topReveal = new Border
        {
            Name = "TopReveal", Height = 6, Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed
        };
        SetRowSpan(_topReveal, 2);
        SetZIndex(_topReveal, 50);
        _topReveal.MouseEnter += (_, _) => CaptionRevealRequested?.Invoke(this, EventArgs.Empty);
        _root.Children.Add(_topReveal);

        owner.SourceInitialized += OwnerSourceInitialized;
        owner.StateChanged += OwnerStateChanged;
        owner.Closed += OwnerClosed;
        _resizeModeProperty.AddValueChanged(owner, OwnerResizeModeChanged);
        if (new WindowInteropHelper(owner).Handle != 0) AttachWindowHook();
        UpdateResizeMode();
        ApplyChrome();
        ApplyLanguage(_language);
    }

    public UIElement? Workspace
    {
        get => _workspace.Child;
        set => _workspace.Child = value;
    }

    /// <summary>Optional middle caption content, such as document tabs.</summary>
    public UIElement? CaptionContent
    {
        get => _captionContent.Child;
        set
        {
            SetCaptionContent(_captionContent, value);
            _brandColumn.Width = value is null ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            _contentColumn.Width = value is null ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            UpdateBrandWidth();
        }
    }

    /// <summary>Optional caption controls immediately before the window buttons.</summary>
    public UIElement? CaptionActions
    {
        get => _captionActions.Child;
        set => SetCaptionContent(_captionActions, value);
    }

    public double CaptionHeight { get; private set; } = 40;

    /// <summary>Changes only chrome and caption layout; the product owns bounds and restore state.</summary>
    public bool IsFullScreen
    {
        get => _isFullScreen;
        set
        {
            if (_isFullScreen == value) return;
            _isFullScreen = value;
            IsCaptionVisible = !value;
            _topReveal.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (!value) CaptionLeftInset = 0;
            ApplyCaptionLayout();
            ApplyChrome();
        }
    }

    public bool IsCaptionVisible
    {
        get => _caption.Visibility == Visibility.Visible;
        set => _caption.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public double CaptionLeftInset
    {
        get => _caption.Margin.Left;
        set
        {
            var inset = double.IsFinite(value) ? Math.Max(0, value) : 0;
            _caption.Margin = _topReveal.Margin = new Thickness(inset, 0, 0, 0);
        }
    }

    public bool IsCaptionMouseOver => _caption.IsMouseOver;
    public event EventHandler? CaptionRevealRequested;

    public void ApplyMetrics(double uiFontSize)
    {
        var size = double.IsFinite(uiFontSize) ? Math.Clamp(uiFontSize, 10, 20) : 16;
        CaptionHeight = Math.Max(40, size * 40 / 13);
        ApplyCaptionLayout();
        if (WindowChrome.GetWindowChrome(_owner) is { } chrome)
            chrome.CaptionHeight = IsFullScreen ? 0 : CaptionHeight;
    }

    public void ApplyLanguage(string resolvedLanguage)
    {
        _language = resolvedLanguage;
        SetButtonLabel(_minimize, UiLanguage.Text(_language, "Minimize", "最小化", "最小化"));
        SetButtonLabel(_close, UiLanguage.Text(_language, "Close window", "關閉視窗", "ウィンドウを閉じる"));
        UpdateMaximizeButton();
    }

    private static void SetCaptionContent(Border host, UIElement? content)
    {
        host.Child = content;
        if (content is not null) WindowChrome.SetIsHitTestVisibleInChrome(content, true);
    }

    private Button CaptionButton(string name, string glyph, Action action)
    {
        var button = new Button
        {
            Name = name, Content = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 42, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center
        };
        // An explicit base style prevents product-specific implicit buttons changing caption geometry.
        button.SetResourceReference(StyleProperty, "UiButtonStyle");
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        button.Click += (_, _) => { if (!_closed) action(); };
        return button;
    }

    private static void SetButtonLabel(Button button, string label)
    {
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
    }

    private void ApplyCaptionLayout()
    {
        _captionRow.Height = new GridLength(IsFullScreen ? 0 : CaptionHeight);
        _caption.Height = IsFullScreen ? CaptionHeight : double.NaN;
        _caption.VerticalAlignment = IsFullScreen ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        SetRowSpan(_caption, IsFullScreen ? 2 : 1);
    }

    private void ApplyChrome() => WindowChrome.SetWindowChrome(_owner, new WindowChrome
    {
        CaptionHeight = IsFullScreen ? 0 : CaptionHeight,
        ResizeBorderThickness = new Thickness(IsFullScreen || _owner.ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) ? 0 : 6),
        GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(4),
        UseAeroCaptionButtons = false
    });

    private void UpdateBrandWidth()
    {
        if (CaptionContent is null || _caption.ActualWidth <= 0) { _brand.MaxWidth = double.PositiveInfinity; return; }
        var controlsWidth = _owner.ResizeMode == ResizeMode.NoResize ? 42 : 126;
        _brand.MaxWidth = Math.Max(0, _caption.ActualWidth - controlsWidth - _captionActions.ActualWidth - 100
            - _brand.Margin.Left - _brand.Margin.Right);
    }

    private void OwnerResizeModeChanged(object? sender, EventArgs e)
    {
        UpdateResizeMode();
        ApplyChrome();
    }

    private void UpdateResizeMode()
    {
        var showButtons = _owner.ResizeMode != ResizeMode.NoResize;
        _minimize.Visibility = _maximize.Visibility = showButtons ? Visibility.Visible : Visibility.Collapsed;
        _maximize.IsEnabled = _owner.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        UpdateBrandWidth();
    }

    private void OwnerSourceInitialized(object? sender, EventArgs e) => AttachWindowHook();
    private void AttachWindowHook()
    {
        if (_source is not null || _closed) return;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_owner).Handle);
        _source?.AddHook(WindowBoundsMessage);
    }

    private nint WindowBoundsMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Preserve the monitor work area for custom chrome; F11 intentionally includes the taskbar.
        if (message == 0x0024 && !IsFullScreen) WindowWorkArea.ApplyMaximizedBounds(handle, lParam);
        // WPF must still enforce MinWidth/MinHeight and explicit maximum constraints.
        return 0;
    }

    private void OwnerStateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();
    private void UpdateMaximizeButton()
    {
        var maximized = _owner.WindowState == WindowState.Maximized;
        _maximize.Content = maximized ? "\uE923" : "\uE922";
        SetButtonLabel(_maximize, maximized
            ? UiLanguage.Text(_language, "Restore", "還原", "元に戻す")
            : UiLanguage.Text(_language, "Maximize", "最大化", "最大化"));
    }

    private void OwnerClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _owner.SourceInitialized -= OwnerSourceInitialized;
        _owner.StateChanged -= OwnerStateChanged;
        _owner.Closed -= OwnerClosed;
        _resizeModeProperty.RemoveValueChanged(_owner, OwnerResizeModeChanged);
        if (_source is { IsDisposed: false }) _source.RemoveHook(WindowBoundsMessage);
        _source = null;
    }
}