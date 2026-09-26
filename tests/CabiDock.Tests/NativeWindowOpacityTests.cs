using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CabiDock.Desktop;
using Xunit;

namespace CabiDock.Tests;

public sealed class NativeWindowOpacityTests
{
    [Fact]
    public async Task ChildWindowCanChangeOpacityAndReturnToOpaqueWithoutClickThrough()
    {
        // VSTest's testhost does not carry our Windows 8+ compatibility declaration.
        // Use the real apphost manifest; the diagnostic creates only hidden owned windows.
        var reportPath = Path.Combine(Path.GetTempPath(), $"CabiDock-opacity-{Guid.NewGuid():N}.json");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "CabiDock.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { "--diagnose-group-opacity", reportPath }
            }
        };
        var started = false;
        try
        {
            started = process.Start();
            Assert.True(started);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            var report = JsonSerializer.Deserialize<NativeWindowOpacityReport>(File.ReadAllText(reportPath));
            Assert.NotNull(report);
            Assert.True(report.Succeeded, report.Message);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(new[] { 0.5, 0.3, 1.0 }, report.Samples.Select(sample => sample.Opacity));
            Assert.Equal(new byte?[] { 128, 76, null }, report.Samples.Select(sample => sample.Alpha));
            Assert.Equal(new[] { true, true, false }, report.Samples.Select(sample => sample.Layered));
            Assert.Equal(new uint[] { 2, 2, 0 }, report.Samples.Select(sample => sample.Flags));
            Assert.All(report.Samples, sample =>
            {
                Assert.True(sample.ChildParentRetained);
                Assert.False(sample.ClickThrough);
            });
        }
        finally
        {
            if (started && !process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            if (File.Exists(reportPath)) File.Delete(reportPath);
        }
    }

    [Fact]
    public Task InvalidOpacityLeavesWindowStylesUnchanged() => OnSta(() =>
    {
        var window = new Window { ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var style = NativeDesktop.ReadStyle(handle, -20);
            foreach (var opacity in new[] { double.NaN, double.PositiveInfinity, 0, 0.29, 1.01 })
            {
                Assert.False(NativeWindowOpacity.TrySet(handle, opacity, out _));
                Assert.Equal(style, NativeDesktop.ReadStyle(handle, -20));
            }
        }
        finally { window.Close(); }
    });

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

}
