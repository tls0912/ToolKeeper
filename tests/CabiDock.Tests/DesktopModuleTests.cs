using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Xunit;

namespace CabiDock.Tests;

[Collection("WPF layout bindings")]
public sealed class DesktopModuleTests
{
    [Fact]
    public Task ExitDiscardsThePendingScanAndPreventsRestartOrPreferenceChanges() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "CabiDock-module-" + Guid.NewGuid().ToString("N"));
        var desktop = Path.Combine(directory, "desktop");
        var data = Path.Combine(directory, "data");
        Directory.CreateDirectory(desktop);
        File.WriteAllText(Path.Combine(desktop, "notes.txt"), "synthetic desktop");
        try
        {
            var pendingScan = new TaskCompletionSource<DesktopScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var scans = 0;
            using var module = new DesktopModule(Dispatcher.CurrentDispatcher, data, [desktop], roots =>
            {
                Assert.Equal(new[] { desktop }, roots);
                scans++;
                return pendingScan.Task;
            });
            var changes = 0;
            module.StateChanged += () => changes++;
            module.SetEnabled(false);
            module.SetEnabled(false);
            Assert.False(module.Enabled);
            Assert.False(module.SupportsDesktop);
            Assert.Equal(1, changes);
            module.SetTools([new("001", "汗青", "已安裝", "開啟")], _ => Assert.Fail("No tool was activated."));
            module.Start();
            module.Start();
            Assert.Equal(1, scans);
            // The scan result is deliberately unavailable until exit has closed the module.
            Assert.True(Field<bool>(module, "_scanning"));
            module.PrepareExit();
            module.PrepareExit();
            module.SetEnabled(true);
            module.Start();
            module.ShowSettings();
            Assert.False(module.Enabled);
            Assert.Equal(1, changes);

            pendingScan.SetResult(new DesktopScanner().Scan([desktop]));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Field<bool>(module, "_scanning") && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.False(Field<bool>(module, "_scanning"));
            Assert.Equal(1, scans);
            Assert.False(File.Exists(Path.Combine(data, "state.json")));
            Assert.False(Field<DispatcherTimer>(module, "_refreshTimer").IsEnabled);
            Assert.False(Field<DispatcherTimer>(module, "_changeTimer").IsEnabled);
            Assert.False(Field<CabiDock.Views.SettingsWindow>(module, "_settings").IsVisible);
            module.Dispose();
            module.Dispose();
        }
        finally { Directory.Delete(directory, true); }
    });

    private static T Field<T>(DesktopModule module, string name) =>
        (T)typeof(DesktopModule).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;

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
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
