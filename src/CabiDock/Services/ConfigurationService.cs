using System.Reflection;
using System.Text.Json;
using CabiDock.Models;

namespace CabiDock.Services;

public static class ConfigurationService
{
    public static string? ValidationError(CabiDockConfiguration configuration)
    {
        var errors = Validate(configuration);
        return errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
    }

    public static CabiDockConfiguration LoadDefaults()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CabiDock.Defaults.categories.json")
            ?? throw new InvalidOperationException("找不到內建分類設定。");
        var configuration = JsonSerializer.Deserialize<CabiDockConfiguration>(stream, JsonFileStore<CabiDockConfiguration>.SerializerOptions)
            ?? throw new InvalidOperationException("內建分類設定無效。");
        return Normalize(configuration);
    }

    public static string NormalizeExtension(string extension) => extension.Trim().TrimStart('.').ToLowerInvariant();

    public static CabiDockConfiguration Normalize(CabiDockConfiguration configuration)
    {
        return new CabiDockConfiguration
        {
            Categories = configuration.Categories.Select(category => new CategoryDefinition
            {
                Id = category.Id.Trim(),
                Name = category.Name.Trim(),
                Kind = category.Kind,
                IsCustom = category.IsCustom,
                Extensions = category.Extensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            }).ToList(),
            KeywordRules = configuration.KeywordRules.Select(rule => new KeywordRule
            {
                Keyword = rule.Keyword,
                CategoryId = rule.CategoryId.Trim()
            }).ToList()
        };
    }

    public static IReadOnlyList<string> Validate(CabiDockConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.Categories is null || configuration.KeywordRules is null)
        {
            errors.Add("設定必須包含分類與關鍵字規則清單。");
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folderCount = 0;
        var fallbackCount = 0;
        foreach (var category in configuration.Categories)
        {
            if (category is null)
            {
                errors.Add("分類不得為空。");
                continue;
            }

            if (string.IsNullOrWhiteSpace(category.Id) || !ids.Add(category.Id.Trim()))
                errors.Add("分類識別碼不得留白或重複。");
            if (string.IsNullOrWhiteSpace(category.Name) || !names.Add(category.Name.Trim()))
                errors.Add("分類名稱不得留白或重複。");
            if (!Enum.IsDefined(category.Kind))
                errors.Add($"「{category.Name}」的分類方式無效。");
            if (category.Kind == CategoryKind.Folder)
                folderCount++;
            if (category.Kind == CategoryKind.Fallback)
                fallbackCount++;
            if (category.IsCustom && category.Kind != CategoryKind.Extension)
                errors.Add($"自訂分類「{category.Name}」必須使用副檔名規則。");
            if (category.Extensions is null)
            {
                errors.Add($"「{category.Name}」缺少副檔名清單。");
                continue;
            }
            if (category.Kind != CategoryKind.Extension && category.Extensions.Count > 0)
                errors.Add($"「{category.Name}」是特殊分類，不使用副檔名規則。");

            var categoryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawExtension in category.Extensions)
            {
                var extension = rawExtension is null ? string.Empty : NormalizeExtension(rawExtension);
                if (extension.Length == 0 || extension.Any(character => char.IsWhiteSpace(character) || ".\\/:*?\"<>|".Contains(character)))
                {
                    errors.Add($"「{category.Name}」含有無效的副檔名「{rawExtension}」。");
                    continue;
                }
                if (!category.IsCustom || !categoryExtensions.Add(extension))
                    continue;
                if (customExtensions.TryGetValue(extension, out var existingCategory))
                    errors.Add($"副檔名「{extension}」已屬於自訂分類「{existingCategory}」，不能重複加入「{category.Name}」。");
                else
                    customExtensions.Add(extension, category.Name);
            }
        }
        if (folderCount != 1 || fallbackCount != 1)
            errors.Add("必須各保留一個資料夾分類與其他兜底分類。");

        foreach (var rule in configuration.KeywordRules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Keyword))
                errors.Add("檔名關鍵字不得留白。");
            if (rule is not null && (rule.CategoryId is null || !ids.Contains(rule.CategoryId.Trim())))
                errors.Add($"關鍵字「{rule.Keyword}」的目標分類不存在。");
        }
        return errors;
    }
}
