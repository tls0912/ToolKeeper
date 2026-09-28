using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace ToolKeeper.Services;

public sealed record FileHashResult(string FilePath, long Length, string Md5, string Sha1, string Sha256);

public static class FileHashService
{
    private const int BufferSize = 128 * 1024;

    /// <summary>
    /// Computes all hashes in a single pass on a worker thread. Progress is a fraction from 0 to 1.
    /// The file is shared for reading only, so Windows rejects concurrent writers and deletion.
    /// </summary>
    public static Task<FileHashResult> ComputeAsync(
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            await using var stream = new FileStream(fullPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = BufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

            long length = stream.Length;
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(fullPath);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            try
            {
                progress?.Report(0);
                long bytesRead = 0;
                double lastReportedProgress = 0;
                long lastProgressTimestamp = Stopwatch.GetTimestamp();
                int count;
                while ((count = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                           .ConfigureAwait(false)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    md5.AppendData(buffer, 0, count);
                    sha1.AppendData(buffer, 0, count);
                    sha256.AppendData(buffer, 0, count);
                    bytesRead += count;

                    // Reserve 100% for a completed, validated result.
                    if (length > 0 && bytesRead < length)
                    {
                        double fraction = (double)bytesRead / length;
                        // Avoid flooding the dispatcher when a large file is read quickly.
                        if (lastReportedProgress == 0 || fraction - lastReportedProgress >= 0.01 ||
                            Stopwatch.GetElapsedTime(lastProgressTimestamp).TotalMilliseconds >= 100)
                        {
                            progress?.Report(fraction);
                            lastReportedProgress = fraction;
                            lastProgressTimestamp = Stopwatch.GetTimestamp();
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (bytesRead != length || stream.Length != length ||
                    File.GetLastWriteTimeUtc(fullPath) != lastWriteTime)
                {
                    throw new IOException("檔案在計算 HASH 時已變更，請等候檔案寫入完成後重試。");
                }

                var result = new FileHashResult(fullPath, length,
                    Convert.ToHexString(md5.GetHashAndReset()),
                    Convert.ToHexString(sha1.GetHashAndReset()),
                    Convert.ToHexString(sha256.GetHashAndReset()));
                progress?.Report(1);
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }, cancellationToken);
    }

    public static string? MatchAlgorithm(FileHashResult result, string expected)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!IsValidExpectedHash(expected))
        {
            return null;
        }

        string normalized = expected.Trim();
        return normalized.Length switch
        {
            32 when string.Equals(result.Md5, normalized, StringComparison.OrdinalIgnoreCase) => "MD5",
            40 when string.Equals(result.Sha1, normalized, StringComparison.OrdinalIgnoreCase) => "SHA-1",
            64 when string.Equals(result.Sha256, normalized, StringComparison.OrdinalIgnoreCase) => "SHA-256",
            _ => null
        };
    }

    public static bool IsValidExpectedHash(string expected)
    {
        if (expected is null)
        {
            return false;
        }

        ReadOnlySpan<char> value = expected.AsSpan().Trim();
        if (value.Length is not (32 or 40 or 64))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
