using System.Text.Json.Serialization;

namespace CabiDock.Models;

public enum ClassificationSource
{
    Auto,
    Manual
}

public sealed class ClassifiedItem
{
    public string FullPath { get; set; } = string.Empty;
    public string? Identity { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    [JsonRequired]
    public ClassificationSource Source { get; set; }
}

public sealed class GroupLayout
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 320;
}

public sealed class CabiDockState
{
    public string? ConfigurationFingerprint { get; set; }
    [JsonRequired]
    public List<ClassifiedItem> Items { get; set; } = [];
    [JsonRequired]
    public Dictionary<string, GroupLayout> Groups { get; set; } = new(StringComparer.Ordinal);
}
