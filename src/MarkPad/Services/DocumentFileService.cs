using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MarkPad.Models;

namespace MarkPad.Services;

public enum FileChangeStatus { Unchanged, Changed, Missing, ReadOnlyChanged }

public sealed class FileConflictException(string path)
    : IOException($"The file changed outside MarkPad. Reload it or save to another file: {path}")
{
    public string FilePath { get; } = path;
}

public sealed class DocumentFileService
{
    public const long LargeFileThreshold = 10 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveGates = new(StringComparer.OrdinalIgnoreCase);

    static DocumentFileService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<DocumentTab> OpenAsync(string path)
    {
        path = Path.GetFullPath(path);
        var loaded = await Task.Run(() => Read(path));
        var tab = new DocumentTab
        {
            FilePath = path,
            Content = loaded.Content,
            Encoding = loaded.Encoding,
            HasBom = loaded.HasBom,
            LastWriteTimeUtc = loaded.LastWriteTimeUtc,
            DiskHash = loaded.Hash,
            DiskLength = loaded.Length,
            IsReadOnly = loaded.IsReadOnly,
            IsLargeFile = loaded.Length > LargeFileThreshold,
            IsPreviewMode = loaded.IsReadOnly || loaded.Length <= LargeFileThreshold
        };
        tab.Document.UndoStack.ClearAll();
        tab.IsDirty = false;
        return tab;
    }

    public async Task SaveAsync(DocumentTab tab, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var explicitSaveAs = path is not null;
        path = Path.GetFullPath(path ?? tab.FilePath ?? throw new InvalidOperationException("Choose a file name before saving."));
        var gate = SaveGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var sameFile = string.Equals(tab.FilePath, path, StringComparison.OrdinalIgnoreCase);
            var content = tab.Content;
            var encoding = Encoding.GetEncoding(tab.Encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var hasBom = tab.HasBom;
            var expectedHash = sameFile ? tab.DiskHash : null;
            var result = await Task.Run(() =>
            {
                // Encode before touching disk. Unsupported characters must never turn into '?'.
                var body = encoding.GetBytes(content);
                var preamble = hasBom ? GetPreamble(encoding.CodePage) : [];
                var bytes = new byte[preamble.Length + body.Length];
                preamble.CopyTo(bytes, 0);
                body.CopyTo(bytes, preamble.Length);
                return SaveBytes(path, bytes, sameFile, expectedHash, explicitSaveAs);
            });
            tab.FilePath = path;
            tab.DiskHash = result.Hash;
            tab.DiskLength = result.Length;
            tab.LastWriteTimeUtc = result.LastWriteTimeUtc;
            tab.IsMissing = false;
            tab.IsReadOnly = false;
            tab.IsLargeFile = result.Length > LargeFileThreshold;
            // The user may continue typing while the write runs.
            tab.IsDirty = !string.Equals(tab.Content, content, StringComparison.Ordinal);
        }
        finally { gate.Release(); }
    }

    public FileChangeStatus RefreshStatus(DocumentTab tab)
    {
        if (tab.FilePath is null) return FileChangeStatus.Unchanged;
        var info = new FileInfo(tab.FilePath);
        if (!info.Exists)
        {
            tab.IsMissing = true;
            return FileChangeStatus.Missing;
        }
        tab.IsMissing = false;
        var readOnlyChanged = tab.IsReadOnly != info.IsReadOnly;
        tab.IsReadOnly = info.IsReadOnly;
        if (info.LastWriteTimeUtc != tab.LastWriteTimeUtc || info.Length != tab.DiskLength)
            return FileChangeStatus.Changed;
        return readOnlyChanged ? FileChangeStatus.ReadOnlyChanged : FileChangeStatus.Unchanged;
    }

    /// <summary>Call only after an explicit Keep Current choice. A later disk edit still blocks saving.</summary>
    public void AcceptExternalChanges(DocumentTab tab)
    {
        if (tab.FilePath is null) return;
        using var stream = new FileStream(tab.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        tab.DiskHash = Convert.ToHexString(SHA256.HashData(stream));
        tab.DiskLength = stream.Length;
        tab.LastWriteTimeUtc = File.GetLastWriteTimeUtc(tab.FilePath);
        tab.IsMissing = false;
        tab.IsReadOnly = File.GetAttributes(tab.FilePath).HasFlag(FileAttributes.ReadOnly);
        tab.IsDirty = true;
    }

    public async Task<string> ImportImageAsync(string sourcePath, string documentPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        documentPath = Path.GetFullPath(documentPath);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico"))
            throw new NotSupportedException("Choose a supported image file.");
        var directory = Path.Combine(Path.GetDirectoryName(documentPath)!, "images");
        Directory.CreateDirectory(directory);
        var name = $"image-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}{extension}";
        var destination = Path.Combine(directory, name);
        try
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(target);
            await target.FlushAsync();
        }
        catch
        {
            if (File.Exists(destination)) File.Delete(destination);
            throw;
        }
        return $"images/{name}";
    }

    private static SavedFile SaveBytes(string path, byte[] bytes, bool sameFile, string? expectedHash, bool explicitSaveAs)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(true);
            }
            if (File.Exists(path))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                    throw new UnauthorizedAccessException($"The file is read-only: {path}");
                // Deny concurrent writes through the final check and replacement, while allowing atomic replacement.
                using var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var actualHash = Convert.ToHexString(SHA256.HashData(original));
                if (sameFile && (expectedHash is null || actualHash != expectedHash))
                    throw new FileConflictException(path);
                File.Replace(temporary, path, null);
            }
            else
            {
                if (sameFile && !explicitSaveAs) throw new FileConflictException(path);
                // No overwrite: a file appearing after the existence check must remain intact.
                File.Move(temporary, path);
            }
            return new SavedFile(Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static LoadedFile Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > int.MaxValue) throw new IOException("This file is too large to load into the editor.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var (encoding, offset) = DetectEncoding(bytes);
        var content = encoding.GetString(bytes, offset, bytes.Length - offset);
        if (content.Contains('\0')) throw new InvalidDataException("This file contains binary data and cannot be opened as Markdown.");
        return new LoadedFile(content, encoding, offset > 0, Convert.ToHexString(SHA256.HashData(bytes)),
            bytes.LongLength, File.GetLastWriteTimeUtc(path), File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly));
    }

    private static (Encoding Encoding, int Offset) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return (new UTF32Encoding(false, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return (new UTF32Encoding(true, true, true), 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return (new UTF8Encoding(true, true), 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return (new UnicodeEncoding(false, true, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return (new UnicodeEncoding(true, true, true), 2);

        // BOM-less UTF-16 is recognized only with a strong alternating-NUL signal.
        if (bytes.Length >= 4 && bytes.Length % 2 == 0)
        {
            var evenZeros = 0;
            var oddZeros = 0;
            var sample = Math.Min(bytes.Length, 4096);
            for (var index = 0; index < sample; index++)
                if (bytes[index] == 0) { if (index % 2 == 0) evenZeros++; else oddZeros++; }
            if (oddZeros > sample / 8 && evenZeros == 0) return (new UnicodeEncoding(false, false, true), 0);
            if (evenZeros > sample / 8 && oddZeros == 0) return (new UnicodeEncoding(true, false, true), 0);
        }

        var utf8 = new UTF8Encoding(false, true);
        try { utf8.GetCharCount(bytes); return (utf8, 0); }
        catch (DecoderFallbackException) { }

        // Legacy encodings are inherently ambiguous. Prefer the user's Windows language and
        // accept only lossless byte-for-byte round trips, never replacement characters.
        var preferred = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        foreach (var codePage in new[] { preferred, 950, 932, 936, 949, 1252 }.Distinct())
        {
            if (codePage == 65001) continue;
            try
            {
                var encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                var text = encoding.GetString(bytes);
                if (encoding.GetBytes(text).AsSpan().SequenceEqual(bytes)) return (encoding, 0);
            }
            catch (Exception ex) when (ex is DecoderFallbackException or EncoderFallbackException or ArgumentException) { }
        }
        throw new InvalidDataException("The document encoding could not be detected safely.");
    }

    internal static byte[] GetPreamble(int codePage) => codePage switch
    {
        65001 => [0xef, 0xbb, 0xbf],
        1200 => [0xff, 0xfe],
        1201 => [0xfe, 0xff],
        12000 => [0xff, 0xfe, 0, 0],
        12001 => [0, 0, 0xfe, 0xff],
        _ => []
    };

    private sealed record LoadedFile(string Content, Encoding Encoding, bool HasBom, string Hash, long Length, DateTime LastWriteTimeUtc, bool IsReadOnly);
    private sealed record SavedFile(string Hash, long Length, DateTime LastWriteTimeUtc);
}
