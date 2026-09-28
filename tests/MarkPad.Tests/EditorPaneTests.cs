using MarkPad.Editing;
using MarkPad.Models;
using Xunit;

namespace MarkPad.Tests;

public sealed class EditorPaneTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ThemesAndLanguageLoadWithMarkdownHighlighting(bool dark, bool ink) => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        pane.ApplyOptions(dark, "Consolas", 18, ink);
        pane.ApplyLanguage("zh-TW");
        pane.ApplyLanguage("ja-JP");
        Assert.Equal("Markdown", pane.Editor.SyntaxHighlighting.Name);
        Assert.Equal(18, pane.Editor.FontSize);
        pane.ApplyOptions(false, "Consolas", double.NaN);
        Assert.Equal(16, pane.Editor.FontSize);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SwitchingTabsPreservesIndependentDocumentsUndoAndCaret() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        var first = Tab("first");
        var second = Tab("second");
        pane.Bind(first);
        pane.Editor.Document.Insert(5, "!");
        pane.Editor.CaretOffset = 3;
        pane.Bind(second);
        Assert.Same(second.Document, pane.Editor.Document);
        pane.Editor.Document.Insert(0, "a ");
        pane.Bind(first);
        Assert.Same(first.Document, pane.Editor.Document);
        Assert.Equal(3, pane.Editor.CaretOffset);
        pane.Editor.Undo();
        Assert.Equal("first", first.Content);
        Assert.Equal("a second", second.Content);
        pane.Bind(second);
        pane.Editor.Undo();
        Assert.Equal("second", second.Content);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SearchStartsAtCaretAndWrapsInBothDirections() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        pane.Bind(Tab("one two ONE two one"));
        pane.Editor.CaretOffset = 4;
        SearchResult? result = null;
        pane.SearchChanged += (_, value) => result = value;
        pane.Find("one", false, restart: true);
        Assert.Equal(new SearchResult(2, 3, false), result);
        Assert.Equal(8, pane.Editor.SelectionStart);
        pane.Find("one", false);
        Assert.Equal(new SearchResult(3, 3, false), result);
        pane.Find("one", false);
        Assert.Equal(new SearchResult(1, 3, true), result);
        pane.Find("one", false, backwards: true);
        Assert.Equal(new SearchResult(3, 3, true), result);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SearchIsLiteralAndHonorsCaseAndClearing() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        pane.Bind(Tab("a.b axb A.B"));
        SearchResult? result = null;
        pane.SearchChanged += (_, value) => result = value;
        pane.Find("a.b", false, restart: true);
        Assert.Equal(2, result!.Count);
        pane.Find("a.b", true, restart: true);
        Assert.Equal(1, result!.Count);
        pane.Find("", true);
        Assert.Equal(new SearchResult(0, 0, false), result);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplaceAllChangesOriginalOccurrencesAndUndoesInOneStep() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        var tab = Tab("a A a");
        pane.Bind(tab);
        Assert.Equal(3, pane.ReplaceAll("a", "aa", false));
        Assert.Equal("aa aa aa", tab.Content);
        pane.Editor.Undo();
        Assert.Equal("a A a", tab.Content);
        Assert.False(tab.Document.UndoStack.CanUndo);
        pane.Editor.Redo();
        Assert.Equal("aa aa aa", tab.Content);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReplaceAdvancesToNextMatchAndDoesNotReplaceUnrelatedSelection() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        var tab = Tab("red blue red");
        pane.Bind(tab);
        pane.Find("red", false, restart: true);
        pane.Replace("green");
        Assert.Equal("green blue red", tab.Content);
        Assert.Equal("red", pane.SelectedText);
        Assert.Equal(11, pane.Editor.SelectionStart);
        pane.Editor.Select(0, 5);
        pane.Replace("wrong");
        Assert.Equal("green blue red", tab.Content);
        Assert.Equal("red", pane.SelectedText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FormattingWrapsSelectionAndHeadingChangesUndoAsOneStep() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        var tab = Tab("one\n## two\nlast");
        pane.Bind(tab);
        pane.Editor.Select(0, tab.Content.Length);
        pane.SetHeading(3);
        Assert.Equal("### one\n### two\n### last", tab.Content);
        pane.Editor.Undo();
        Assert.Equal("one\n## two\nlast", tab.Content);
        pane.Editor.Select(0, 3);
        pane.WrapSelection("**", "**");
        Assert.Equal("**one**\n## two\nlast", tab.Content);
        Assert.Equal("one", pane.SelectedText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BodyRemovesAtxHeadingsOnlyAndUndoesMultipleLinesInOneStep() => StaTest.Run(() =>
    {
        const string source = "# first\n   ## second\n  plain\n    # indented code\n##not-heading";
        const string expected = "first\nsecond\n  plain\n    # indented code\n##not-heading";
        var pane = new EditorPane();
        var tab = Tab(source);
        pane.Bind(tab);
        pane.Editor.Select(0, source.Length);

        pane.SetHeading(0);

        Assert.Equal(expected, tab.Content);
        pane.Editor.Undo();
        Assert.Equal(source, tab.Content);
        Assert.False(tab.Document.UndoStack.CanUndo);
        pane.Editor.Redo();
        Assert.Equal(expected, tab.Content);

        var plain = Tab("  unchanged\nplain text");
        pane.Bind(plain);
        pane.Editor.Select(0, plain.Content.Length);
        pane.SetHeading(0);
        Assert.Equal("  unchanged\nplain text", plain.Content);
        Assert.False(plain.Document.UndoStack.CanUndo);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("See this now", 4, 4, "See [this](https://) now")]
    [InlineData("See this now", 4, 0, "See [](https://)this now")]
    [InlineData("", 0, 0, "[](https://)")]
    public Task InsertLinkUsesSelectionOrCaretAndUndoesInOneStep(string source, int start, int length, string expected) => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        var tab = Tab(source);
        pane.Bind(tab);
        pane.Editor.Select(start, length);

        pane.InsertLink();

        Assert.Equal(expected, tab.Content);
        Assert.Equal("https://", pane.SelectedText);
        Assert.Equal(start + length + 3, pane.Editor.SelectionStart);
        pane.Editor.Undo();
        Assert.Equal(source, tab.Content);
        Assert.False(tab.Document.UndoStack.CanUndo);
        pane.Editor.Redo();
        Assert.Equal(expected, tab.Content);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ChangingEditorFontPreservesContentSelectionAndExistingUndo() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        var tab = Tab("keep text");
        pane.Bind(tab);
        pane.Editor.Document.Insert(9, "!");
        pane.Editor.Select(5, 4);
        var caret = pane.Editor.CaretOffset;
        var changes = 0;
        pane.ContentChanged += (_, _) => changes++;

        pane.ApplyOptions(true, "Georgia", 24, ink: true);

        Assert.Equal("keep text!", tab.Content);
        Assert.Equal("text", pane.SelectedText);
        Assert.Equal(5, pane.Editor.SelectionStart);
        Assert.Equal(caret, pane.Editor.CaretOffset);
        Assert.Equal(0, changes);
        pane.Editor.Undo();
        Assert.Equal("keep text", tab.Content);
        Assert.False(tab.Document.UndoStack.CanUndo);
        pane.Editor.Redo();
        Assert.Equal("keep text!", tab.Content);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReadOnlyDocumentBlocksAllFormattingAndReplaceCommands() => StaTest.Run(() =>
    {
        var pane = new EditorPane();
        var tab = Tab("keep me");
        tab.IsReadOnly = true;
        pane.Bind(tab);
        pane.Find("keep", false, restart: true);
        pane.Replace("change");
        Assert.Equal(0, pane.ReplaceAll("keep", "change", false));
        pane.WrapSelection("**", "**");
        pane.SetHeading(1);
        pane.SetHeading(0);
        pane.InsertLink();
        Assert.Equal("keep me", tab.Content);
        Assert.Equal("keep", pane.SelectedText);
        Assert.False(tab.Document.UndoStack.CanUndo);
        var heading = Tab("# keep heading");
        heading.IsReadOnly = true;
        pane.Bind(heading);
        pane.SetHeading(0);
        Assert.Equal("# keep heading", heading.Content);
        Assert.False(heading.Document.UndoStack.CanUndo);
        return Task.CompletedTask;
    });

    private static DocumentTab Tab(string value)
    {
        var tab = new DocumentTab { Content = value };
        tab.Document.UndoStack.ClearAll();
        tab.IsDirty = false;
        return tab;
    }
}
