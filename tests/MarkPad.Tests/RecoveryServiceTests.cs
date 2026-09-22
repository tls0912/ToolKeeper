using System.IO;
using System.Text;
using MarkPad.Models;
using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class RecoveryServiceTests
{
    [Fact]
    public Task RecoveryRestoresUnsavedTextAndMetadataWithoutTouchingOriginal() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("document.md");
        await File.WriteAllTextAsync(path, "original\r\n", new UTF8Encoding(true));
        var original = await File.ReadAllBytesAsync(path);
        var fileService = new DocumentFileService();
        var tab = await fileService.OpenAsync(path);
        tab.Content = "unsaved\r\n中文";
        tab.CaretOffset = 5;
        tab.PreviewScroll = 240;
        tab.EditorScroll = 64;
        tab.IsPreviewMode = false;
        var recovery = new RecoveryService(directory.FilePath("recovery"));
        await recovery.SaveAsync([tab]);

        var recovered = Assert.Single(await recovery.RecoverAsync());
        Assert.Equal(tab.Id, recovered.Id);
        Assert.Equal(path, recovered.FilePath);
        Assert.Equal(tab.Content, recovered.Content);
        Assert.True(recovered.HasBom);
        Assert.Equal(65001, recovered.Encoding.CodePage);
        Assert.Equal(5, recovered.CaretOffset);
        Assert.Equal(240, recovered.PreviewScroll);
        Assert.Equal(64, recovered.EditorScroll);
        Assert.False(recovered.IsPreviewMode);
        Assert.True(recovered.IsDirty);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));

        await fileService.SaveAsync(recovered);
        Assert.Equal("unsaved\r\n中文", await File.ReadAllTextAsync(path));
        Assert.True((await File.ReadAllBytesAsync(path)).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
    });

    [Fact]
    public Task RecoveredFileRetainsOriginalFingerprintAndBlocksNewDiskChanges() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("changed.md");
        await File.WriteAllTextAsync(path, "original");
        var files = new DocumentFileService();
        var tab = await files.OpenAsync(path);
        tab.Content = "unsaved local";
        var recovery = new RecoveryService(directory.FilePath("recovery"));
        await recovery.SaveAsync([tab]);
        await File.WriteAllTextAsync(path, "external edit while app was closed");
        var recovered = Assert.Single(await recovery.RecoverAsync());
        await Assert.ThrowsAsync<FileConflictException>(() => files.SaveAsync(recovered));
        Assert.Equal("external edit while app was closed", await File.ReadAllTextAsync(path));
        Assert.Equal("unsaved local", recovered.Content);
    });

    [Fact]
    public Task RecoveryIncludesUntitledAndMissingDocumentsButExcludesCleanDocuments() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var missing = directory.FilePath("missing.md");
        var tab = new DocumentTab { Content = "untitled content", IsPreviewMode = false };
        var missingTab = new DocumentTab { FilePath = missing, Content = "retained" };
        var clean = new DocumentTab();
        var recovery = new RecoveryService(directory.FilePath("recovery"));
        await recovery.SaveAsync([tab, missingTab, clean]);
        var recovered = await recovery.RecoverAsync();
        Assert.Equal(2, recovered.Count);
        Assert.Contains(recovered, item => item.FilePath is null && item.Content == "untitled content");
        Assert.Contains(recovered, item => item.FilePath == missing && item.IsMissing && item.Content == "retained");
        Assert.False(File.Exists(missing));
    });

    [Fact]
    public Task DeletingSnapshotWhileSaveIsPendingDoesNotResurrectClosedTab() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var recovery = new RecoveryService(directory.FilePath("recovery"));
        var tab = new DocumentTab { Content = new string('a', 100_000) };
        var pending = recovery.SaveAsync([tab]);
        recovery.Delete(tab.Id);
        await pending;
        Assert.Empty(await recovery.RecoverAsync());

        // A later edit of a still-open, successfully saved tab may create a fresh snapshot.
        tab.Content = "a new unsaved change";
        await recovery.SaveAsync([tab]);
        Assert.Single(await recovery.RecoverAsync());
        tab.IsDirty = false;
        await recovery.SaveAsync([tab]);
        Assert.Empty(await recovery.RecoverAsync());
    });

    [Fact]
    public Task ClearRemovesOnlyRecoveryRecordsAndInvalidatesPendingSaves() => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var path = directory.FilePath("recovery");
        Directory.CreateDirectory(path);
        var unrelated = Path.Combine(path, "keep.recovery.json");
        await File.WriteAllTextAsync(unrelated, "do not delete me");
        var recovery = new RecoveryService(path);
        var tab = new DocumentTab { Content = "unsaved" };
        var pending = recovery.SaveAsync([tab]);
        recovery.Clear();
        await pending;
        Assert.Equal("do not delete me", await File.ReadAllTextAsync(unrelated));
        Assert.Single(Directory.EnumerateFiles(path));
    });
}
