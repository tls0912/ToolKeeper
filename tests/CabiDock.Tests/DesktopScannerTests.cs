using System.IO;
using CabiDock.Models;
using CabiDock.Services;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CabiDock.ScannerTests", Guid.NewGuid().ToString("N"));

    public DesktopScannerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MissingOneKnownFolderCannotBeTreatedAsACompleteDesktop()
    {
        var roots = DesktopScanner.ResolveRoots(folder => folder == Environment.SpecialFolder.DesktopDirectory ? "" : _root);
        var scan = new DesktopScanner().Scan(roots);
        Assert.Empty(roots);
        Assert.False(scan.Succeeded);
        var state = new CabiDockState();
        var configuration = ConfigurationService.LoadDefaults();
        var classification = new ClassificationService();
        classification.AssignManually(new DesktopItem(Path.Combine(_root, "notes.md"), false), "archives", state, configuration);
        classification.Reconcile(scan.Items, state, configuration, scan.Succeeded);
        Assert.Equal("archives", Assert.Single(state.Items).CategoryId);
    }

    [Fact]
    public void ScanMergesBothDesktopsWithoutRecursingOrConfusingSameNames()
    {
        var user = Directory.CreateDirectory(Path.Combine(_root, "user")).FullName;
        var common = Directory.CreateDirectory(Path.Combine(_root, "public")).FullName;
        File.WriteAllText(Path.Combine(user, "notes.md"), "user");
        File.WriteAllText(Path.Combine(common, "notes.md"), "common");
        var directory = Directory.CreateDirectory(Path.Combine(user, "archive.zip")).FullName;
        File.WriteAllText(Path.Combine(directory, "nested.txt"), "nested");
        var hidden = Path.Combine(user, "desktop.ini");
        File.WriteAllText(hidden, "shell metadata");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        var result = new DesktopScanner().Scan([user, common]);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(2, result.Items.Count(item => item.Name == "notes.md"));
        Assert.True(Assert.Single(result.Items, item => item.Name == "archive.zip").IsDirectory);
        Assert.DoesNotContain(result.Items, item => item.Name is "nested.txt" or "desktop.ini");
    }

    [Fact]
    public void PartialScanFailureCannotDeleteSavedAssignments()
    {
        var path = Path.Combine(_root, "notes.md");
        File.WriteAllText(path, "user");
        var configuration = ConfigurationService.LoadDefaults();
        var state = new CabiDockState();
        var classification = new ClassificationService();
        var scanner = new DesktopScanner();
        var initial = scanner.Scan([_root]);
        classification.Reconcile(initial.Items, state, configuration);
        var failed = scanner.Scan([_root, Path.Combine(_root, "unavailable")]);

        Assert.False(failed.Succeeded);
        Assert.False(classification.Reconcile(failed.Items, state, configuration, failed.Succeeded));
        Assert.Equal(path, Assert.Single(state.Items).FullPath);
    }

    [Fact]
    public void RenameRetainsManualClassificationAndOriginalFileContents()
    {
        var oldPath = Path.Combine(_root, "notes.md");
        var newPath = Path.Combine(_root, "renamed.png");
        File.WriteAllText(oldPath, "unchanged contents");
        var scanner = new DesktopScanner();
        var configuration = ConfigurationService.LoadDefaults();
        var classification = new ClassificationService();
        var state = new CabiDockState();
        var first = Assert.Single(scanner.Scan([_root]).Items);
        Assert.NotNull(first.Identity);
        classification.AssignManually(first, "archives", state, configuration);
        File.Move(oldPath, newPath);

        classification.Reconcile(scanner.Scan([_root]).Items, state, configuration);

        var record = Assert.Single(state.Items);
        Assert.Equal(newPath, record.FullPath);
        Assert.Equal("archives", record.CategoryId);
        Assert.Equal(ClassificationSource.Manual, record.Source);
        Assert.Equal("unchanged contents", File.ReadAllText(newPath));
    }

    [Fact]
    public async Task WatcherReportsMoveOutAndReturnSeparately()
    {
        var desktop = Directory.CreateDirectory(Path.Combine(_root, "desktop")).FullName;
        var path = Path.Combine(desktop, "notes.md");
        var outside = Path.Combine(_root, "notes.md");
        File.WriteAllText(path, "user");
        var removed = new TaskCompletionSource<DesktopChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource<DesktopChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new DesktopWatcher();
        watcher.Changed += change =>
        {
            if (change.Kind == WatcherChangeTypes.Deleted) removed.TrySetResult(change);
            if (change.Kind == WatcherChangeTypes.Created) returned.TrySetResult(change);
        };
        watcher.Start([desktop]);
        File.Move(path, outside);
        var deletion = await removed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        File.Move(outside, path);
        var creation = await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(path, deletion.FullPath);
        Assert.Equal(path, creation.FullPath);
        Assert.False(watcher.NeedsRestart);
    }

    public void Dispose()
    {
        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }
}
