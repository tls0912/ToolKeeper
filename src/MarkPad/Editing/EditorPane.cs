using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Indentation;
using ICSharpCode.AvalonEdit.Rendering;
using MarkPad.Models;

namespace MarkPad.Editing;

public sealed record SearchResult(int Index, int Count, bool Wrapped);

/// <summary>A single editor surface, with the actual text and undo stack owned by each tab.</summary>
public sealed class EditorPane : UserControl
{
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""\[\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private readonly SearchRenderer _searchRenderer;
    private readonly ActiveLineNumberMargin _lineNumbers;
    private readonly Popup _selectionPopup;
    private readonly Border _popupBorder;
    private readonly DispatcherTimer _searchRefresh;
    private readonly List<int> _matches = [];
    private readonly List<Button> _formatButtons = [];
    private DocumentTab? _tab;
    private string _query = string.Empty;
    private bool _matchCase;
    private int _currentMatch = -1;
    private bool _binding;
    private bool _selectingResult;
    private bool _formatting;
    private bool _searchSelection;

    public TextEditor Editor { get; }
    public string SelectedText => Editor.SelectedText;
    public event EventHandler? ContentChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler<SearchResult>? SearchChanged;
    public event EventHandler? PasteImageRequested;
    public event EventHandler<string>? OpenUrlRequested;

    public EditorPane()
    {
        Editor = new TextEditor
        {
            WordWrap = true,
            ShowLineNumbers = false,
            Padding = new Thickness(12, 16, 16, 32),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            AllowDrop = true,
        };
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.HighlightCurrentLine = true;
        // Navigation is routed to the host, so only an explicit Ctrl+Click opens URLs.
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.TextArea.IndentationStrategy = new DefaultIndentationStrategy();
        _lineNumbers = new ActiveLineNumberMargin(Editor) { Margin = new Thickness(0, 0, 16, 0) };
        Editor.TextArea.LeftMargins.Add(_lineNumbers);
        _searchRenderer = new SearchRenderer(this);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_searchRenderer);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        AddFormatButton(buttons, "B", "Bold · Ctrl+B", () => WrapSelection("**", "**"));
        AddFormatButton(buttons, "I", "Italic · Ctrl+I", () => WrapSelection("*", "*"));
        AddFormatButton(buttons, "`", "Inline code", () => WrapSelection("`", "`"));
        AddFormatButton(buttons, "↗", "Link", InsertLink);
        _popupBorder = new Border
        {
            Child = buttons,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(3),
        };
        _selectionPopup = new Popup
        {
            Child = _popupBorder,
            PlacementTarget = Editor.TextArea.TextView,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            StaysOpen = true,
        };
        Content = Editor;
        _searchRefresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _searchRefresh.Tick += (_, _) => { _searchRefresh.Stop(); RefreshMatches(); };
        Editor.TextChanged += OnTextChanged;
        Editor.TextArea.SelectionChanged += OnSelectionChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            SavePosition();
            _lineNumbers.InvalidateVisual();
            if (!_binding) SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) =>
        {
            SavePosition();
            _selectionPopup.IsOpen = false;
        };
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.PreviewKeyDown += OnPreviewKeyDown;
        Editor.PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
        Editor.PreviewMouseLeftButtonUp += (_, _) => ShowSelectionPopup();
        Editor.LostKeyboardFocus += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!Editor.IsKeyboardFocusWithin && !_selectionPopup.IsKeyboardFocusWithin)
                    _selectionPopup.IsOpen = false;
            }, DispatcherPriority.Background);
        };
        DataObject.AddPastingHandler(Editor, OnPasting);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) _selectionPopup.IsOpen = false;
            else if (_tab is { } tab)
            {
                var offset = tab.EditorScroll;
                Dispatcher.BeginInvoke(() =>
                {
                    if (ReferenceEquals(_tab, tab)) Editor.ScrollToVerticalOffset(offset);
                }, DispatcherPriority.Loaded);
            }
        };
        Unloaded += (_, _) => { _selectionPopup.IsOpen = false; _searchRefresh.Stop(); };
        ApplyOptions(false, "Cascadia Mono, Consolas", 16);
    }

    public void Bind(DocumentTab? tab)
    {
        if (ReferenceEquals(tab, _tab))
        {
            Editor.IsReadOnly = tab?.IsReadOnly ?? true;
            return;
        }

        SavePosition();
        var scrollToRestore = tab?.EditorScroll ?? 0;
        _searchRefresh.Stop();
        _selectionPopup.IsOpen = false;
        _binding = true;
        try
        {
            _tab = tab;
            Editor.Document = tab?.Document ?? new TextDocument();
            Editor.IsReadOnly = tab?.IsReadOnly ?? true;
            Editor.CaretOffset = Math.Clamp(tab?.CaretOffset ?? 0, 0, Editor.Document.TextLength);
            Editor.ScrollToVerticalOffset(scrollToRestore);
        }
        finally { _binding = false; }
        _currentMatch = -1;
        _searchSelection = false;
        RefreshMatches();
        // A tab may be bound before the editor has been measured after Preview mode.
        Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_tab, tab))
            {
                _binding = true;
                try { Editor.ScrollToVerticalOffset(scrollToRestore); }
                finally { _binding = false; }
            }
        }, DispatcherPriority.Loaded);
    }

    public void ApplyOptions(bool dark, string fontFamily, double fontSize)
    {
        Editor.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? "Cascadia Mono, Consolas" : fontFamily);
        Editor.FontSize = Math.Clamp(double.IsFinite(fontSize) ? fontSize : 16, 8, 72);
        Editor.Background = Brush(dark ? "#0D1117" : "#E8EBEF");
        Editor.Foreground = Brush(dark ? "#E6EDF3" : "#24292F");
        Editor.LineNumbersForeground = Brush(dark ? "#7D8590" : "#8C959F");
        _lineNumbers.SetValue(Control.ForegroundProperty, Editor.LineNumbersForeground);
        _lineNumbers.ActiveBrush = Brush(dark ? "#79C0FF" : "#0969DA");
        _lineNumbers.ActiveBackground = Brush(dark ? "#161B22" : "#DDE2E8");
        Editor.TextArea.TextView.CurrentLineBackground = _lineNumbers.ActiveBackground;
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);
        Editor.TextArea.SelectionBrush = Brush(dark ? "#264F78" : "#ADD6FF");
        Editor.TextArea.SelectionForeground = Editor.Foreground;
        Editor.SyntaxHighlighting = MarkdownHighlighting.Create(dark);
        _popupBorder.Background = Brush(dark ? "#21262D" : "#E8EBEF");
        _popupBorder.BorderBrush = Brush(dark ? "#30363D" : "#BEC6CF");
        _popupBorder.SetValue(TextElement.ForegroundProperty, Editor.Foreground);
        _searchRenderer.MatchBrush = Brush(dark ? "#665A1D" : "#FFF0A6");
        _searchRenderer.CurrentBrush = Brush(dark ? "#9E6A03" : "#FFCE53");
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        _lineNumbers.InvalidateVisual();
    }

    public void FocusEditor() => Editor.Focus();

    public bool DismissSelectionToolbar()
    {
        var wasOpen = _selectionPopup.IsOpen;
        _selectionPopup.IsOpen = false;
        return wasOpen;
    }

    public void ApplyLanguage(string language)
    {
        string[] tooltips = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? ["粗體 · Ctrl+B", "斜體 · Ctrl+I", "行內程式碼", "連結"]
            : language.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
                ? ["太字 · Ctrl+B", "斜体 · Ctrl+I", "インラインコード", "リンク"]
                : ["Bold · Ctrl+B", "Italic · Ctrl+I", "Inline code", "Link"];
        for (var index = 0; index < _formatButtons.Count; index++) _formatButtons[index].ToolTip = tooltips[index];
    }

    public void GoToLine(int line)
    {
        var target = Editor.Document.GetLineByNumber(Math.Clamp(line, 1, Editor.Document.LineCount));
        Editor.CaretOffset = target.Offset;
        Editor.ScrollToLine(target.LineNumber);
        FocusEditor();
    }

    public void Find(string text, bool matchCase, bool backwards = false, bool restart = false)
    {
        _searchRefresh.Stop();
        var sameQuery = string.Equals(_query, text, StringComparison.Ordinal) && _matchCase == matchCase;
        var anchor = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        _query = text ?? string.Empty;
        _matchCase = matchCase;
        RefreshMatches(false);
        if (_matches.Count == 0)
        {
            _searchSelection = false;
            PublishSearch(false);
            return;
        }

        int next;
        bool wrapped;
        if (backwards)
        {
            next = _matches.FindLastIndex(offset => restart || !sameQuery ? offset <= anchor : offset < anchor);
            wrapped = next < 0;
            if (wrapped) next = _matches.Count - 1;
        }
        else
        {
            if (sameQuery && !restart && _searchSelection) anchor += Math.Max(1, Editor.SelectionLength);
            next = _matches.FindIndex(offset => offset >= anchor);
            wrapped = next < 0;
            if (wrapped) next = 0;
        }

        _currentMatch = next;
        _selectingResult = true;
        try
        {
            Editor.Select(_matches[next], _query.Length);
            Editor.ScrollToLine(Editor.Document.GetLineByOffset(_matches[next]).LineNumber);
            Editor.TextArea.Caret.BringCaretToView();
            _searchSelection = true;
            _selectionPopup.IsOpen = false;
        }
        finally { _selectingResult = false; }
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        PublishSearch(wrapped);
    }

    public void Replace(string replacement)
    {
        if (Editor.IsReadOnly || string.IsNullOrEmpty(_query)) return;
        var comparison = _matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!string.Equals(Editor.SelectedText, _query, comparison))
        {
            Find(_query, _matchCase, restart: true);
            return;
        }
        var offset = Editor.SelectionStart;
        Editor.Document.Replace(offset, Editor.SelectionLength, replacement ?? string.Empty);
        Editor.CaretOffset = offset + (replacement?.Length ?? 0);
        Editor.SelectionLength = 0;
        _searchSelection = false;
        Find(_query, _matchCase, restart: true);
    }

    /// <summary>The host confirms the count with the user before calling this method.</summary>
    public int ReplaceAll(string search, string replacement, bool matchCase)
    {
        if (Editor.IsReadOnly || string.IsNullOrEmpty(search)) return 0;
        var offsets = FindOffsets(Editor.Document.Text, search, matchCase);
        if (offsets.Count == 0) return 0;
        Editor.Document.BeginUpdate();
        try
        {
            for (var index = offsets.Count - 1; index >= 0; index--)
                Editor.Document.Replace(offsets[index], search.Length, replacement ?? string.Empty);
        }
        finally { Editor.Document.EndUpdate(); }
        _searchSelection = false;
        RefreshMatches();
        return offsets.Count;
    }

    public void WrapSelection(string prefix, string suffix)
    {
        if (Editor.IsReadOnly) return;
        _formatting = true;
        try
        {
            var offset = Editor.SelectionStart;
            var selected = Editor.SelectedText;
            Editor.Document.Replace(offset, Editor.SelectionLength, prefix + selected + suffix);
            Editor.Select(offset + prefix.Length, selected.Length);
            FocusEditor();
        }
        finally { _formatting = false; _selectionPopup.IsOpen = false; }
    }

    public void SetHeading(int level)
    {
        if (Editor.IsReadOnly || level is < 1 or > 6) return;
        var first = Editor.Document.GetLineByOffset(Editor.SelectionStart).LineNumber;
        var finalOffset = Editor.SelectionStart + Math.Max(0, Editor.SelectionLength - 1);
        var last = Editor.Document.GetLineByOffset(finalOffset).LineNumber;
        Editor.Document.BeginUpdate();
        try
        {
            for (var index = last; index >= first; index--)
            {
                var line = Editor.Document.GetLineByNumber(index);
                var value = Editor.Document.GetText(line.Offset, line.Length);
                var existing = Regex.Match(value, @"^ {0,3}#{1,6}(?:[ \t]+|$)");
                Editor.Document.Replace(line.Offset, existing.Length, new string('#', level) + " ");
            }
        }
        finally { Editor.Document.EndUpdate(); }
        FocusEditor();
    }

    private void SavePosition()
    {
        if (_binding || _tab is null) return;
        _tab.CaretOffset = Editor.CaretOffset;
        // Collapsing the editor for Preview can reset its layout offset to zero.
        if (Editor.IsVisible) _tab.EditorScroll = Editor.VerticalOffset;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_binding) return;
        SavePosition();
        _selectionPopup.IsOpen = false;
        _searchSelection = false;
        _currentMatch = -1;
        // Stale offsets can extend beyond the edited document; hide until recomputed.
        _matches.Clear();
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        if (!string.IsNullOrEmpty(_query)) { _searchRefresh.Stop(); _searchRefresh.Start(); }
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_binding || _selectingResult) return;
        _searchSelection = false;
        _currentMatch = _matches.BinarySearch(Editor.SelectionStart);
        if (Editor.SelectionLength != _query.Length) _currentMatch = -1;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (_formatting) return;
        if (Mouse.LeftButton != MouseButtonState.Pressed)
            Dispatcher.BeginInvoke(ShowSelectionPopup, DispatcherPriority.Background);
    }

    private void ShowSelectionPopup()
    {
        if (!IsVisible || !Editor.IsKeyboardFocusWithin || Editor.IsReadOnly || Editor.SelectionLength == 0 || _searchSelection || _formatting)
        {
            _selectionPopup.IsOpen = false;
            return;
        }
        var view = Editor.TextArea.TextView;
        if (!view.VisualLinesValid) return;
        var position = view.GetVisualPosition(Editor.TextArea.Caret.Position, VisualYPosition.LineTop) - view.ScrollOffset;
        _selectionPopup.HorizontalOffset = Math.Clamp(position.X, 0, Math.Max(0, view.ActualWidth - 156));
        _selectionPopup.VerticalOffset = position.Y >= 38 ? position.Y - 38 : position.Y + Editor.FontSize * 1.6;
        _selectionPopup.IsOpen = true;
    }

    private void InsertLink()
    {
        if (Editor.IsReadOnly) return;
        var offset = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        WrapSelection("[", "](https://)");
        Editor.Select(offset + length + 3, 8);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Editor.IsReadOnly) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.B) { WrapSelection("**", "**"); e.Handled = true; }
            else if (e.Key == Key.I) { WrapSelection("*", "*"); e.Handled = true; }
            else if (e.Key >= Key.D1 && e.Key <= Key.D6) { SetHeading((int)e.Key - (int)Key.D1 + 1); e.Handled = true; }
        }
        if (e.Key == Key.Escape) _selectionPopup.IsOpen = false;
        if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None && Editor.SelectionLength == 0 && Editor.CaretOffset > 0 && Editor.CaretOffset < Editor.Document.TextLength)
        {
            var offset = Editor.CaretOffset;
            var previous = Editor.Document.GetCharAt(offset - 1);
            var next = Editor.Document.GetCharAt(offset);
            if ((previous == '(' && next == ')') || (previous == '[' && next == ']') || (previous == '{' && next == '}'))
            {
                Editor.Document.Remove(offset - 1, 2);
                Editor.CaretOffset = offset - 1;
                e.Handled = true;
            }
        }
    }

    private void OnTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (Editor.IsReadOnly || e.Text.Length != 1) return;
        var typed = e.Text[0];
        var closing = typed switch { '(' => ')', '[' => ']', '{' => '}', _ => '\0' };
        if (closing != '\0')
        {
            WrapSelection(typed.ToString(), closing.ToString());
            e.Handled = true;
        }
        else if (Editor.SelectionLength == 0 && typed is ')' or ']' or '}' && Editor.CaretOffset < Editor.Document.TextLength && Editor.Document.GetCharAt(Editor.CaretOffset) == typed)
        {
            Editor.CaretOffset++;
            e.Handled = true;
        }
    }

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (Editor.IsReadOnly) return;
        if (e.DataObject.GetDataPresent(DataFormats.Bitmap))
        {
            e.CancelCommand();
            PasteImageRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (Editor.SelectionLength == 0 || !e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        var text = (e.DataObject.GetData(DataFormats.UnicodeText) as string)?.Trim();
        if (text is null || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        e.CancelCommand();
        var label = Editor.SelectedText.Replace("[", @"\[").Replace("]", @"\]");
        var url = text.Replace("(", "%28").Replace(")", "%29");
        var start = Editor.SelectionStart;
        var replacement = $"[{label}]({url})";
        Editor.Document.Replace(start, Editor.SelectionLength, replacement);
        Editor.Select(start + replacement.Length, 0);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _selectionPopup.IsOpen = false;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position is null) return;
        var line = Editor.Document.GetLineByNumber(position.Value.Line);
        var content = Editor.Document.GetText(line.Offset, line.Length);
        try
        {
            foreach (Match match in UrlPattern.Matches(content))
            {
                var column = position.Value.Column - 1;
                if (column < match.Index || column >= match.Index + match.Length) continue;
                var url = TrimUrl(match.Value);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                {
                    OpenUrlRequested?.Invoke(this, url);
                    e.Handled = true;
                }
                break;
            }
        }
        catch (RegexMatchTimeoutException) { /* A pathological line must not block editing. */ }
    }

    private void RefreshMatches(bool publish = true)
    {
        _matches.Clear();
        if (!string.IsNullOrEmpty(_query)) _matches.AddRange(FindOffsets(Editor.Document.Text, _query, _matchCase));
        _currentMatch = _matches.BinarySearch(Editor.SelectionStart);
        if (Editor.SelectionLength != _query.Length) _currentMatch = -1;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        if (publish) PublishSearch(false);
    }

    private static string TrimUrl(string value)
    {
        value = value.TrimEnd('.', ',', ';', '!', '}');
        while (value.EndsWith(')') && value.Count(character => character == ')') > value.Count(character => character == '('))
            value = value[..^1];
        return value;
    }

    private void PublishSearch(bool wrapped) => SearchChanged?.Invoke(this, new SearchResult(Math.Max(0, _currentMatch + 1), _matches.Count, wrapped));

    private static List<int> FindOffsets(string source, string search, bool matchCase)
    {
        List<int> result = [];
        if (search.Length == 0) return result;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var start = 0; start <= source.Length - search.Length;)
        {
            var offset = source.IndexOf(search, start, comparison);
            if (offset < 0) break;
            result.Add(offset);
            start = offset + search.Length;
        }
        return result;
    }

    private void AddFormatButton(Panel panel, string label, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = label,
            ToolTip = tooltip,
            MinWidth = 32,
            Height = 28,
            Padding = new Thickness(6, 0, 6, 0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false,
        };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        _formatButtons.Add(button);
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }

    private sealed class SearchRenderer(EditorPane owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;
        public Brush MatchBrush { get; set; } = Brushes.LightGoldenrodYellow;
        public Brush CurrentBrush { get; set; } = Brushes.Gold;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || textView.VisualLines.Count == 0 || owner._matches.Count == 0) return;
            var firstOffset = textView.VisualLines[0].FirstDocumentLine.Offset;
            var endOffset = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            var first = owner._matches.BinarySearch(Math.Max(0, firstOffset - owner._query.Length));
            if (first < 0) first = ~first;
            for (var index = first; index < owner._matches.Count && owner._matches[index] <= endOffset; index++)
            {
                var segment = new TextSegment { StartOffset = owner._matches[index], Length = owner._query.Length };
                foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                    drawingContext.DrawRoundedRectangle(index == owner._currentMatch ? CurrentBrush : MatchBrush, null, rectangle, 2, 2);
            }
        }
    }

    private sealed class ActiveLineNumberMargin(TextEditor editor) : LineNumberMargin
    {
        public Brush ActiveBrush { get; set; } = Brushes.DodgerBlue;
        public Brush ActiveBackground { get; set; } = Brushes.WhiteSmoke;

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (TextView?.VisualLinesValid != true) return;
            var active = TextView.VisualLines.FirstOrDefault(line => line.FirstDocumentLine.LineNumber == editor.TextArea.Caret.Line);
            if (active is null || active.TextLines.Count == 0) return;
            var y = active.GetTextLineVisualYPosition(active.TextLines[0], VisualYPosition.TextTop) - TextView.VerticalOffset;
            var text = new FormattedText(editor.TextArea.Caret.Line.ToString(CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(editor.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                editor.FontSize, ActiveBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            drawingContext.DrawRectangle(ActiveBackground, null, new Rect(0, y, ActualWidth, text.Height));
            drawingContext.DrawText(text, new Point(Math.Max(0, ActualWidth - text.Width), y));
        }
    }
}
