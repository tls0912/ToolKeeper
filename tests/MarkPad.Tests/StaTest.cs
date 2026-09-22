using System.IO;
using System.Windows.Threading;

namespace MarkPad.Tests;

internal static class StaTest
{
    public static Task Run(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "MarkPad test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}

internal sealed class TestDirectory : IDisposable
{
    private static readonly string TestRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MarkPad.Tests"));
    public string PathName { get; } = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));

    public TestDirectory() => Directory.CreateDirectory(PathName);
    public string FilePath(string name) => Path.Combine(PathName, name);

    public void Dispose()
    {
        var fullPath = Path.GetFullPath(PathName);
        if (!fullPath.StartsWith(TestRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup escaped the test directory.");
        if (!Directory.Exists(fullPath)) return;
        foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(fullPath, true);
    }
}
