using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MarkPad.Editing;
using MarkPad.Models;
using MarkPad.Rendering;
using ToolKeeper.UI;

namespace MarkPad;

/// <summary>Retains one document's editor and browser until the tab is closed.</summary>
public sealed partial class DocumentView : Grid, IDisposable
{
    private bool _disposed;
    private readonly ColumnDefinition _leftColumn = new();
    private readonly ColumnDefinition _rightColumn = new();
    private readonly Grid _outlinePanel = new();
    private readonly ListBox _outlineList = new() { BorderThickness = new Thickness(0), Padding = new Thickness(4), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _outlineTitle = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 12, 8, 10) };
    private readonly TextBlock _emptyOutline = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Top };
    private readonly HashSet<int> _outlineLevels = [1, 2, 3, 4, 5, 6];
    private readonly GridSplitter _splitter = new()
    {
        Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
        ShowsPreview = false, KeyboardIncrement = 16, DragIncrement = 1, Focusable = true, Cursor = Cursors.SizeWE
    };
    private bool? _previewMode;
    private bool _updatingOutlineSelection;
    private int _previewLine = 1;
    private double _outlineWidth = 200;
    private double _editorFraction = 0.5;
    private double _layoutWidth = double.NaN;
    private string _language = "en";
    public DocumentTab Document { get; }
    public EditorPane Editor { get; } = new() { Margin = new Thickness(0, 4, 0, 4), Visibility = Visibility.Collapsed };
    public PreviewPane Preview { get; }
    public bool PreviewOverlay { get; set; }
    public event EventHandler<PreviewMessage>? PreviewMessageReceived;

    public DocumentView(DocumentTab document, string? browserDataFolder = null)
    {
        Document = document;
        Visibility = Visibility.Collapsed;
        Preview = new PreviewPane(browserDataFolder) { Visibility = Visibility.Collapsed };
        ClipToBounds = true;
        ColumnDefinitions.Add(_leftColumn);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        ColumnDefinitions.Add(_rightColumn);
        SetColumn(Preview, 2);
        SetColumn(_splitter, 1);
        _outlinePanel.SetResourceReference(BackgroundProperty, "PaperBackgroundBrush");
        _outlinePanel.SetResourceReference(TextElement.FontFamilyProperty, "UiFontFamily");
        _outlinePanel.SetResourceReference(TextElement.FontSizeProperty, "UiFontSize");
        _outlinePanel.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        _outlinePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _outlinePanel.RowDefinitions.Add(new RowDefinition());
        SetRow(_outlineList, 1);
        SetRow(_emptyOutline, 1);
        _emptyOutline.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _outlinePanel.Children.Add(_outlineTitle);
        _outlinePanel.Children.Add(_outlineList);
        _outlinePanel.Children.Add(_emptyOutline);
        _outlineList.Background = Brushes.Transparent;
        _outlineList.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _outlineList.SetResourceReference(Control.FontFamilyProperty, "UiFontFamily");
        _outlineList.SetResourceReference(Control.FontSizeProperty, "UiFontSize");
        _outlineList.Template = OutlineListTemplate();
        _outlineList.ItemContainerStyle = OutlineItemStyle();
        _outlineList.SelectionChanged += async (_, _) =>
        {
            if (!_updatingOutlineSelection && _outlineList.SelectedItem is ListBoxItem { Tag: PreviewHeading heading })
                await NavigateToHeadingAsync(heading);
        };
        _outlineList.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter || _outlineList.SelectedItem is not ListBoxItem { Tag: PreviewHeading heading }) return;
            args.Handled = true;
            await NavigateToHeadingAsync(heading);
        };
        var dividerStyle = new Style(typeof(GridSplitter));
        dividerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("LineBrush")));
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentBrush")));
            dividerStyle.Triggers.Add(trigger);
        }
        _splitter.Style = dividerStyle;
        _splitter.DragCompleted += (_, _) => RememberSplitWidth();
        _splitter.KeyUp += (_, args) =>
        {
            if (args.Key is Key.Left or Key.Right) RememberSplitWidth();
        };
        Children.Add(_outlinePanel);
        Children.Add(Preview);
        Children.Add(Editor);
        Children.Add(_splitter);
        Editor.Bind(document);
        Editor.ScrollChanged += OnEditorScrollChanged;
        IsVisibleChanged += (_, _) => QueueEditorScroll();
        Preview.MessageReceived += ForwardPreviewMessage;
        Preview.HeadingsChanged += OnHeadingsChanged;
        ApplyLanguage("en");
        RefreshOutline();
    }

    public void SetActive(bool active)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Binding the same document only refreshes read-only state; selection and undo stay intact.
        Editor.Bind(Document);
        if (_previewMode != Document.IsPreviewMode)
        {
            RememberSplitWidth();
            _previewMode = Document.IsPreviewMode;
            ApplySplitWidths();
        }
        Preview.Visibility = Visibility.Visible;
        Editor.Visibility = Document.IsPreviewMode ? Visibility.Collapsed : Visibility.Visible;
        _outlinePanel.Visibility = Document.IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateSplitterLabel();
        Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        QueueEditorScroll();
    }

    public void ApplyLanguage(string language)
    {
        _language = language;
        _outlinePanel.FlowDirection = UiLanguage.IsRightToLeft(language) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _outlineTitle.Text = Translate("Outline", "章節大綱", "見出し一覧");
        UpdateEmptyOutlineMessage();
        System.Windows.Automation.AutomationProperties.SetName(_outlineList, _outlineTitle.Text);
        UpdateSplitterLabel();
        foreach (var item in _outlineList.Items.OfType<ListBoxItem>())
            if (item.Tag is PreviewHeading heading) SetHeadingLabel(item, heading);
    }

    public void ApplyOutlineLevels(IEnumerable<int> levels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var selected = levels.Where(level => level is >= 1 and <= 6).ToHashSet();
        if (_outlineLevels.SetEquals(selected)) return;
        _outlineLevels.Clear();
        _outlineLevels.UnionWith(selected);
        RefreshOutline();
    }

    private string Translate(string english, string chinese, string japanese) =>
        UiLanguage.Text(_language, english, chinese, japanese);

    private void UpdateSplitterLabel()
    {
        var label = Document.IsPreviewMode
            ? Translate("Resize outline and preview", "調整大綱與預覽寬度", "見出し一覧とプレビューの幅を調整")
            : Translate("Resize editor and preview", "調整編輯器與預覽寬度", "エディターとプレビューの幅を調整");
        System.Windows.Automation.AutomationProperties.SetName(_splitter, label);
        _splitter.ToolTip = label + Translate(" · Drag or use arrow keys", " · 拖曳或使用方向鍵", " · ドラッグまたは矢印キー");
    }

    private void RememberSplitWidth()
    {
        if (_previewMode is null || _leftColumn.ActualWidth <= 0 || _rightColumn.ActualWidth <= 0) return;
        if (_previewMode.Value) _outlineWidth = _leftColumn.ActualWidth;
        else _editorFraction = _leftColumn.ActualWidth / (_leftColumn.ActualWidth + _rightColumn.ActualWidth);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        // Use the host's constraint before measuring: an oversized initial minimum can
        // otherwise inflate ActualWidth, making a post-arrange correction ineffective.
        // Unchanged constraints must leave GridSplitter's in-progress resize untouched.
        if (double.IsFinite(constraint.Width) && constraint.Width != _layoutWidth)
        {
            _layoutWidth = constraint.Width;
            ApplySplitWidths();
        }
        return base.MeasureOverride(constraint);
    }

    private void ApplySplitWidths()
    {
        var widthConstraint = double.IsFinite(_layoutWidth) ? _layoutWidth : ActualWidth > 0 ? ActualWidth : 640;
        var available = Math.Max(0, widthConstraint - 6);
        // Preserve two useful panes on a narrow window without forcing the grid beyond its host.
        _rightColumn.MinWidth = Math.Min(180, available * 0.4);
        _leftColumn.MinWidth = Math.Min(Document.IsPreviewMode ? 120 : 180, available * 0.3);
        var desired = Document.IsPreviewMode ? _outlineWidth : available * _editorFraction;
        var width = Math.Clamp(desired, _leftColumn.MinWidth, Math.Max(_leftColumn.MinWidth, available - _rightColumn.MinWidth));
        _leftColumn.Width = new GridLength(width);
        _rightColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    private void OnHeadingsChanged(object? sender, EventArgs args) => RefreshOutline();

    private void RefreshOutline()
    {
        if (_disposed) return;
        _updatingOutlineSelection = true;
        try
        {
            _outlineList.Items.Clear();
            foreach (var heading in Preview.Headings.Where(heading => _outlineLevels.Contains(heading.Level)))
            {
                var text = new TextBlock
                {
                    Text = heading.Title, TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness((Math.Clamp(heading.Level, 1, 6) - 1) * 12, 0, 0, 0)
                };
                var item = new ListBoxItem { Content = text, Tag = heading, ToolTip = heading.Title };
                SetHeadingLabel(item, heading);
                item.PreviewMouseLeftButtonDown += async (_, _) =>
                {
                    // Clicking the already active chapter still returns to its beginning.
                    if (item.IsSelected) await NavigateToHeadingAsync(heading);
                };
                _outlineList.Items.Add(item);
            }
            _emptyOutline.Visibility = _outlineList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateEmptyOutlineMessage();
        }
        finally { _updatingOutlineSelection = false; }
        UpdateActiveHeading();
    }

    private void UpdateEmptyOutlineMessage() => _emptyOutline.Text = Preview.Headings.Count > 0 && _outlineList.Items.Count == 0
        ? Translate("No headings match the selected levels.", "所選層級沒有章節標題。", "選択したレベルの見出しがありません。")
        : Translate("No headings yet.\nAdd headings to see chapters here.", "尚無章節標題。\n加入標題後會顯示在這裡。", "見出しがありません。\n見出しを追加するとここに表示されます。");

    private void SetHeadingLabel(ListBoxItem item, PreviewHeading heading) =>
        System.Windows.Automation.AutomationProperties.SetName(item,
            ToolKeeper.UI.UiLanguage.Format(_language, "Heading {0}: {1}", "第 {0} 級標題：{1}", "見出し {0}：{1}", heading.Level, heading.Title));

    private void UpdateActiveHeading()
    {
        ListBoxItem? active = null;
        foreach (var item in _outlineList.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not PreviewHeading heading || heading.Line > _previewLine) continue;
            active = item;
        }
        if (ReferenceEquals(active, _outlineList.SelectedItem)) return;
        _updatingOutlineSelection = true;
        try
        {
            _outlineList.SelectedItem = active;
            if (active is not null && Document.IsPreviewMode) _outlineList.ScrollIntoView(active);
        }
        finally { _updatingOutlineSelection = false; }
    }

    private async Task NavigateToHeadingAsync(PreviewHeading heading)
    {
        if (_disposed) return;
        try { await Preview.GoToAnchorAsync(heading.Id); }
        catch (Exception exception)
        {
            if (!_disposed) PreviewMessageReceived?.Invoke(this, new PreviewMessage("error", exception.Message));
        }
    }

    private void ForwardPreviewMessage(object? sender, PreviewMessage message)
    {
        SynchronizePreviewScroll(message);
        if (message.Type == "scroll" && message.Line > 0)
        {
            _previewLine = message.Line;
            UpdateActiveHeading();
        }
        PreviewMessageReceived?.Invoke(this, message);
    }

    private static ControlTemplate OutlineListTemplate()
    {
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetResourceReference(StyleProperty, "SidebarScrollViewerStyle");
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        return new ControlTemplate(typeof(ListBox)) { VisualTree = scroll };
    }

    private static Style OutlineItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border), "ItemBorder");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 7, 6, 7));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2, 0, 0, 0)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        foreach (var property in new[] { IsMouseOverProperty, ListBoxItem.IsSelectedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("HoverBrush")));
            if (property != IsMouseOverProperty)
                trigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
            style.Triggers.Add(trigger);
        }
        var focus = new MultiTrigger();
        focus.Conditions.Add(new Condition(IsKeyboardFocusWithinProperty, true));
        focus.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, false));
        focus.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("AccentBrush")));
        focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Triggers.Add(focus);
        return style;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Editor.ScrollChanged -= OnEditorScrollChanged;
        Visibility = Visibility.Collapsed;
        Preview.MessageReceived -= ForwardPreviewMessage;
        Preview.HeadingsChanged -= OnHeadingsChanged;
        PreviewMessageReceived = null;
        Preview.Dispose();
        // Detach AvalonEdit from the live TextDocument before dropping this container.
        Editor.Bind(null);
        Children.Clear();
    }
}
