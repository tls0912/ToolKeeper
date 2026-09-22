using System.IO;
using System.Text.Json;
using MarkPad.Models;

namespace MarkPad.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    public string DataDirectory { get; }
    public AppSettings Settings { get; private set; }

    public SettingsService(string? directory = null)
    {
        DataDirectory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper", "MarkPad"));
        _path = Path.Combine(DataDirectory, "settings.json");
        Settings = new AppSettings();
        try
        {
            if (File.Exists(_path))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LocalLog.Write(ex);
        }
        Normalize();
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(DataDirectory);
        AtomicFile.Write(_path, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Settings, JsonOptions)));
    }

    public void AddRecent(string path)
    {
        path = Path.GetFullPath(path);
        Settings.RecentFiles.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        Settings.RecentFiles.Insert(0, path);
        Save();
    }

    public void Reset()
    {
        Settings = new AppSettings();
        Save();
    }

    private void Normalize()
    {
        // Fresh installs, Reset and invalid-value recovery share one source of defaults.
        var defaults = new AppSettings();
        if (Settings.Theme is not ("System" or "Light" or "Dark")) Settings.Theme = defaults.Theme;
        if (Settings.Language is not ("System" or "en" or "zh-TW" or "ja")) Settings.Language = defaults.Language;
        Settings.UiFontFamily ??= defaults.UiFontFamily;
        Settings.PreviewFontFamily ??= defaults.PreviewFontFamily;
        if (string.IsNullOrWhiteSpace(Settings.EditorFontFamily)) Settings.EditorFontFamily = defaults.EditorFontFamily;
        Settings.UiFontSize = ValidNumber(Settings.UiFontSize, 10, 20, defaults.UiFontSize);
        Settings.PreviewFontSize = ValidNumber(Settings.PreviewFontSize, 8, 72, defaults.PreviewFontSize);
        Settings.EditorFontSize = ValidNumber(Settings.EditorFontSize, 8, 72, defaults.EditorFontSize);
        Settings.WindowWidth = ValidNumber(Settings.WindowWidth, 480, 10000, defaults.WindowWidth);
        Settings.WindowHeight = ValidNumber(Settings.WindowHeight, 0, 10000, defaults.WindowHeight);
        Settings.RecentFiles = (Settings.RecentFiles ?? []).Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
    }

    private static double ValidNumber(double value, double min, double max, double fallback) =>
        double.IsFinite(value) && value >= min && value <= max ? value : fallback;
}
