using System.IO;

namespace ConvAnvil.Services;

public sealed record FileSnapshot(string Path, byte[] Bytes, EncodingDetection Detection);

public static class FileConversionService
{
    public const int MaxFileBytes = 16 * 1024 * 1024;

    public static async Task<FileSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        byte[] bytes;
        await using (var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (file.Length > MaxFileBytes)
            {
                throw new IOException("目前支援最大 16 MiB 的檔案；尚未讀取或修改檔案。");
            }

            using var memory = new MemoryStream((int)file.Length);
            var buffer = new byte[81920];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = MaxFileBytes - (int)memory.Length;
                // Read one extra byte to reject a file that grows while loading.
                var count = await file.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0) break;
                if (count > remaining)
                {
                    throw new IOException("讀取期間檔案超過 16 MiB；已停止讀取，原始檔案未修改。");
                }

                memory.Write(buffer, 0, count);
            }

            bytes = memory.ToArray();
        }

        var detection = await Task.Run(() => EncodingDetectionService.Detect(bytes), cancellationToken).ConfigureAwait(false);
        return new FileSnapshot(fullPath, bytes, detection);
    }

    public static async Task SaveNewAsync(string destination, byte[] bytes, string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(destination);
        if (sourcePath is not null && string.Equals(fullPath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("請另選新檔名；轉換不會覆寫來源檔案。");
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new IOException("目的地已存在；請另選新檔名，既有內容未修改。");
        }

        var directory = Path.GetDirectoryName(fullPath)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Same-directory rename publishes only a completed file. This also
            // refuses an existing destination created after the preflight check.
            File.Move(temporaryPath, fullPath, false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
