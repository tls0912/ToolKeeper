using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CabiDock.Desktop;

/// <summary>Arms an independent process before the desktop list view is clipped.</summary>
public sealed class DesktopRecoveryGuard : IDisposable
{
    private const string HelperArgument = "--desktop-recovery";
    private const int MaximumRegionBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);
    private readonly TargetIdentity _target;
    private readonly byte[]? _originalRegion;
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _guardian;
    private bool _disposed;

    private DesktopRecoveryGuard(TargetIdentity target, byte[]? originalRegion,
        NamedPipeServerStream pipe, Process guardian)
    {
        _target = target;
        _originalRegion = originalRegion;
        _pipe = pipe;
        _guardian = guardian;
    }

    public bool IsAlive
    {
        get
        {
            try { return !_disposed && _pipe.IsConnected && !_guardian.HasExited; }
            catch (InvalidOperationException) { return false; }
            catch (IOException) { return false; }
        }
    }

    public static bool TryStart(nint iconWindow, out DesktopRecoveryGuard? guard, out string reason)
        => TryStartCore(iconWindow, false, null, out guard, out reason);

    // Synthetic-window integration tests must not contend with a running desktop guardian.
    // The isolated lease is permitted only for a window owned by this exact parent process;
    // both the launching process and the independent helper enforce that restriction.
    internal static bool TryStartForOwnedWindow(nint iconWindow, string helperExecutablePath,
        out DesktopRecoveryGuard? guard, out string reason)
        => TryStartCore(iconWindow, true, helperExecutablePath, out guard, out reason);

    private static bool TryStartCore(nint iconWindow, bool useOwnedWindowLease, string? helperExecutablePath,
        out DesktopRecoveryGuard? guard, out string reason)
    {
        guard = null;
        reason = "";
        NamedPipeServerStream? pipe = null;
        Process? guardian = null;
        try
        {
            var target = TargetIdentity.Capture(iconWindow);
            using var parent = Process.GetCurrentProcess();
            var request = new RecoveryRequest("CabiDock-Recovery-" + Guid.NewGuid().ToString("N"),
                parent.Id, parent.StartTime.ToUniversalTime().Ticks, target, useOwnedWindowLease);
            if (!request.HasValidLeaseScope)
                throw new InvalidOperationException("獨立復原保護僅可用於本程序擁有的測試視窗。");
            pipe = new NamedPipeServerStream(request.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            guardian = Process.Start(CreateStartInfo(request, helperExecutablePath))
                ?? throw new InvalidOperationException("無法啟動桌面復原程序。");
            using var timeout = new CancellationTokenSource(StartupTimeout);
            pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var clientId)
                || clientId != guardian.Id)
                throw new InvalidOperationException("桌面復原程序的身分不符。");
            var ready = new byte[1];
            if (pipe.ReadAsync(ready, timeout.Token).AsTask().GetAwaiter().GetResult() != 1
                || ready[0] != 2 || !target.IsCurrent())
                throw new InvalidOperationException("先前的桌面接管尚未完成復原。");
            // The helper owns the recovery lease before we snapshot the original region.
            // This also prevents a restarted app from saving its crashed predecessor's clipping.
            var original = CaptureRegion(iconWindow);
            // HRGN values are process-local. Only transfer the original region's serialized data.
            pipe.WriteAsync(BitConverter.GetBytes(original?.Length ?? -1), timeout.Token).AsTask().GetAwaiter().GetResult();
            if (original is not null) pipe.WriteAsync(original, timeout.Token).AsTask().GetAwaiter().GetResult();
            pipe.FlushAsync(timeout.Token).GetAwaiter().GetResult();
            if (pipe.ReadAsync(ready, timeout.Token).AsTask().GetAwaiter().GetResult() != 1
                || ready[0] != 1 || guardian.HasExited || !target.IsCurrent())
                throw new InvalidOperationException("桌面復原程序尚未就緒。");
            guard = new DesktopRecoveryGuard(target, original, pipe, guardian);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No desktop mutation is permitted until this method has returned success.
            pipe?.Dispose();
            guardian?.Dispose();
            reason = "無法建立桌面復原保護：" + exception.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // The helper can stop only after a successful restore (or the exact target is gone).
            if (TryRestore(_target, _originalRegion))
            {
                try { _pipe.WriteByte(1); _pipe.Flush(); }
                catch (IOException) { }
                catch (InvalidOperationException) { }
            }
            // Otherwise EOF hands restoration to the helper, which keeps retrying independently.
        }
        finally
        {
            _pipe.Dispose();
            _guardian.Dispose();
        }
    }

    /// <summary>Handles the private helper mode synchronously, without creating application UI.</summary>
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != HelperArgument) return false;
        try
        {
            if (args.Length != 2) return true;
            var request = JsonSerializer.Deserialize<RecoveryRequest>(Convert.FromBase64String(args[1]));
            if (request is null || !request.PipeName.StartsWith("CabiDock-Recovery-", StringComparison.Ordinal)
                || !request.HasValidLeaseScope)
                return true;
            using var lease = new Mutex(false, request.LeaseName);
            var acquired = false;
            try
            {
                try { acquired = lease.WaitOne(StartupTimeout); }
                catch (AbandonedMutexException) { acquired = true; }
                if (acquired) RunHelperAsync(request).GetAwaiter().GetResult();
            }
            finally { if (acquired) lease.ReleaseMutex(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Invalid helper requests must never fall through into the normal application startup.
        }
        return true;
    }

    private static async Task RunHelperAsync(RecoveryRequest request)
    {
        using var parent = Process.GetProcessById(request.ParentId);
        if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTicks || parent.HasExited)
            return;
        using var pipe = new NamedPipeClientStream(".", request.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(StartupTimeout);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var serverId)
            || serverId != request.ParentId || !request.Target.IsCurrent()) return;
        await pipe.WriteAsync(new byte[] { 2 }, timeout.Token).ConfigureAwait(false);
        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
        var sizeBytes = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(sizeBytes, timeout.Token).ConfigureAwait(false);
        var size = BitConverter.ToInt32(sizeBytes);
        if (size < -1 || size == 0 || size > MaximumRegionBytes) return;
        byte[]? region = size == -1 ? null : new byte[size];
        if (region is not null)
        {
            await pipe.ReadExactlyAsync(region, timeout.Token).ConfigureAwait(false);
            // Validate that the snapshot can be recreated before authorizing any mutation.
            var check = ExtCreateRegion(0, (uint)region.Length, region);
            if (check == 0) return;
            DeleteObject(check);
        }
        if (!request.Target.IsCurrent() || parent.HasExited) return;

        // From READY onward, every exit path must restore the saved native region.
        var parentRestored = false;
        try
        {
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            var message = new byte[1];
            var read = pipe.ReadAsync(message).AsTask();
            var exited = parent.WaitForExitAsync();
            if (await Task.WhenAny(read, exited).ConfigureAwait(false) == read)
                parentRestored = await read.ConfigureAwait(false) == 1 && message[0] == 1;
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
        finally
        {
            if (!parentRestored)
                while (!TryRestore(request.Target, region))
                    await Task.Delay(250).ConfigureAwait(false);
        }
    }

    private static ProcessStartInfo CreateStartInfo(RecoveryRequest request, string? helperExecutablePath)
    {
        var start = CreateHostStartInfo(helperExecutablePath ?? Environment.ProcessPath,
            Assembly.GetEntryAssembly()?.Location);
        start.ArgumentList.Add(HelperArgument);
        start.ArgumentList.Add(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(request)));
        return start;
    }

    // The entry executable owns helper dispatch. This library is never an executable target.
    internal static ProcessStartInfo CreateHostStartInfo(string? currentHost, string? entryAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(currentHost)) throw new InvalidOperationException("無法識別桌面模組的宿主程式。");
        var usesDotnet = string.Equals(Path.GetFileNameWithoutExtension(currentHost), "dotnet", StringComparison.OrdinalIgnoreCase);
        if (usesDotnet && (string.IsNullOrWhiteSpace(entryAssemblyPath)
            || string.Equals(entryAssemblyPath, typeof(DesktopRecoveryGuard).Assembly.Location, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("無法識別桌面模組的可執行進入點。");
        var start = new ProcessStartInfo
        {
            FileName = currentHost,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(usesDotnet ? entryAssemblyPath : currentHost)!
        };
        if (usesDotnet) start.ArgumentList.Add(entryAssemblyPath!);
        return start;
    }

    private static byte[]? CaptureRegion(nint window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) throw new InvalidOperationException("無法讀取原始桌面區域。");
        try
        {
            // GetWindowRgn returns ERROR for a window with no explicit region.
            Marshal.SetLastPInvokeError(0);
            if (GetWindowRgn(window, region) == 0)
            {
                if (Marshal.GetLastPInvokeError() != 0)
                    throw new InvalidOperationException("無法讀取原始桌面區域。");
                return null;
            }
            var size = GetRegionData(region, 0, null);
            if (size == 0 || size > MaximumRegionBytes)
                throw new InvalidOperationException("原始桌面區域資料無效。");
            var data = new byte[size];
            if (GetRegionData(region, size, data) != size)
                throw new InvalidOperationException("無法保存原始桌面區域。");
            return data;
        }
        finally { DeleteObject(region); }
    }

    private static bool TryRestore(TargetIdentity target, byte[]? data)
    {
        // Explorer restart / HWND replacement must never redirect a restore to a different process.
        var identity = target.GetState();
        if (identity == IdentityState.Gone) return true;
        if (identity != IdentityState.Current) return false;
        var region = data is null ? 0 : ExtCreateRegion(0, (uint)data.Length, data);
        if (data is not null && region == 0) return false;
        if (SetWindowRgn((nint)target.Window, region, true) != 0) return true;
        // Windows owns HRGN only after SetWindowRgn succeeds.
        if (region != 0) DeleteObject(region);
        return target.GetState() == IdentityState.Gone;
    }

    private sealed record RecoveryRequest(string PipeName, int ParentId, long ParentStartTicks, TargetIdentity Target,
        bool UseOwnedWindowLease = false)
    {
        // A different lease can never be used to bypass the production desktop lease for
        // Explorer or any other external target. Repeated guards for the same test HWND
        // still share a lease, so an earlier restore completes before a new snapshot.
        [JsonIgnore] public bool HasValidLeaseScope => !UseOwnedWindowLease
            || (Target.ProcessId == ParentId && Target.ProcessStartTicks == ParentStartTicks);
        [JsonIgnore] public string LeaseName => UseOwnedWindowLease
            ? $@"Local\CabiDock.DesktopRecovery.OwnedWindow.{ParentId}.{ParentStartTicks:X}.{Target.Window:X}"
            : @"Local\CabiDock.DesktopRecovery";
    }

    private enum IdentityState { Current, Gone, Unverifiable }

    private sealed record TargetIdentity(long Window, uint ProcessId, long ProcessStartTicks)
    {
        internal static TargetIdentity Capture(nint window)
        {
            GetWindowThreadProcessId(window, out var processId);
            using var process = Process.GetProcessById(checked((int)processId));
            var identity = new TargetIdentity(window.ToInt64(), processId, process.StartTime.ToUniversalTime().Ticks);
            if (!identity.IsCurrent()) throw new InvalidOperationException("桌面圖示視窗的身分已變更。");
            return identity;
        }

        internal bool IsCurrent() => GetState() == IdentityState.Current;

        internal IdentityState GetState()
        {
            try
            {
                var window = (nint)Window;
                if (!IsWindow(window)) return IdentityState.Gone;
                GetWindowThreadProcessId(window, out var processId);
                if (processId != ProcessId) return IdentityState.Gone;
                var name = new StringBuilder(64);
                if (GetClassName(window, name, name.Capacity) == 0) return IdentityState.Unverifiable;
                if (name.ToString() != "SysListView32") return IdentityState.Gone;
                using var process = Process.GetProcessById(checked((int)processId));
                return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ProcessStartTicks
                    ? IdentityState.Current : IdentityState.Gone;
            }
            catch (ArgumentException) { return IdentityState.Gone; }
            catch (InvalidOperationException) { return IdentityState.Unverifiable; }
            catch (System.ComponentModel.Win32Exception) { return IdentityState.Unverifiable; }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint clientId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint serverId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int maximum);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern uint GetRegionData(nint region, uint size, [Out] byte[]? data);
    [DllImport("gdi32.dll")] private static extern nint ExtCreateRegion(nint transform, uint size, byte[] data);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
}
