using System.ComponentModel;
using System.Windows;

namespace ToolKeeper;

/// <summary>The host owns close-to-tray. Detached views and the desktop module do not own application exit.</summary>
internal sealed class HostWindowLifetime : IDisposable
{
    private readonly Window _window;
    private bool _exiting;
    public bool IsExiting => _exiting;

    public HostWindowLifetime(Window window)
    {
        _window = window;
        window.Closing += Closing;
        window.StateChanged += StateChanged;
    }

    public void PrepareExit() => _exiting = true;

    public void Show()
    {
        if (_exiting) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Closing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        _window.Hide();
    }

    private void StateChanged(object? sender, EventArgs e)
    {
        if (!_exiting && _window.WindowState == WindowState.Minimized) _window.Hide();
    }

    public void Dispose()
    {
        PrepareExit();
        _window.Closing -= Closing;
        _window.StateChanged -= StateChanged;
    }
}
