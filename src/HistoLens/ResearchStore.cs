using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HistoLens.Core;

namespace HistoLens;

public sealed record SavedResearch
{
    public int FormatVersion { get; init; } = 1;
    public DateTimeOffset SavedAtUtc { get; init; }
    public DataSnapshot Snapshot { get; init; } = new();
    public ResearchRun Run { get; init; } = new();
}

/// <summary>Development saves contain synthetic data only; no source permission is inferred.</summary>
public sealed class ResearchStore(string directory)
{
    // Bounded v1 files accommodate 10,000 bars, six conditions and four dense horizons.
    internal const int MaximumBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true
    };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);

    public string[] List() => Directory.Exists(DirectoryPath)
        ? Directory.GetFiles(DirectoryPath, "*.histolens.json").OrderDescending(StringComparer.Ordinal).ToArray() : [];

    // Queue the entire operation: validation and JSON/hash work must not run on the UI thread.
    public Task<string> SaveAsync(DataSnapshot snapshot, ResearchRun run, CancellationToken cancellationToken = default) =>
        Task.Run(() => SaveCoreAsync(snapshot, run, cancellationToken), cancellationToken);

    private async Task<string> SaveCoreAsync(DataSnapshot snapshot, ResearchRun run, CancellationToken cancellationToken)
    {
        SavedResearchValidator.Validate(snapshot, run, cancellationToken);
        var document = new SavedResearch { SavedAtUtc = DateTimeOffset.UtcNow, Snapshot = snapshot, Run = run };
        var envelope = await CreateEnvelopeAsync(document, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DirectoryPath);
        var final = Path.Combine(DirectoryPath, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.histolens.json");
        var temporary = final + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                using var bounded = new BoundedWriteStream(stream);
                await JsonSerializer.SerializeAsync(bounded, envelope, Json, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, final);
            return final;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task<SavedResearch> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCoreAsync(path, cancellationToken), cancellationToken);

    private static async Task<SavedResearch> LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        // Open once; the size check and read refer to the same file handle.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Research file is too large.");
        try
        {
            var envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, Json, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Empty research file.");
            cancellationToken.ThrowIfCancellationRequested();
            if (envelope.FormatVersion != 1 || envelope.Payload is null ||
                !string.Equals(Hash(envelope.Payload), envelope.Checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Research file version or checksum is invalid.");
            cancellationToken.ThrowIfCancellationRequested();
            // A memory stream keeps cancellation available while parsing a large v1 payload.
            using var payload = new MemoryStream(Encoding.UTF8.GetBytes(envelope.Payload), false);
            var result = await JsonSerializer.DeserializeAsync<SavedResearch>(payload, Json, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Empty research payload.");
            if (result.FormatVersion != 1 || result.SavedAtUtc == default)
                throw new InvalidDataException("Unsupported or incomplete research version.");
            SavedResearchValidator.Validate(result.Snapshot, result.Run, cancellationToken);
            return result;
        }
        catch (JsonException error) { throw new InvalidDataException("Research JSON is invalid.", error); }
    }

    private static async Task<Envelope> CreateEnvelopeAsync(SavedResearch document, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        using var bounded = new BoundedWriteStream(memory);
        await JsonSerializer.SerializeAsync(bounded, document, Json, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var count = checked((int)memory.Length);
        var checksum = Convert.ToHexString(SHA256.HashData(memory.GetBuffer().AsSpan(0, count)));
        return new Envelope(1, checksum, Encoding.UTF8.GetString(memory.GetBuffer(), 0, count));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Envelope(int FormatVersion, string Checksum, string Payload);

    // Enforce the same bound while serializing both the payload and the escaped v1 envelope.
    // The caller owns the underlying stream, so disposal here does not close it.
    private sealed class BoundedWriteStream(Stream inner) : Stream
    {
        private long _written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        private void Check(int count)
        {
            if (_written + count > MaximumBytes)
                throw new InvalidDataException("Research file exceeds the 64 MiB development size limit.");
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            inner.Write(buffer);
            _written += buffer.Length;
        }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Check(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _written += buffer.Length;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
