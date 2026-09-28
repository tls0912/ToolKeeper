using System.Windows;
using System.IO;
using System.Text.Json;
using CabiDock;
using CabiDock.Desktop;
using ToolKeeper.Services;

namespace ToolKeeper;

public partial class App : Application
{
    private SingleInstance? _instance;
    private PlatformController? _controller;
    private ActivationBroker? _activation;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            // The independent recovery process must never create a tray, window or instance lease.
            if (DesktopRecoveryGuard.TryRun(e.Args)) { Shutdown(); return; }
            var options = StartupOptions.Parse(e.Args);
            if (options.DiagnosticPath is not null)
            {
                if (options.DiagnoseOpacity)
                {
                    var report = NativeWindowOpacityDiagnostics.Capture();
                    File.WriteAllText(options.DiagnosticPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                    Shutdown(report.Succeeded ? 0 : 1);
                }
                else
                {
                    var report = DesktopProbe.CaptureDiagnostics();
                    File.WriteAllText(options.DiagnosticPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                    Shutdown();
                }
                return;
            }
            // Keep the legacy desktop identity so an older host cannot manage the same data together.
            _instance = new SingleInstance(options.DesktopDataDirectory);
            _activation = new ActivationBroker(options.DesktopDataDirectory);
            if (!_instance.IsPrimary)
            {
                var delivered = _activation.ForwardAsync(options.ActivationUri).GetAwaiter().GetResult();
                if (!delivered && options.ActivationUri is null) _instance.Signal();
                else if (!delivered)
                    MessageBox.Show("現有工具番尚未接收啟動連結。若仍在執行舊版 CabiDock，請先結束舊程式後重試。",
                        "ToolKeeper", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(delivered || options.ActivationUri is null ? 0 : 1);
                return;
            }
            PlatformIcon.InitializeHostedWindows();
            _controller = new PlatformController(this, options);
            _instance.Listen(() => Dispatcher.BeginInvoke(() => _controller?.ShowHome()));
            _controller.Start();
            _activation.Start((uri, cancellation) => Dispatcher.InvokeAsync(
                () => !cancellation.IsCancellationRequested && _controller?.ActivateUri(uri) == true,
                System.Windows.Threading.DispatcherPriority.Normal, cancellation).Task);
            // Isolated directory runs and diagnostic/helper modes never register a system entry.
            if (options.RegisterProtocol) _controller.SetProtocolError(ToolKeeperProtocolRegistration.TryRegister());
        }
        catch (Exception ex)
        {
            _controller?.PrepareExit();
            MessageBox.Show("ToolKeeper 無法啟動。\n\n" + ex.Message, "ToolKeeper", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _activation?.Dispose();
        _controller?.PrepareExit();
        base.OnSessionEnding(e);
        e.Cancel = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _activation?.Dispose(); _controller?.Dispose(); }
        finally
        {
            _instance?.Dispose();
            base.OnExit(e);
        }
    }
}
