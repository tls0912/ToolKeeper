using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ToolKeeper.Services;
using ToolKeeper.Modules;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class PlatformLifecycleTests
{
    [Fact]
    public void DefaultOptionsPreserveLegacyDesktopDataLocation()
    {
        var options = StartupOptions.Parse([]);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper");
        Assert.Equal(Path.Combine(root, "CabiDock"), options.DesktopDataDirectory);
        Assert.Equal(Path.Combine(root, "ToolKeeper"), options.PlatformDataDirectory);
        Assert.Null(options.DesktopRoots);
    }

    [Fact]
    public void CustomDataDirectoryIsolatesBothHostAndDesktopPreferences()
    {
        var path = Path.GetFullPath("artifacts/host-options-test");
        var options = StartupOptions.Parse(["--data-directory", path, "--desktop-directory", path]);
        Assert.Equal(path, options.DesktopDataDirectory);
        Assert.Equal(Path.Combine(path, "toolkeeper-host"), options.PlatformDataDirectory);
        Assert.Equal(path, Assert.Single(options.DesktopRoots!));
    }

    [Theory]
    [InlineData("--unknown", "value")]
    [InlineData("--data-directory", "")]
    [InlineData("--desktop-directory", "--data-directory")]
    public void InvalidOptionsNeverFallThroughToDesktopStartup(string key, string value) =>
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse([key, value]));

    [Fact]
    public void RejectsDuplicateAndAmbiguousOptions()
    {
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--data-directory"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--data-directory", "a", "--data-directory", "b"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--diagnose-desktop", "a", "--diagnose-group-opacity", "b"]));
    }

    [Fact]
    public void DesktopPreferenceSurvivesRestartWithoutChangingLegacyFiles()
    {
        using var fixture = new Fixture();
        var existing = Path.Combine(fixture.Directory, "state.json");
        File.WriteAllText(existing, "legacy classification data");
        var store = new DesktopPreferencesStore(fixture.Directory);
        Assert.True(store.Current.DesktopEnabled);
        store.SetEnabled(false);
        Assert.Null(store.Warning);
        Assert.False(new DesktopPreferencesStore(fixture.Directory).Current.DesktopEnabled);
        Assert.Equal("legacy classification data", File.ReadAllText(existing));
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{}")]
    [InlineData("{\"desktopEnable\":false}")]
    public void CorruptPreferenceStaysPreservedAndDoesNotEnableTakeover(string invalid)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Directory, "platform.json");
        File.WriteAllText(path, invalid);
        var store = new DesktopPreferencesStore(fixture.Directory);
        Assert.False(store.Current.DesktopEnabled);
        Assert.NotNull(store.Warning);
        store.SetEnabled(true);
        Assert.NotNull(store.Warning);
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void ATemporarySaveFailureIsRetriedEvenWhenTheInMemoryValueAlreadyMatches()
    {
        using var fixture = new Fixture();
        var store = new DesktopPreferencesStore(fixture.Directory);
        var blockedPath = Path.Combine(fixture.Directory, "platform.json");
        Directory.CreateDirectory(blockedPath);
        store.SetEnabled(false);
        Assert.NotNull(store.Warning);
        Assert.False(store.Current.DesktopEnabled);
        Directory.Delete(blockedPath);
        store.RetrySave();
        Assert.Null(store.Warning);
        Assert.False(new DesktopPreferencesStore(fixture.Directory).Current.DesktopEnabled);
    }

    [Fact]
    public Task CloseToTrayPreservesTheLauncherAndDoesNotInterruptAnIndependentModule() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var file = Path.Combine(fixture.Directory, "test.txt");
        File.WriteAllText(file, "abc");
        var window = new MainWindow { PreferencesPath = null, ShowInTaskbar = false, ShowActivated = false };
        var hash = new HashCheckerWindow { PreferencesPath = null, ShowInTaskbar = false, ShowActivated = false };
        using var lifetime = new HostWindowLifetime(window);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        try
        {
            window.Close();
            Assert.Equal(0, closed);
            Assert.False(lifetime.IsExiting);
            await hash.LoadHashFileAsync(file);
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", ((TextBox)hash.FindName("Md5Output")).Text.ToLowerInvariant());
            lifetime.PrepareExit();
            lifetime.PrepareExit();
            window.Close();
            Assert.Equal(1, closed);
            Assert.True(lifetime.IsExiting);
        }
        finally { lifetime.PrepareExit(); if (closed == 0) window.Close(); hash.Close(); }
    });

    private static Task OnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception exception) { completion.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ToolKeeper.HostTests", Guid.NewGuid().ToString("N"));
        public Fixture() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
