using MarkPad.Models;

namespace MarkPad.Editing;

public sealed partial class EditorPane
{
    private bool _applyingScroll;
    private double? _synchronizedOffset;

    public event EventHandler? ScrollChanged;

    private void OnScrollOffsetChanged()
    {
        if (_binding || _applyingScroll) return;
        // AvalonEdit may report the applied offset again after its layout pass.
        if (_synchronizedOffset is { } offset && Math.Abs(Editor.TextArea.TextView.ScrollOffset.Y - offset) < 0.5) return;
        _synchronizedOffset = null;
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    public ScrollPosition GetScrollPosition()
    {
        var view = Editor.TextArea.TextView;
        view.EnsureVisualLines();
        var offset = view.ScrollOffset.Y;
        var range = Math.Max(0, Editor.ExtentHeight - Editor.ViewportHeight);
        var line = view.GetDocumentLineByVisualTop(offset);
        var visual = view.GetOrConstructVisualLine(line);
        var fraction = Math.Clamp((offset - visual.VisualTop) / Math.Max(1, visual.Height), 0, 0.999999);
        var progress = range > 0 && offset > 0 ? range - offset <= 1 ? 1 : Math.Clamp(offset / range, 0, 1) : 0;
        return new ScrollPosition(line.LineNumber + fraction, progress);
    }

    /// <summary>Move the viewport without moving the caret, selection, or keyboard focus.</summary>
    public void ScrollToPosition(ScrollPosition position)
    {
        if (!double.IsFinite(position.Line) || !double.IsFinite(position.Progress)) return;
        _applyingScroll = true;
        try
        {
            var view = Editor.TextArea.TextView;
            view.EnsureVisualLines();
            var source = Math.Clamp(position.Line, 1, Editor.Document.LineCount + 0.999999);
            var line = Editor.Document.GetLineByNumber((int)source);
            var visual = view.GetOrConstructVisualLine(line);
            var range = Math.Max(0, Editor.ExtentHeight - Editor.ViewportHeight);
            var offset = position.Progress <= 0 ? 0 : position.Progress >= 1 ? range
                : Math.Clamp(visual.VisualTop + (source - line.LineNumber) * visual.Height, 0, range);
            _synchronizedOffset = offset;
            // End-scrolling lets the ScrollViewer follow extent changes as wrapped
            // lines are measured; a pixel offset can stop short of the actual end.
            if (position.Progress >= 1)
            {
                // AvalonEdit initially estimates offscreen wrapped-line heights.
                // Realizing the final viewport can increase the extent again.
                for (var pass = 0; pass < 8; pass++)
                {
                    Editor.ScrollToEnd();
                    Editor.UpdateLayout();
                    if (Editor.ExtentHeight - Editor.ViewportHeight - view.ScrollOffset.Y <= 1) break;
                }
            }
            else
            {
                Editor.ScrollToVerticalOffset(offset);
                Editor.UpdateLayout();
            }
            _synchronizedOffset = view.ScrollOffset.Y;
            SavePosition();
        }
        finally { _applyingScroll = false; }
    }
}
