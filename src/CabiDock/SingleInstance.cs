using System.Security.Cryptography;
using System.Text;

namespace CabiDock;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private readonly CancellationTokenSource _stop = new();
    public bool IsPrimary { get; }

    public SingleInstance(string dataDirectory)
    {
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())))[..24];
        _mutex = new Mutex(false, @"Local\CabiDock." + identity);
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CabiDock.Show." + identity);
    }

    public void Signal() => _show.Set();

    public void Listen(Action show)
    {
        var token = _stop.Token;
        _ = Task.Run(() =>
        {
            WaitHandle[] handles = [_show, token.WaitHandle];
            while (WaitHandle.WaitAny(handles) == 0)
            {
                if (token.IsCancellationRequested) break;
                show();
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // The background listener may still be leaving WaitAny; process exit owns its handles.
    }
}
