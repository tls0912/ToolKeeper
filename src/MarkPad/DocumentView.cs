using System.Windows;
using System.Windows.Controls;
using MarkPad.Editing;
using MarkPad.Models;
using MarkPad.Rendering;

namespace MarkPad;

/// <summary>Retains one document's editor and browser until the tab is closed.</summary>
public sealed class DocumentView : Grid, IDisposable
{
    private bool _disposed;
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
        Children.Add(Preview);
        Children.Add(Editor);
        Editor.Bind(document);
        Preview.MessageReceived += ForwardPreviewMessage;
    }

    public void SetActive(bool active)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Binding the same document only refreshes read-only state; selection and undo stay intact.
        Editor.Bind(Document);
        Preview.Visibility = Document.IsPreviewMode ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = Document.IsPreviewMode ? Visibility.Collapsed : Visibility.Visible;
        Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ForwardPreviewMessage(object? sender, PreviewMessage message) =>
        PreviewMessageReceived?.Invoke(this, message);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Visibility = Visibility.Collapsed;
        Preview.MessageReceived -= ForwardPreviewMessage;
        PreviewMessageReceived = null;
        Preview.Dispose();
        // Detach AvalonEdit from the live TextDocument before dropping this container.
        Editor.Bind(null);
        Children.Clear();
    }
}
