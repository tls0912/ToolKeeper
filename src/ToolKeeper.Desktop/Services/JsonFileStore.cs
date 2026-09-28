using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CabiDock.Services;

public sealed record LoadResult<T>(T Value, string? Error = null, bool RecoveredFromBackup = false, bool CanSave = true);
public sealed record SaveResult(bool Success, string? Error = null);

/// <summary>Local JSON persistence with same-directory replacement and a last-known-good backup.</summary>
public sealed class JsonFileStore<T> where T : class
{
    private readonly object _gate = new();
    private readonly Func<T, string?>? _validate;
    private bool _unrecoverableLoadFailure;

    public JsonFileStore(string path, Func<T, string?>? validate = null)
    {
        FilePath = Path.GetFullPath(path);
        _validate = validate;
    }

    public string FilePath { get; }
    public string BackupPath => FilePath + ".bak";

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public LoadResult<T> Load(Func<T> defaults)
    {
        lock (_gate)
        {
            _unrecoverableLoadFailure = false;
            string? error = null;
            try
            {
                return new LoadResult<T>(Read(FilePath));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // Only these errors establish absence. File.Exists also hides access failures.
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                error = $"無法讀取 {Path.GetFileName(FilePath)}：{exception.Message}";
            }

            try
            {
                return new LoadResult<T>(Read(BackupPath), error ?? "主要資料檔遺失，已載入備份。", RecoveredFromBackup: true);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                error = $"{error} 備份也無法讀取：{exception.Message}".Trim();
            }

            // Keep damaged files for recovery. The caller can still use defaults in memory.
            _unrecoverableLoadFailure = error is not null;
            return new LoadResult<T>(defaults(), error, CanSave: !_unrecoverableLoadFailure);
        }
    }

    public SaveResult Save(T value)
    {
        lock (_gate)
        {
            if (_unrecoverableLoadFailure)
                return new SaveResult(false, $"{Path.GetFileName(FilePath)} 與備份無法讀取，已保留原檔，暫停儲存。");

            string? temporaryPath = null;
            try
            {
                ArgumentNullException.ThrowIfNull(value);
                var validationError = _validate?.Invoke(value);
                if (validationError is not null)
                    return new SaveResult(false, validationError);

                // Serialize fully before touching either usable data file.
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           bufferSize: 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(FilePath))
                {
                    var validPrimary = true;
                    try
                    {
                        Read(FilePath);
                    }
                    catch (JsonException)
                    {
                        validPrimary = false;
                    }

                    // Never replace a healthy backup with a damaged primary after recovery.
                    File.Replace(temporaryPath, FilePath, validPrimary ? BackupPath : null);
                }
                else
                {
                    File.Move(temporaryPath, FilePath);
                }
                temporaryPath = null;
                return new SaveResult(true);
            }
            catch (Exception exception) when (IsStorageException(exception) || exception is ArgumentException)
            {
                return new SaveResult(false, $"無法儲存 {Path.GetFileName(FilePath)}：{exception.Message}");
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception exception) when (IsStorageException(exception)) { }
                }
            }
        }
    }

    private T Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var value = JsonSerializer.Deserialize<T>(stream, SerializerOptions)
            ?? throw new JsonException("資料內容不得為 null。");
        var validationError = _validate?.Invoke(value);
        if (validationError is not null)
            throw new JsonException(validationError);
        return value;
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or System.Security.SecurityException;
}
