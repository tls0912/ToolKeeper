using System.Windows;
using System.IO;
using ToolKeeper.UI;
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
    public Task ExplicitExitClosesEveryLiveModuleAndRejectsNewActivations() => OnSta(() =>
    {
        var closed = 0;
        var manager = new ModuleWindowManager(_ => { });
        foreach (var id in new[] { "004", "005" })
            manager.Open(id, () => { var window = new Window(); window.Closed += (_, _) => closed++; return window; });
        manager.Dispose();
        manager.Dispose();
        Assert.Equal(2, closed);
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
