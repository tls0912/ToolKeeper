using System.IO;
using MarkPad.Editing;
using MarkPad.Models;
using MarkPad.Rendering;

namespace MarkPad;

public partial class MainWindow
{
    private readonly Dictionary<Guid, DocumentView> _documentViews = [];
    private DocumentView? _currentView;
    private EditorPane _editor => _currentView!.Editor;
    private PreviewPane _preview => _currentView!.Preview;

    private DocumentView GetDocumentView(DocumentTab tab)
    {
        if (_documentViews.TryGetValue(tab.Id, out var existing)) return existing;
        var view = new DocumentView(tab, Path.Combine(App.Preferences.DataDirectory, "WebView2"));
        view.Editor.ApplyOptions(_dark, Settings.EditorFontFamily, Settings.EditorFontSize);
        view.Editor.ApplyLanguage(UiLanguage);
        _ = GuardAsync(() => view.Preview.SetThemeAsync(_dark));
        view.Editor.SelectionChanged += OnEditorSelectionChanged;
        view.Editor.SearchChanged += OnEditorSearchChanged;
        view.Editor.PasteImageRequested += OnEditorPasteImageRequested;
        view.Editor.OpenUrlRequested += OnEditorOpenUrlRequested;
        view.PreviewMessageReceived += OnPreviewMessageReceived;
        _documentViews.Add(tab.Id, view);
        DocumentHost.Children.Add(view);
        return view;
    }

    private void ReleaseDocumentView(DocumentTab tab)
    {
        if (!_documentViews.Remove(tab.Id, out var view)) return;
        view.Editor.SelectionChanged -= OnEditorSelectionChanged;
        view.Editor.SearchChanged -= OnEditorSearchChanged;
        view.Editor.PasteImageRequested -= OnEditorPasteImageRequested;
        view.Editor.OpenUrlRequested -= OnEditorOpenUrlRequested;
        view.PreviewMessageReceived -= OnPreviewMessageReceived;
        DocumentHost.Children.Remove(view);
        view.Dispose();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs args)
    {
        if (!_disposed && ReferenceEquals(sender, _currentView?.Editor)) UpdateStatus();
    }

    private void OnEditorSearchChanged(object? sender, SearchResult result)
    {
        if (!_disposed && _current?.IsPreviewMode == false && ReferenceEquals(sender, _currentView?.Editor))
            ShowSearchResult(result.Index, result.Count, result.Wrapped);
    }

    private async void OnEditorPasteImageRequested(object? sender, EventArgs args)
    {
        if (!_disposed && _current?.IsPreviewMode == false && ReferenceEquals(sender, _currentView?.Editor))
            await GuardAsync(PasteImageAsync);
    }

    private async void OnEditorOpenUrlRequested(object? sender, string url)
    {
        if (!_disposed && _current?.IsPreviewMode == false && ReferenceEquals(sender, _currentView?.Editor))
            await GuardAsync(() => OpenLinkAsync(url));
    }
}
