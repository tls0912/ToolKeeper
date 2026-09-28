namespace CabiDock;

/// <summary>A host-owned product entry. It is never a filesystem or classification item.</summary>
public sealed record DesktopTool(string Id, string Name, string Description, string ActionLabel,
    bool CanActivate = true, string? ActivationUri = null);

internal static class DesktopToolsGroup
{
    // Reserved for layout only; this ID is never a user category or classification target.
    public const string Id = "__toolkeeper_tools";
    public static Models.CategoryDefinition Category { get; } = new() { Id = Id, Name = "工具番" };
}
