using System.Globalization;
using System.IO;
using System.Text;
using System.ComponentModel;
using MarkPad.Models;
using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class DocumentFileServiceTests
{
    [Theory]
    [InlineData(65001, false)]
    [InlineData(65001, true)]
    [InlineData(1200, true)]
    [InlineData(1201, true)]
    [InlineData(12000, true)]
    [InlineData(12001, true)]
    public Task OpenAndSavePreserveEncodingBomAndMixedLineEndings(int codePage, bool bom) => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("roundtrip.md");
        var encoding = Encoding.GetEncoding(codePage);
        const string content = "# 繁體中文・日本語\r\n\r\nA line\nAnother line\r\n";
        var original = (bom ? encoding.GetPreamble() : []).Concat(encoding.GetBytes(content)).ToArray();
        await File.WriteAllBytesAsync(path, original);
        var service = new DocumentFileService();
        var tab = await service.OpenAsync(path);

        Assert.Equal(content, tab.Content);
        Assert.Equal(codePage, tab.Encoding.CodePage);
        Assert.Equal(bom, tab.HasBom);
        Assert.False(tab.IsDirty);
        Assert.False(tab.Document.UndoStack.CanUndo);
        await service.SaveAsync(tab);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));

        tab.Document.Insert(tab.Document.TextLength, "Tail\r\n");
        await service.SaveAsync(tab);
        Assert.Equal((bom ? encoding.GetPreamble() : []).Concat(encoding.GetBytes(content + "Tail\r\n")), await File.ReadAllBytesAsync(path));
        Assert.False(tab.IsDirty);
        Assert.Empty(Directory.EnumerateFiles(directory.PathName, "*.tmp"));
    });

    [Fact]
    public Task LegacyBig5RoundTripsAndUnencodableEditDoesNotDamageFile() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var service = new DocumentFileService();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-TW");
        var encoding = Encoding.GetEncoding(950);
        var original = encoding.GetBytes("# 繁體中文\r\n閱讀與編輯\r\n");
        var path = directory.FilePath("big5.md");
        await File.WriteAllBytesAsync(path, original);

        var tab = await service.OpenAsync(path);
        Assert.Equal(950, tab.Encoding.CodePage);
        await service.SaveAsync(tab);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        tab.Document.Insert(tab.Document.TextLength, "😀");
        await Assert.ThrowsAsync<EncoderFallbackException>(() => service.SaveAsync(tab));
        Assert.True(tab.IsDirty);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(directory.PathName, "*.tmp"));
    });

    [Fact]
    public Task SameTimestampAndLengthExternalChangeStillBlocksSave() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("conflict.md");
        await File.WriteAllTextAsync(path, "initial");
        var service = new DocumentFileService();
        var tab = await service.OpenAsync(path);
        tab.Content = "my edits";
        await File.WriteAllTextAsync(path, "outside");
        File.SetLastWriteTimeUtc(path, tab.LastWriteTimeUtc);

        await Assert.ThrowsAsync<FileConflictException>(() => service.SaveAsync(tab));
        Assert.Equal("outside", await File.ReadAllTextAsync(path));
        Assert.True(tab.IsDirty);
        Assert.Empty(Directory.EnumerateFiles(directory.PathName, "*.tmp"));
    });

    [Fact]
    public Task KeepCurrentAcceptsOnlyTheObservedVersion() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("keep.md");
        await File.WriteAllTextAsync(path, "original");
        var service = new DocumentFileService();
        var tab = await service.OpenAsync(path);
        tab.Content = "local";
        await File.WriteAllTextAsync(path, "external first");
        service.AcceptExternalChanges(tab);
        await File.WriteAllTextAsync(path, "external second");
        await Assert.ThrowsAsync<FileConflictException>(() => service.SaveAsync(tab));
        Assert.Equal("external second", await File.ReadAllTextAsync(path));

        service.AcceptExternalChanges(tab);
        await service.SaveAsync(tab);
        Assert.Equal("local", await File.ReadAllTextAsync(path));
        Assert.False(tab.IsDirty);
    });

    [Fact]
    public Task MissingFileKeepsContentAndRequiresExplicitSaveAs() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("missing.md");
        await File.WriteAllTextAsync(path, "retained");
        var service = new DocumentFileService();
        var tab = await service.OpenAsync(path);
        File.Delete(path);
        Assert.Equal(FileChangeStatus.Missing, service.RefreshStatus(tab));
        Assert.True(tab.IsMissing);
        Assert.Equal("retained", tab.Content);
        await Assert.ThrowsAsync<FileConflictException>(() => service.SaveAsync(tab));
        Assert.False(File.Exists(path));
        await service.SaveAsync(tab, path);
        Assert.Equal("retained", await File.ReadAllTextAsync(path));
        Assert.False(tab.IsMissing);
    });

    [Fact]
    public Task ReadOnlyFilesOpenInPreviewAndCanBeSavedAs() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("readonly.md");
        await File.WriteAllTextAsync(path, "read only");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var service = new DocumentFileService();
        var tab = await service.OpenAsync(path);
        Assert.True(tab.IsReadOnly);
        Assert.True(tab.IsPreviewMode);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(tab));
        await service.SaveAsync(tab, directory.FilePath("copy.md"));
        Assert.False(tab.IsReadOnly);
        Assert.Equal("read only", await File.ReadAllTextAsync(path));
    });

    [Fact]
    public Task TabsKeepIndependentUndoAndClearDirtyWhenUndoReturnsToSavedText() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var first = new DocumentTab();
        var second = new DocumentTab();
        var service = new DocumentFileService();
        first.Content = "first";
        await service.SaveAsync(first, directory.FilePath("first.md"));
        first.Document.Insert(first.Document.TextLength, " changed");
        second.Content = "second";
        first.IsPreviewMode = false;
        first.IsPreviewMode = true;
        Assert.True(first.IsDirty);
        first.Document.UndoStack.Undo();
        Assert.Equal("first", first.Content);
        Assert.False(first.IsDirty);
        Assert.Equal("second", second.Content);
        Assert.True(second.IsDirty);
        first.Document.UndoStack.Redo();
        Assert.True(first.IsDirty);
    });

    [Theory]
    [InlineData("")]
    [InlineData("saved snapshot")]
    public Task UndoReturnsToTheSnapshotSavedBeforeAnInterleavedEdit(string content) => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var tab = new MarkPad.Models.DocumentTab { Content = content };
        var service = new DocumentFileService();
        var path = directory.FilePath("snapshot.md");
        await SaveWithInterleavedEditAsync(service, tab, path);

        Assert.Equal(content, await File.ReadAllTextAsync(path));
        Assert.Equal(content + " later", tab.Content);
        Assert.True(tab.IsDirty);
        tab.Document.UndoStack.Undo();
        Assert.Equal(content, tab.Content);
        Assert.False(tab.IsDirty);
        tab.Document.UndoStack.Redo();
        Assert.Equal(content + " later", tab.Content);
        Assert.True(tab.IsDirty);
    });

    [Fact]
    public Task LaterSaveReplacesTheInterleavedSaveBaseline() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var tab = new MarkPad.Models.DocumentTab { Content = "first" };
        var service = new DocumentFileService();
        await SaveWithInterleavedEditAsync(service, tab, directory.FilePath("first.md"));
        await SaveWithInterleavedEditAsync(service, tab, directory.FilePath("second.md"));

        tab.Document.UndoStack.Undo();
        Assert.Equal("first later", tab.Content);
        Assert.False(tab.IsDirty);
        tab.Document.UndoStack.Undo();
        Assert.Equal("first", tab.Content);
        Assert.True(tab.IsDirty);
        tab.Document.UndoStack.Redo();
        Assert.False(tab.IsDirty);
        tab.Document.UndoStack.Redo();
        await service.SaveAsync(tab);
        Assert.False(tab.IsDirty);
        tab.Document.UndoStack.Undo();
        Assert.True(tab.IsDirty);
        tab.Document.UndoStack.Redo();
        Assert.False(tab.IsDirty);
    });

    [Fact]
    public Task FailedSavePreservesTheLastSuccessfulInterleavedSnapshot() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var tab = new MarkPad.Models.DocumentTab { Content = "saved" };
        var service = new DocumentFileService();
        var path = directory.FilePath("failed.md");
        await SaveWithInterleavedEditAsync(service, tab, path);
        tab.Encoding = Encoding.ASCII;
        tab.Document.Insert(tab.Document.TextLength, "中文");

        await Assert.ThrowsAsync<EncoderFallbackException>(() => service.SaveAsync(tab));
        Assert.Equal("saved", await File.ReadAllTextAsync(path));
        Assert.True(tab.IsDirty);
        tab.Document.UndoStack.Undo();
        Assert.True(tab.IsDirty);
        tab.Document.UndoStack.Undo();
        Assert.Equal("saved", tab.Content);
        Assert.False(tab.IsDirty);
    });

    [Fact]
    public Task ExplicitDirtyAndReloadDecisionsReplaceTheInterleavedSnapshot() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var tab = new MarkPad.Models.DocumentTab { Content = "saved" };
        var service = new DocumentFileService();
        await SaveWithInterleavedEditAsync(service, tab, directory.FilePath("forced.md"));
        tab.IsDirty = true;
        tab.Document.UndoStack.Undo();
        Assert.Equal("saved", tab.Content);
        Assert.True(tab.IsDirty);

        await SaveWithInterleavedEditAsync(service, tab, directory.FilePath("reload.md"));
        tab.Content = "reloaded";
        tab.Document.UndoStack.ClearAll();
        tab.IsDirty = false;
        tab.Document.Insert(tab.Document.TextLength, " changed");
        tab.Document.UndoStack.Undo();
        Assert.Equal("reloaded", tab.Content);
        Assert.False(tab.IsDirty);
    });

    private static async Task SaveWithInterleavedEditAsync(DocumentFileService service, MarkPad.Models.DocumentTab tab, string path)
    {
        // FilePath changes after writing the captured bytes but before updating dirty state.
        // Editing at that public notification makes the race deterministic without timing sleeps.
        var edited = false;
        PropertyChangedEventHandler changed = (_, args) =>
        {
            if (args.PropertyName != nameof(tab.FilePath)) return;
            tab.Document.Insert(tab.Document.TextLength, " later");
            edited = true;
        };
        tab.PropertyChanged += changed;
        try { await service.SaveAsync(tab, path); }
        finally { tab.PropertyChanged -= changed; }
        Assert.True(edited);
    }

    [Fact]
    public Task LargeFilesOpenInEditMode() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("large.md");
        await File.WriteAllTextAsync(path, new string('a', (int)DocumentFileService.LargeFileThreshold + 1));
        var tab = await new DocumentFileService().OpenAsync(path);
        Assert.True(tab.IsLargeFile);
        Assert.False(tab.IsPreviewMode);
    });

    [Fact]
    public Task ImageImportCopiesWithoutChangingSourceAndReturnsRelativePath() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var sourcePath = directory.FilePath("original.png");
        var bytes = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        await File.WriteAllBytesAsync(sourcePath, bytes);
        var relativePath = await new DocumentFileService().ImportImageAsync(sourcePath, directory.FilePath("doc.md"));
        Assert.StartsWith("images/image-", relativePath);
        Assert.False(Path.IsPathRooted(relativePath));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(directory.FilePath(relativePath)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(sourcePath));
    });
}
