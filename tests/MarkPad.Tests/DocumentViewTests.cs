using System.Windows;
using MarkPad.Models;
using Xunit;

namespace MarkPad.Tests;

public sealed class DocumentViewTests
{
    [Fact]
    public Task HiddenDocumentsKeepTheirOwnEditorPreviewSelectionAndUndo() => StaTest.Run(() =>
    {
        var first = new DocumentTab { Content = "first document", IsPreviewMode = false };
        var second = new DocumentTab { Content = "second document", IsPreviewMode = false };
        using var firstView = new DocumentView(first);
        using var secondView = new DocumentView(second);
        var editor = firstView.Editor;
        var preview = firstView.Preview;
        firstView.SetActive(true);
        editor.Editor.Document.Insert(0, "edited ");
        editor.Editor.Select(7, 5);
        firstView.SetActive(false);
        secondView.SetActive(true);
        secondView.Editor.Editor.Document.Insert(0, "other ");
        secondView.SetActive(false);
        firstView.SetActive(true);

        Assert.Same(editor, firstView.Editor);
        Assert.Same(preview, firstView.Preview);
        Assert.Same(first.Document, editor.Editor.Document);
        Assert.Equal("first", editor.SelectedText);
        Assert.Equal(Visibility.Visible, firstView.Visibility);
        Assert.Equal(Visibility.Collapsed, secondView.Visibility);
        editor.Editor.Undo();
        Assert.Equal("first document", first.Content);
        Assert.Equal("other second document", second.Content);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ModeChangesKeepBothContainersAndRefreshReadOnlyState() => StaTest.Run(() =>
    {
        var document = new DocumentTab { Content = "content", IsPreviewMode = false };
        using var view = new DocumentView(document);
        var editor = view.Editor;
        var preview = view.Preview;
        view.SetActive(true);
        editor.Editor.Select(1, 3);
        document.IsPreviewMode = true;
        view.SetActive(true);
        Assert.Equal(Visibility.Visible, preview.Visibility);
        Assert.Equal(Visibility.Collapsed, editor.Visibility);
        document.IsPreviewMode = false;
        document.IsReadOnly = true;
        view.SetActive(true);
        Assert.Same(editor, view.Editor);
        Assert.Same(preview, view.Preview);
        Assert.Equal("ont", editor.SelectedText);
        Assert.True(editor.Editor.IsReadOnly);
        Assert.Equal(Visibility.Collapsed, preview.Visibility);
        Assert.Equal(Visibility.Visible, editor.Visibility);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClosingContainerDetachesEditorWithoutChangingDocument() => StaTest.Run(async () =>
    {
        var document = new DocumentTab { Content = "keep this", IsPreviewMode = false };
        var view = new DocumentView(document);
        view.SetActive(true);
        view.Editor.Editor.Document.Insert(0, "edited ");
        view.Dispose();
        view.Dispose();

        Assert.Empty(view.Children);
        Assert.NotSame(document.Document, view.Editor.Editor.Document);
        Assert.Equal("edited keep this", document.Content);
        document.Document.UndoStack.Undo();
        Assert.Equal("keep this", document.Content);
        Assert.Throws<ObjectDisposedException>(() => view.SetActive(true));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => view.Preview.ExportPdfAsync("text", null, new(), "unused.pdf"));
    });
}
