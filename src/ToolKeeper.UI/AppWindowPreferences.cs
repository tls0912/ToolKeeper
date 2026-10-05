using System.IO;
using System.Text.Json;

namespace ToolKeeper.UI;

/// <summary>Shell preferences kept in a caller-selected file, separate from product data.</summary>
public sealed record AppWindowPreferences(string Theme = "Ink", string Language = "System")
{
    public static AppWindowPreferences Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var value = JsonSerializer.Deserialize<AppWindowPreferences>(File.ReadAllText(path)) ?? new();
            return new(UiTheme.IsSupported(value.Theme) ? value.Theme : "Ink",
                UiLanguage.IsSupported(value.Language) ? value.Language : "System");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
