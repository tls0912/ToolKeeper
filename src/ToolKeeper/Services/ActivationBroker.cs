using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace ToolKeeper.Services;

/// <summary>Routes URI requests from short-lived secondary invocations to the owning host.</summary>
internal sealed class ActivationBroker : IDisposable
{
    private const int MaximumMessageBytes = 512;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    private bool _disposed;

    public ActivationBroker(string dataDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).ToUpperInvariant();
        // Include session: the desktop ownership mutex is Local\ and the same user can have multiple sessions.
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var identity = $"{process.SessionId}:{path}";
        _pipeName = "ToolKeeper.Activation." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }

    public void Start(Func<string?, CancellationToken, Task<bool>> activate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activate);
        if (_listener is not null) return;
        _listener = Task.Run(() => ListenAsync(activate, _stop.Token));
    }

    private async Task ListenAsync(Func<string?, CancellationToken, Task<bool>> activate, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var header = new byte[4];
                await pipe.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                var size = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (size < 0 || size > MaximumMessageBytes)
                {
                    await pipe.WriteAsync(new byte[] { 0 }, timeout.Token).ConfigureAwait(false);
                    continue;
                }
                var payload = new byte[size];
                await pipe.ReadExactlyAsync(payload, timeout.Token).ConfigureAwait(false);
                var message = Utf8.GetString(payload);
                var valid = message.Length == 0 || ToolActivationUri.TryParse(message, out _);
                var accepted = false;
                if (valid)
                {
                    try
                    {
                        accepted = await activate(message.Length == 0 ? null : message, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    // A failed module activation must not take down the host's activation channel.
                    catch (OperationCanceledException) { throw; }
                    catch (Exception error) when (error is not OutOfMemoryException) { }
                }
                await pipe.WriteAsync(new byte[] { accepted ? (byte)1 : (byte)0 }, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { if (stop.IsCancellationRequested) break; }
            catch (Exception error) when (error is IOException or DecoderFallbackException or ObjectDisposedException or UnauthorizedAccessException)
            {
                if (stop.IsCancellationRequested) break;
                // In particular, don't spin if Windows temporarily refuses to create the pipe.
                try { await Task.Delay(100, stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async Task<bool> ForwardAsync(string? activationUri, CancellationToken cancellationToken = default)
    {
        if (activationUri is not null && !ToolActivationUri.TryParse(activationUri, out _)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var payload = Utf8.GetBytes(activationUri ?? "");
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await pipe.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            var ack = new byte[1];
            await pipe.ReadExactlyAsync(ack, timeout.Token).ConfigureAwait(false);
            return ack[0] == 1;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        if (_listener is null) _stop.Dispose();
        else _ = _listener.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
