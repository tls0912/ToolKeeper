using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);

    public string[] List() => Directory.Exists(DirectoryPath)
        ? Directory.GetFiles(DirectoryPath, "*.histolens.json").OrderDescending(StringComparer.Ordinal).ToArray() : [];

    public async Task<string> SaveAsync(DataSnapshot snapshot, ResearchRun run, CancellationToken cancellationToken = default)
    {
        Validate(snapshot, run);
        var document = new SavedResearch { SavedAtUtc = DateTimeOffset.UtcNow, Snapshot = snapshot, Run = run };
        var payload = JsonSerializer.Serialize(document, Json);
        var envelope = JsonSerializer.Serialize(new Envelope(1, Hash(payload), payload), Json);
        if (Encoding.UTF8.GetByteCount(envelope) > MaximumBytes) throw new InvalidDataException("Research file exceeds the development size limit.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(DirectoryPath);
        var final = Path.Combine(DirectoryPath, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.histolens.json");
        var temporary = final + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, envelope, Encoding.UTF8, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, final);
            return final;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<SavedResearch> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Open once; the size check and read refer to the same file handle.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Research file is too large.");
        var envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, Json, cancellationToken)
            ?? throw new InvalidDataException("Empty research file.");
        if (envelope.FormatVersion != 1 || envelope.Payload is null || Hash(envelope.Payload) != envelope.Checksum)
            throw new InvalidDataException("Research file version or checksum is invalid.");
        var result = JsonSerializer.Deserialize<SavedResearch>(envelope.Payload, Json)
            ?? throw new InvalidDataException("Empty research payload.");
        if (result.FormatVersion != 1) throw new InvalidDataException("Unsupported research version.");
        Validate(result.Snapshot, result.Run);
        return result;
    }

    private static void Validate(DataSnapshot snapshot, ResearchRun run)
    {
        if (snapshot is null || run is null || !snapshot.IsSynthetic || !run.IsSynthetic ||
            snapshot.Instrument is null || snapshot.Instrument.SecurityType != SecurityType.Synthetic)
            throw new InvalidDataException("This preview saves and loads synthetic research only.");
        var hash = SnapshotFingerprint.Compute(snapshot);
        if (hash != run.DataContentHash || snapshot.SnapshotId != run.DataSnapshotId)
            throw new InvalidDataException("Research and data snapshot do not match.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Envelope(int FormatVersion, string Checksum, string Payload);
}
