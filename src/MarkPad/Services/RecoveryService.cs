using System.IO;
using System.Text;
using System.Text.Json;
using MarkPad.Models;

namespace MarkPad.Services;

/// <summary>Crash snapshots are separate files; recovery never writes to an original document.</summary>
public sealed class RecoveryService
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _generationGate = new();
    private readonly Dictionary<Guid, long> _generations = [];
    private long _clearGeneration;

    public RecoveryService(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task SaveAsync(IEnumerable<DocumentTab> tabs)
    {
        // Capture AvalonEdit documents on their owning UI thread, before awaiting disk work.
        List<Snapshot> snapshots;
        long clearGeneration;
        lock (_generationGate)
        {
            clearGeneration = _clearGeneration;
            snapshots = tabs.Select(tab => new Snapshot(tab.Id, _generations.GetValueOrDefault(tab.Id),
                tab.IsDirty ? new RecoveryRecord
                {
                    Id = tab.Id,
                    FilePath = tab.FilePath,
                    Content = tab.Content,
                    CodePage = tab.Encoding.CodePage,
                    HasBom = tab.HasBom,
                    IsPreviewMode = tab.IsPreviewMode,
                    IsLargeFile = tab.IsLargeFile,
                    CaretOffset = tab.CaretOffset,
                    PreviewScroll = tab.PreviewScroll,
                    EditorScroll = tab.EditorScroll,
                    LastWriteTimeUtc = tab.LastWriteTimeUtc,
                    DiskHash = tab.DiskHash,
                    DiskLength = tab.DiskLength,
                    SavedAtUtc = DateTime.UtcNow
                } : null)).ToList();
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(_directory);
                foreach (var snapshot in snapshots)
                {
                    lock (_generationGate)
                    {
                        if (clearGeneration != _clearGeneration || snapshot.Generation != _generations.GetValueOrDefault(snapshot.Id))
                            continue;
                        if (snapshot.Record is null) File.Delete(PathFor(snapshot.Id));
                        else AtomicFile.Write(PathFor(snapshot.Id), JsonSerializer.SerializeToUtf8Bytes(snapshot.Record));
                    }
                }
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<List<DocumentTab>> RecoverAsync()
    {
        var records = await ReadRecordsAsync();
        return records.Select(record =>
        {
            var exists = record.FilePath is not null && File.Exists(record.FilePath);
            var tab = new DocumentTab
            {
                Id = record.Id,
                FilePath = record.FilePath,
                Content = record.Content,
                Encoding = Encoding.GetEncoding(record.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
                HasBom = record.HasBom,
                IsPreviewMode = record.IsPreviewMode,
                IsLargeFile = record.IsLargeFile,
                IsMissing = record.FilePath is not null && !exists,
                IsReadOnly = exists && new FileInfo(record.FilePath!).IsReadOnly,
                CaretOffset = Math.Clamp(record.CaretOffset, 0, record.Content.Length),
                PreviewScroll = NonNegative(record.PreviewScroll),
                EditorScroll = NonNegative(record.EditorScroll),
                LastWriteTimeUtc = record.LastWriteTimeUtc,
                DiskHash = record.DiskHash,
                DiskLength = record.DiskLength
            };
            tab.Document.UndoStack.ClearAll();
            tab.IsDirty = true;
            if (tab.IsReadOnly) tab.IsPreviewMode = true;
            return tab;
        }).ToList();
    }

    private async Task<List<RecoveryRecord>> ReadRecordsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var result = new List<RecoveryRecord>();
                if (!Directory.Exists(_directory)) return result;
                foreach (var file in Directory.EnumerateFiles(_directory, "*.recovery.json"))
                {
                    try
                    {
                        var record = JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(file));
                        if (record is null || record.Version != 1 || record.Id == Guid.Empty || record.Content is null
                            || !string.Equals(Path.GetFileName(file), $"{record.Id:N}.recovery.json", StringComparison.OrdinalIgnoreCase))
                            continue;
                        Encoding.GetEncoding(record.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                        if (record.FilePath is not null) record.FilePath = Path.GetFullPath(record.FilePath);
                        result.Add(record);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
                    {
                        LocalLog.Write(ex);
                    }
                }
                return result.OrderBy(record => record.SavedAtUtc).ToList();
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public void Delete(Guid id)
    {
        // Invalidate pending snapshots before waiting, so closing a tab cannot resurrect its backup.
        lock (_generationGate) _generations[id] = _generations.GetValueOrDefault(id) + 1;
        _gate.Wait();
        try { File.Delete(PathFor(id)); }
        finally { _gate.Release(); }
    }

    public void Clear()
    {
        lock (_generationGate) _clearGeneration++;
        _gate.Wait();
        try
        {
            if (!Directory.Exists(_directory)) return;
            foreach (var path in Directory.EnumerateFiles(_directory, "*.recovery.json"))
            {
                var name = Path.GetFileName(path)[..^".recovery.json".Length];
                if (Guid.TryParseExact(name, "N", out _)) File.Delete(path);
            }
        }
        finally { _gate.Release(); }
    }

    private string PathFor(Guid id) => Path.Combine(_directory, $"{id:N}.recovery.json");
    private static double NonNegative(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
    private sealed record Snapshot(Guid Id, long Generation, RecoveryRecord? Record);

    private sealed class RecoveryRecord
    {
        public int Version { get; set; } = 1;
        public Guid Id { get; set; }
        public string? FilePath { get; set; }
        public string Content { get; set; } = string.Empty;
        public int CodePage { get; set; } = 65001;
        public bool HasBom { get; set; }
        public bool IsPreviewMode { get; set; }
        public bool IsLargeFile { get; set; }
        public int CaretOffset { get; set; }
        public double PreviewScroll { get; set; }
        public double EditorScroll { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string? DiskHash { get; set; }
        public long DiskLength { get; set; }
        public DateTime SavedAtUtc { get; set; }
    }
}
