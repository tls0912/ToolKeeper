using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace MarkPad.Services;

/// <summary>Forwards open requests between MarkPad processes belonging to the same Windows user.</summary>
public sealed class SingleInstanceService : IDisposable
{
    private const int MaximumPayloadBytes = 256 * 1024;
    private const int MaximumPathCount = 256;
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    private bool _disposed;

    public bool IsPrimary { get; }

    public SingleInstanceService(string name = "MarkPad")
    {
        var identity = WindowsIdentity.GetCurrent();
        using (identity)
        {
            var sid = identity.User?.Value ?? throw new InvalidOperationException("A Windows user identity is required.");
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name + ":" + sid)))[..24];
            _pipeName = "MarkPad.Open." + suffix;
            // A Windows Local mutex uses the creating user's default security descriptor.
            // Including the user SID also prevents collisions between signed-in users.
            _mutex = new Mutex(false, @"Local\MarkPad.Instance." + suffix, out var created);
            IsPrimary = created;
        }
    }

    public async Task<bool> ForwardAsync(string[] paths, bool newWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ValidPaths(paths)) return false;
        try { paths = paths.Select(Path.GetFullPath).ToArray(); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        var payload = JsonSerializer.SerializeToUtf8Bytes(new OpenRequest(Guid.NewGuid(), paths, newWindow));
        if (payload.Length > MaximumPayloadBytes) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        // The mutex can exist briefly before the primary process starts its listener.
        for (var attempt = 0; attempt < 4 && !timeout.IsCancellationRequested; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(900, timeout.Token).ConfigureAwait(false);
                var header = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
                await client.WriteAsync(header, timeout.Token).ConfigureAwait(false);
                await client.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
                await client.FlushAsync(timeout.Token).ConfigureAwait(false);
                var acknowledgment = new byte[1];
                await client.ReadExactlyAsync(acknowledgment, timeout.Token).ConfigureAwait(false);
                return acknowledgment[0] == 1;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                if (attempt == 3) return false;
            }
            catch (OperationCanceledException) { return false; }
            try { await Task.Delay(100, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    /// <summary>The callback runs off the UI thread. The caller dispatches it to WPF.</summary>
    public void StartListening(Action<string[], bool> callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        if (!IsPrimary) throw new InvalidOperationException("Only the primary instance can listen for file requests.");
        if (_listener is not null) throw new InvalidOperationException("The listener has already been started.");
        _listener = Task.Run(() => ListenAsync(callback, _shutdown.Token));
    }

    private async Task ListenAsync(Action<string[], bool> callback, CancellationToken shutdown)
    {
        HashSet<Guid> accepted = [];
        Queue<Guid> recentRequests = [];
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
                await server.WaitForConnectionAsync(shutdown).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var header = new byte[sizeof(int)];
                await server.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is <= 0 or > MaximumPayloadBytes) continue;
                var payload = new byte[length];
                await server.ReadExactlyAsync(payload, timeout.Token).ConfigureAwait(false);
                var request = JsonSerializer.Deserialize<OpenRequest>(payload);
                if (request is null || request.Id == Guid.Empty || !ValidPaths(request.Paths)) continue;
                if (!accepted.Contains(request.Id))
                {
                    try { callback(request.Paths, request.NewWindow); }
                    catch (Exception ex) { LocalLog.Write(ex); continue; }
                    accepted.Add(request.Id);
                    recentRequests.Enqueue(request.Id);
                    if (recentRequests.Count > 64) accepted.Remove(recentRequests.Dequeue());
                }
                await server.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
                await server.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            {
                // A disconnected or malformed sender must not terminate the primary process.
                if (shutdown.IsCancellationRequested) break;
                try { await Task.Delay(50, shutdown).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static bool ValidPaths(string[]? paths) => paths is { Length: <= MaximumPathCount }
        && paths.All(path => path is not null && path.Length <= 32767 && !path.Contains('\0'));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        _mutex.Dispose();
        if (_listener is { } listener)
            _ = listener.ContinueWith(_ => _shutdown.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else _shutdown.Dispose();
    }

    private sealed record OpenRequest(Guid Id, string[] Paths, bool NewWindow);
}
