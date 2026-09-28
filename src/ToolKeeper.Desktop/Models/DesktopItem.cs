using System.IO;
using System.Text.Json.Serialization;

namespace CabiDock.Models;

public sealed class DesktopItem
{
    public DesktopItem()
    {
    }

    public DesktopItem(string fullPath, bool isDirectory, string? identity = null)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Identity = identity;
    }

    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string? Identity { get; set; }

    [JsonIgnore]
    public string Name => Path.GetFileName(FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
