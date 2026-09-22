using CabiDock.Models;
using CabiDock.Services;
using Xunit;

namespace CabiDock.Tests;

public sealed class ConfigurationServiceTests
{
    [Fact]
    public void DefaultsHaveExactlySevenUsableCategoriesAndIndependentLists()
    {
        var configuration = ConfigurationService.LoadDefaults();
        Assert.Equal(new[] { "資料夾", "捷徑", "圖像類", "文件類", "影音類", "壓縮檔", "其他" }, configuration.Categories.Select(category => category.Name));
        Assert.Empty(configuration.KeywordRules);
        Assert.Empty(ConfigurationService.Validate(configuration));
        configuration.Categories.Clear();
        Assert.Equal(7, ConfigurationService.LoadDefaults().Categories.Count);
    }

    [Fact]
    public void CustomExtensionsCanOverlapDefaultsButNotOtherCustomCategories()
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories.Add(Custom("technical", "技術文件", ".MD"));
        Assert.Empty(ConfigurationService.Validate(configuration));
        configuration.Categories.Add(Custom("notes", "筆記", "md"));

        var error = Assert.Single(ConfigurationService.Validate(configuration));
        Assert.Contains("技術文件", error);
        Assert.Contains("md", error);
    }

    [Fact]
    public void NormalizationIgnoresExtensionCaseAndLeadingDotsWithoutMutatingInput()
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories.Add(Custom("technical", " 技術文件 ", " .MD ", "md"));

        var normalized = ConfigurationService.Normalize(configuration);

        Assert.Equal("md", Assert.Single(normalized.Categories[^1].Extensions));
        Assert.Equal("技術文件", normalized.Categories[^1].Name);
        Assert.Equal(2, configuration.Categories[^1].Extensions.Count);
    }

    [Fact]
    public void EmptyCustomExtensionListDoesNotMeanExtensionlessFiles()
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories.Add(Custom("empty", "空分類"));
        Assert.Empty(ConfigurationService.Validate(configuration));
        Assert.Equal("other", new ClassificationService().Classify(new DesktopItem(@"C:\Desktop\README", false), configuration));
    }

    [Fact]
    public void MissingRuleTargetAndWhitespaceKeywordAreRejected()
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.KeywordRules.Add(new KeywordRule { Keyword = " ", CategoryId = "missing" });
        Assert.Equal(2, ConfigurationService.Validate(configuration).Count);
    }

    [Fact]
    public void InvalidSpecialRulesAndDuplicateIdsAreRejected()
    {
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories[0].Extensions.Add("jpg");
        configuration.Categories.Add(Custom("images", "second images", ""));
        Assert.Contains(ConfigurationService.Validate(configuration), error => error.Contains("識別碼"));
        Assert.Contains(ConfigurationService.Validate(configuration), error => error.Contains("特殊分類"));
        Assert.Contains(ConfigurationService.Validate(configuration), error => error.Contains("無效的副檔名"));
    }

    [Fact]
    public void NullCollectionsFromDamagedJsonAreRejected()
    {
        Assert.NotEmpty(ConfigurationService.Validate(new CabiDockConfiguration { Categories = null! }));
        Assert.NotNull(StateService.ValidationError(new CabiDockState { Items = null! }));
        Assert.NotNull(StateService.ValidationError(new CabiDockState { Groups = { ["images"] = null! } }));
    }

    private static CategoryDefinition Custom(string id, string name, params string[] extensions) => new()
    {
        Id = id, Name = name, IsCustom = true, Extensions = extensions.ToList()
    };
}
