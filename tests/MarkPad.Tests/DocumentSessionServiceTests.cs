using System.IO;
using System.Text.Json;
using MarkPad.Models;
using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class DocumentSessionServiceTests
{
    [Fact]
    public void ExistingSettingsDoNotEnableSessionRestore()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.FilePath("settings.json"), "{\"AutoSave\":false}");
        var preferences = new SettingsService(directory.PathName);
        Assert.False(preferences.Settings.RememberOpenFiles);
        Assert.Empty(DocumentSessionService.FilesToRestore(preferences.Settings));
    }

    [Fact]
    public void ClosingRemembersSavedPathsAndActiveFileWithoutChangingDirtyOrUnsavedContent()
    {
        using var directory = new TestDirectory();
        var firstPath = directory.FilePath("first.md");
        var secondPath = directory.FilePath("second.md");
        File.WriteAllText(firstPath, "saved first");
        File.WriteAllText(secondPath, "saved second");
        var first = new DocumentTab { FilePath = firstPath, Content = "pending first", IsDirty = true };
        var second = new DocumentTab { FilePath = secondPath, Content = "saved second", IsDirty = false };
        var unsaved = new DocumentTab { Content = "new unsaved document", IsDirty = true };
        var preferences = EnabledPreferences(directory);

        new DocumentSessionService().RememberClosingWindow(preferences, [first, unsaved, second], [], firstPath);

        var restored = new SettingsService(directory.PathName).Settings;
        Assert.True(restored.RememberOpenFiles);
        Assert.Equal(new[] { firstPath, secondPath }, restored.SessionFiles);
        Assert.Equal(firstPath, restored.SessionActiveFile);
        Assert.Equal(new[] { firstPath, secondPath }, DocumentSessionService.FilesToRestore(restored));
        Assert.Equal("saved first", File.ReadAllText(firstPath));
        Assert.Equal("pending first", first.Content);
        Assert.True(first.IsDirty);
        Assert.True(unsaved.IsDirty);
        Assert.Equal("new unsaved document", unsaved.Content);
        Assert.False(second.IsDirty);
        var serialized = File.ReadAllText(directory.FilePath("settings.json"));
        Assert.DoesNotContain("pending first", serialized);
        Assert.DoesNotContain("new unsaved document", serialized);
    }

    [Fact]
    public void MultipleWindowClosesKeepEarlierWindowsAndUseTheCurrentRemainingTabs()
    {
        using var directory = new TestDirectory();
        var first = new DocumentTab { FilePath = directory.FilePath("first.md") };
        var second = new DocumentTab { FilePath = directory.FilePath("second.md") };
        var removed = new DocumentTab { FilePath = directory.FilePath("removed.md") };
        var preferences = EnabledPreferences(directory);
        var session = new DocumentSessionService();

        session.RememberClosingWindow(preferences, [first], [second, removed], first.FilePath);
        Assert.Equal(new[] { first.FilePath, second.FilePath, removed.FilePath }, preferences.Settings.SessionFiles);
        session.RememberClosingWindow(preferences, [second], [], second.FilePath);

        var restored = new SettingsService(directory.PathName).Settings;
        Assert.Equal(new[] { first.FilePath, second.FilePath }, restored.SessionFiles);
        Assert.Equal(second.FilePath, restored.SessionActiveFile);
    }

    [Fact]
    public void StartupSkipsMissingFilesAndDirectoriesAndNormalizesCorruptPaths()
    {
        using var directory = new TestDirectory();
        var existing = directory.FilePath("existing.md");
        var missing = directory.FilePath("missing.md");
        File.WriteAllText(existing, "# Exists");
        var input = new AppSettings
        {
            RememberOpenFiles = true,
            SessionFiles = [existing, existing.ToUpperInvariant(), missing, directory.PathName, "relative.md", "", "C:\\bad\0path", null!],
            SessionActiveFile = existing.ToUpperInvariant()
        };
        File.WriteAllText(directory.FilePath("settings.json"), JsonSerializer.Serialize(input));

        var preferences = new SettingsService(directory.PathName);
        Assert.Equal(new[] { existing, missing, directory.PathName }, preferences.Settings.SessionFiles);
        Assert.Equal(existing, preferences.Settings.SessionActiveFile);
        Assert.Equal(new[] { existing }, DocumentSessionService.FilesToRestore(preferences.Settings));
    }

    [Fact]
    public void ActiveFileMustBelongToTheRememberedSession()
    {
        using var directory = new TestDirectory();
        var preferences = EnabledPreferences(directory);
        var document = new DocumentTab { FilePath = directory.FilePath("open.md") };
        new DocumentSessionService().RememberClosingWindow(preferences, [document], [], directory.FilePath("other.md"));
        Assert.Null(new SettingsService(directory.PathName).Settings.SessionActiveFile);
    }

    [Fact]
    public void DisablingAndReenablingStartsANewSessionWithoutEarlierClosedWindows()
    {
        using var directory = new TestDirectory();
        var preferences = EnabledPreferences(directory);
        var previous = new DocumentTab { FilePath = directory.FilePath("previous.md") };
        var current = new DocumentTab { FilePath = directory.FilePath("current.md") };
        var session = new DocumentSessionService();
        session.RememberClosingWindow(preferences, [previous], [], previous.FilePath);

        preferences.Settings.RememberOpenFiles = false;
        session.Reset();
        preferences.Save();
        Assert.Empty(preferences.Settings.SessionFiles);
        Assert.Null(preferences.Settings.SessionActiveFile);
        Assert.Empty(DocumentSessionService.FilesToRestore(preferences.Settings));
        preferences.Settings.RememberOpenFiles = true;
        session.RememberClosingWindow(preferences, [current], [], current.FilePath);

        Assert.Equal(new[] { current.FilePath }, new SettingsService(directory.PathName).Settings.SessionFiles);
    }

    [Fact]
    public void NullSerializedSessionFieldsRemainUsable()
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.FilePath("settings.json"),
            "{\"RememberOpenFiles\":true,\"SessionFiles\":null,\"SessionActiveFile\":null}");
        var preferences = new SettingsService(directory.PathName);
        preferences.Save();
        Assert.Empty(preferences.Settings.SessionFiles);
        Assert.Empty(DocumentSessionService.FilesToRestore(preferences.Settings));
        Assert.Null(preferences.Settings.SessionActiveFile);
    }

    private static SettingsService EnabledPreferences(TestDirectory directory)
    {
        var preferences = new SettingsService(directory.PathName);
        preferences.Settings.RememberOpenFiles = true;
        return preferences;
    }
}
