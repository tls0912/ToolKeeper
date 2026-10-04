using System.IO;
using MarkPad.Models;

namespace MarkPad.Services;

/// <summary>Remembers saved file paths; unsaved content stays with the existing recovery and close flows.</summary>
public sealed class DocumentSessionService
{
    private readonly List<string> _closedFiles = [];

    public void Reset() => _closedFiles.Clear();

    public void RememberClosingWindow(SettingsService preferences, IEnumerable<DocumentTab> closingDocuments,
        IEnumerable<DocumentTab> remainingDocuments, string? activeFile)
    {
        var settings = preferences.Settings;
        if (!settings.RememberOpenFiles)
        {
            Reset();
            Normalize(settings);
            preferences.Save();
            return;
        }

        // Include windows already closed during this run, while taking still-open tabs
        // from their current state so an explicitly closed tab is not remembered.
        var closedFiles = NormalizePaths(_closedFiles.Concat(closingDocuments.Select(tab => tab.FilePath)));
        settings.SessionFiles = NormalizePaths(closedFiles.Concat(remainingDocuments.Select(tab => tab.FilePath)));
        settings.SessionActiveFile = FindPath(settings.SessionFiles, activeFile);
        preferences.Save();
        _closedFiles.Clear();
        _closedFiles.AddRange(closedFiles);
    }

    public static string[] FilesToRestore(AppSettings settings)
    {
        if (!settings.RememberOpenFiles) return [];
        return NormalizePaths(settings.SessionFiles ?? []).Where(File.Exists).ToArray();
    }

    internal static void Normalize(AppSettings settings)
    {
        settings.SessionFiles = settings.RememberOpenFiles ? NormalizePaths(settings.SessionFiles ?? []) : [];
        settings.SessionActiveFile = FindPath(settings.SessionFiles, settings.SessionActiveFile);
    }

    private static List<string> NormalizePaths(IEnumerable<string?> paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in paths)
        {
            var path = Canonical(value);
            if (path is not null && seen.Add(path)) result.Add(path);
        }
        return result;
    }

    private static string? FindPath(IEnumerable<string> paths, string? value)
    {
        var path = Canonical(value);
        return path is null ? null : paths.FirstOrDefault(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) return null;
        try { return Path.GetFullPath(value); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
