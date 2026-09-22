namespace MarkPad.Models;

public sealed class DocumentTab
{
    public string? FilePath { get; set; }

    public string DisplayName { get; set; } = "Untitled.md";

    public string Content { get; set; } = string.Empty;

    public bool IsDirty { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsPreviewMode { get; set; } = true;
}
