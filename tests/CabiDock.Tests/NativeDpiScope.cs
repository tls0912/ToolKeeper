using System.Runtime.InteropServices;
using Xunit;

namespace CabiDock.Tests;

/// <summary>Keep synthetic native geometry in physical pixels, independently of WPF startup.</summary>
internal sealed class NativeDpiScope : IDisposable
{
    private readonly int _threadId = Environment.CurrentManagedThreadId;
    private readonly nint _previous;
    private bool _disposed;

    public NativeDpiScope()
    {
        _previous = SetThreadDpiAwarenessContext(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        Assert.NotEqual(nint.Zero, _previous);
    }

    public void Dispose()
    {
        if (_disposed) return;
        // Thread-local DPI must never be held across an await that can change threads.
        Assert.Equal(_threadId, Environment.CurrentManagedThreadId);
        Assert.NotEqual(nint.Zero, SetThreadDpiAwarenessContext(_previous));
        _disposed = true;
    }

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
}
