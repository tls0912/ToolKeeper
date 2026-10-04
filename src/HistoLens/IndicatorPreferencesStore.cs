using System.IO;
using System.Text.Json;
using HistoLens.Core;

namespace HistoLens;

internal sealed class IndicatorPreferencesStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "indicator-preferences.json");

    public SimilarityIndicator Load()
    {
        try
        {
            using var stream = File.OpenRead(_path);
            var document = JsonSerializer.Deserialize<Document>(stream);
            return document is { Version: 1, SelectedIndicators: { } selected } && IsValid(selected)
                ? selected : SimilarityIndicator.Default;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or JsonException)
        { return SimilarityIndicator.Default; }
    }

    public void Save(SimilarityIndicator selected)
    {
        if (!IsValid(selected)) throw new ArgumentOutOfRangeException(nameof(selected));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new Document(1, selected));
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool IsValid(SimilarityIndicator selected) => (selected & ~SimilarityIndicator.All) == 0;
    private sealed record Document(int Version, SimilarityIndicator? SelectedIndicators);
}
