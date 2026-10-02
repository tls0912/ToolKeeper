using System.Windows;
using System.IO;
using ToolKeeper.UI;
using ToolKeeper.Services;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class ModuleWindowManagerTests
{
    [Fact]
    public Task UriColdStartUsesSavedPreferencesWithoutShowingTheHomeWindow() => OnSta(() =>
    {
        var path = Path.GetTempFileName();
        try
        {
            new AppWindowPreferences("InkDark", "ja").Save(path);
            var home = PlatformController.CreateHomeWindow(path);
            try
            {
                Assert.False(home.IsVisible);
                Assert.Equal("InkDark", home.SelectedTheme);
                Assert.Equal("ja", home.SelectedLanguage);
                Assert.Equal("ja", home.ResolvedLanguage);
                Assert.Equal("内蔵モジュールとアプリ", home.Resources["ToolKeeper.ToolsCatalog"]);
            }
            finally { home.Close(); }
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public Task RepeatedActivationPreservesEachModulesWindowAndWork() => OnSta(() =>
    {
        var shown = new List<Window>();
        using var manager = new ModuleWindowManager(shown.Add);
        var hash = manager.Open("004", () => new Window { Tag = "hash in progress" });
        var ico = manager.Open("005", () => new Window { Tag = "image batch" });
        Assert.Same(hash, manager.Open("004", () => throw new InvalidOperationException("Must reuse window")));
        Assert.Equal("hash in progress", hash.Tag);
        Assert.Equal("image batch", ico.Tag);
        Assert.Equal(new[] { hash, ico, hash }, shown);
        hash.Close();
        Assert.Same(ico, manager.Open("005", () => throw new InvalidOperationException("Unrelated module must survive")));
        Assert.NotSame(hash, manager.Open("004", () => new Window()));
    });

    [Fact]
    public Task HistoLensRoutesWithinTheHostReusesItsWindowAndClosesOnExit() => OnSta(() =>
    {
        var profile = Path.Combine(Path.GetTempPath(), "ToolKeeper.HistoLensHostTests", Guid.NewGuid().ToString("N"));
        var options = StartupOptions.Parse(["--data-directory", profile, "--activate", "toolkeeper://run/006"]);
        var expectedPreferences = Path.Combine(options.PlatformDataDirectory, "HistoLens", "ui.json");
        var shown = new List<Window>();
        var launches = new List<System.Diagnostics.ProcessStartInfo>();
        using var manager = new ModuleWindowManager(shown.Add);
        var created = 0;
        var closed = 0;
        try
        {
            var environment = new ProductTestEnvironment();
            var launcher = new ProductLauncherService(environment.Catalog, launches.Add, id =>
            {
                Assert.Equal("006", id);
                manager.Open(id, () =>
                {
                    created++;
                    var window = PlatformController.CreateHistoLensWindow(options.PlatformDataDirectory);
                    window.Closed += (_, _) => closed++;
                    Assert.Equal(expectedPreferences, window.PreferencesPath);
                    return window;
                });
            });
            Assert.True(ToolActivationUri.TryParse(options.ActivationUri, out var productId));
            Assert.True(launcher.Launch(productId).Succeeded);
            var first = Assert.IsType<HistoLens.MainWindow>(Assert.Single(shown));
            first.Tag = "preserved research state";

            Assert.True(launcher.Launch(productId).Succeeded);
            Assert.Same(first, shown[1]);
            Assert.Equal("preserved research state", shown[1].Tag);
            Assert.Equal(1, created);
            Assert.Empty(launches);
            Assert.Empty(environment.FileQueries);
            Assert.Empty(environment.ProtocolQueries);

            first.Close();
            Assert.Equal(1, closed);
            Assert.True(launcher.Launch(productId).Succeeded);
            Assert.NotSame(first, shown[2]);
            Assert.Equal(2, created);
            manager.Dispose();
            Assert.Equal(2, closed);
            Assert.False(launcher.Launch(productId).Succeeded);
            Assert.Empty(launches);
        }
        finally
        {
            manager.Dispose();
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
    });

    [Fact]
    public Task ExplicitExitClosesEveryLiveModuleAndRejectsNewActivations() => OnSta(() =>
    {
        var closed = 0;
        var manager = new ModuleWindowManager(_ => { });
        foreach (var id in new[] { "004", "005", "006" })
            manager.Open(id, () => { var window = new Window(); window.Closed += (_, _) => closed++; return window; });
        manager.Dispose();
        manager.Dispose();
        Assert.Equal(3, closed);
        Assert.Throws<ObjectDisposedException>(() => manager.Open("004", () => new Window()));
    });

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
