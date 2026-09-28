using System.Windows;

namespace ToolKeeper;

/// <summary>Keeps one live window per hosted module; closing a module never exits the platform.</summary>
internal sealed class ModuleWindowManager(Action<Window>? showWindow = null) : IDisposable
{
    private readonly Dictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly Action<Window> _show = showWindow ?? Show;
    private bool _disposed;

    public Window Open(string id, Func<Window> create)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_windows.TryGetValue(id, out var window))
        {
            window = create();
            _windows.Add(id, window);
            window.Closed += (_, _) => _windows.Remove(id);
        }
        _show(window);
        return window;
    }

    private static void Show(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
    }
}
