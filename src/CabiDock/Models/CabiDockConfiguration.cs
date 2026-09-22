namespace CabiDock.Models;

public enum CategoryKind
{
    Extension,
    Folder,
    Fallback
}

public sealed class CategoryDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = [];
    public CategoryKind Kind { get; set; } = CategoryKind.Extension;
    public bool IsCustom { get; set; }
}

public sealed class KeywordRule
{
    public string Keyword { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
}

public sealed class CabiDockConfiguration
{
    public List<CategoryDefinition> Categories { get; set; } = [];
    public List<KeywordRule> KeywordRules { get; set; } = [];
}
