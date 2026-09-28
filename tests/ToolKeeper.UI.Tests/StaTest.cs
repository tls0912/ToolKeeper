using System.Windows.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ToolKeeper.UI.Tests;

internal static class StaTest
{
    public static Task Run(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { action(); completion.SetResult(); }
                catch (Exception exception) { completion.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "ToolKeeper.UI test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
