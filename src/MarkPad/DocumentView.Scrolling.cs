using System.Windows.Threading;
using MarkPad.Models;
using MarkPad.Rendering;

namespace MarkPad;

public sealed partial class DocumentView
{
    private bool _scrollQueued;
    private bool _sendingScroll;
    private long _scrollVersion;
    private bool CanSynchronizeScroll => !_disposed && IsVisible && !Document.IsPreviewMode && Editor.IsVisible;

    private void OnEditorScrollChanged(object? sender, EventArgs args) => QueueEditorScroll();

    private void QueueEditorScroll()
    {
        if (!CanSynchronizeScroll) return;
        _scrollVersion++;
        if (_scrollQueued) return;
        _scrollQueued = true;
        // Read after AvalonEdit's layout and saved-position restoration have completed.
        Dispatcher.BeginInvoke(SynchronizeEditorScroll, DispatcherPriority.Background);
    }

    private async void SynchronizeEditorScroll()
    {
        if (_sendingScroll) return;
        _sendingScroll = true;
        try
        {
            while (_scrollQueued)
            {
                _scrollQueued = false;
                if (!CanSynchronizeScroll || !Preview.IsShowing(Document.Content, Document.FilePath)) continue;
                await Preview.ScrollToPositionAsync(Editor.GetScrollPosition());
            }
        }
        finally { _sendingScroll = false; }
    }

    private void SynchronizePreviewScroll(PreviewMessage message)
    {
        if (message.Type == "ready")
        {
            // A live render restores its old pixels first; the editor owns the latest position.
            QueueEditorScroll();
            return;
        }
        if (message.Type != "scroll" || message.Flag || message.SourcePosition < 1 || !CanSynchronizeScroll
            || !Preview.IsShowing(Document.Content, Document.FilePath)) return;
        _scrollQueued = false;
        var version = ++_scrollVersion;
        Dispatcher.BeginInvoke(() =>
        {
            if (version != _scrollVersion || !CanSynchronizeScroll
                || !Preview.IsShowing(Document.Content, Document.FilePath)) return;
            Editor.ScrollToPosition(new ScrollPosition(message.SourcePosition, message.ScrollProgress));
        }, DispatcherPriority.Background);
    }
}
