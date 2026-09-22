using CabiDock.Models;
using CabiDock.Services;
using Xunit;

namespace CabiDock.Tests;

public sealed class ClassificationServiceTests
{
    private readonly ClassificationService _service = new();
    private readonly CabiDockConfiguration _configuration = ConfigurationService.LoadDefaults();

    [Theory]
    [InlineData("photo.JPG", false, "images")]
    [InlineData("report.pdf", false, "documents")]
    [InlineData("README.MD", false, "documents")]
    [InlineData("film.mkv", false, "media")]
    [InlineData("backup.7z", false, "archives")]
    [InlineData("app.lnk", false, "shortcuts")]
    [InlineData("site.url", false, "shortcuts")]
    [InlineData("LICENSE", false, "other")]
    [InlineData("unknown.zzz", false, "other")]
    [InlineData("folder.jpg", true, "folders")]
    public void ClassifiesDefaultTypes(string name, bool isDirectory, string expected)
    {
        Assert.Equal(expected, _service.Classify(Item(name, isDirectory), _configuration));
    }

    [Fact]
    public void KeywordUsesCompleteFilenameOnlyAndFirstMatch()
    {
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = "desktop", CategoryId = "media" });
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = "發票", CategoryId = "documents" });
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = ".PNG", CategoryId = "archives" });

        Assert.Equal("documents", _service.Classify(Item("發票.png"), _configuration));
        Assert.Equal("archives", _service.Classify(Item("photo.png"), _configuration));
        Assert.Equal("other", _service.Classify(Item("unmatched"), _configuration));
    }

    [Fact]
    public void KeywordCanClassifyFoldersBeforeTypeButExtensionsCannot()
    {
        AddCustom("technical", "技術文件", ".MD");
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = "發票", CategoryId = "documents" });

        Assert.Equal("documents", _service.Classify(Item("發票.md", true), _configuration));
        Assert.Equal("folders", _service.Classify(Item("folder.md", true), _configuration));
    }

    [Fact]
    public void ManualOverridesKeywordAndCustomOverridesDefault()
    {
        AddCustom("technical", "技術文件", ".MD");
        Assert.Equal("technical", _service.Classify(Item("notes.md"), _configuration));
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = "notes", CategoryId = "images" });
        Assert.Equal("images", _service.Classify(Item("notes.md"), _configuration));
        Assert.Equal("archives", _service.Classify(Item("notes.md"), _configuration, "archives"));
    }

    [Fact]
    public void StartupPreservesBothSavedAutomaticAndManualAssignments()
    {
        var automatic = Item("a.png");
        var manual = Item("b.png");
        var state = new CabiDockState();
        _service.Reconcile([automatic, manual], state, _configuration);
        _service.AssignManually(manual, "archives", state, _configuration);
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = ".png", CategoryId = "documents" });

        Assert.False(_service.Reconcile([automatic, manual], state, _configuration));
        Assert.Equal("images", state.Items[0].CategoryId);
        Assert.Equal("archives", state.Items[1].CategoryId);
        Assert.Equal(ClassificationSource.Manual, state.Items[1].Source);
    }

    [Fact]
    public void ExplicitRuleChangeReclassifiesAutomaticAndPreservesValidManual()
    {
        var automatic = Item("a.png");
        var manual = Item("b.png");
        var state = new CabiDockState();
        _service.Reconcile([automatic, manual], state, _configuration);
        _service.AssignManually(manual, "archives", state, _configuration);
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = ".png", CategoryId = "documents" });

        Assert.True(_service.Reconcile([automatic, manual], state, _configuration, rulesChanged: true));
        Assert.Equal("documents", state.Items[0].CategoryId);
        Assert.Equal("archives", state.Items[1].CategoryId);
    }

    [Fact]
    public void KeywordOrderChangeUpdatesSavedAutomaticItems()
    {
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = "發票", CategoryId = "documents" });
        _configuration.KeywordRules.Add(new KeywordRule { Keyword = "2026", CategoryId = "archives" });
        var item = Item("發票2026.png");
        var state = new CabiDockState();
        _service.Reconcile([item], state, _configuration);
        _configuration.KeywordRules.Reverse();

        _service.Reconcile([item], state, _configuration, rulesChanged: true);

        Assert.Equal("archives", Assert.Single(state.Items).CategoryId);
    }

    [Fact]
    public void DeletedCategoryInvalidatesManualRecordButDoesNotDeleteRulesOrLayout()
    {
        AddCustom("technical", "技術文件", "md");
        var item = Item("a.png");
        var state = new CabiDockState { Groups = { ["images"] = new GroupLayout { X = 55, Y = 70 } } };
        _service.AssignManually(item, "technical", state, _configuration);
        _configuration.Categories.RemoveAll(category => category.Id == "technical");

        Assert.True(_service.Reconcile([item], state, _configuration));
        Assert.Equal("images", Assert.Single(state.Items).CategoryId);
        Assert.Equal(ClassificationSource.Auto, state.Items[0].Source);
        Assert.Equal(55, state.Groups["images"].X);
    }

    [Fact]
    public void SuccessfulScanRemovesMissingRecordsAndAddsNewOnes()
    {
        var state = new CabiDockState();
        _service.Reconcile([Item("old.png")], state, _configuration);
        Assert.True(_service.Reconcile([Item("new.pdf")], state, _configuration));
        Assert.Equal(Item("new.pdf").FullPath, Assert.Single(state.Items).FullPath);
    }

    [Fact]
    public void FailedScanDoesNotDeleteOrReclassifyAnyRecord()
    {
        var state = new CabiDockState();
        _service.AssignManually(Item("existing.png"), "archives", state, _configuration);

        Assert.False(_service.Reconcile([], state, _configuration, scanSucceeded: false, rulesChanged: true));
        Assert.Equal("archives", Assert.Single(state.Items).CategoryId);
    }

    [Fact]
    public void ObservedRemovalThenReturnStartsFreshButUnobservedAbsencePreservesRecord()
    {
        var item = Item("existing.png");
        var state = new CabiDockState();
        _service.AssignManually(item, "archives", state, _configuration);
        _service.Reconcile([item], state, _configuration);
        Assert.Equal("archives", Assert.Single(state.Items).CategoryId);

        Assert.True(_service.Remove(item.FullPath, state));
        _service.Reconcile([item], state, _configuration);
        Assert.Equal("images", Assert.Single(state.Items).CategoryId);
        Assert.Equal(ClassificationSource.Auto, state.Items[0].Source);
    }

    [Fact]
    public void ConfirmedRenamePreservesCategoryAndUpdatesPath()
    {
        var before = Item("a.png");
        var after = Item("a.pdf");
        var state = new CabiDockState();
        _service.AssignManually(before, "archives", state, _configuration);

        Assert.True(_service.Rename(before.FullPath, after, state));
        Assert.False(_service.Reconcile([after], state, _configuration));
        Assert.Equal(after.FullPath, Assert.Single(state.Items).FullPath);
        Assert.Equal("archives", state.Items[0].CategoryId);
    }

    [Fact]
    public void UniqueStableIdentityMatchesRenameOnStartup()
    {
        var before = new DesktopItem(@"C:\Desktop\a.png", false, "volume:file-id");
        var after = new DesktopItem(@"C:\Desktop\b.pdf", false, "volume:file-id");
        var state = new CabiDockState();
        _service.Reconcile([before], state, _configuration);

        Assert.True(_service.Reconcile([after], state, _configuration));
        Assert.Equal("images", Assert.Single(state.Items).CategoryId);
        Assert.Equal(after.FullPath, state.Items[0].FullPath);
    }

    [Fact]
    public void ChangedIdentityAtSamePathIsANewItem()
    {
        var state = new CabiDockState();
        _service.AssignManually(new DesktopItem(@"C:\Desktop\a.png", false, "old-id"), "archives", state, _configuration);
        _service.Reconcile([new DesktopItem(@"C:\Desktop\a.png", false, "new-id")], state, _configuration);
        Assert.Equal("images", Assert.Single(state.Items).CategoryId);
    }

    [Fact]
    public void DuplicatePathsAcrossDesktopSourcesAppearOnce()
    {
        var state = new CabiDockState();
        _service.Reconcile([Item("a.png"), Item("A.PNG")], state, _configuration);
        Assert.Single(state.Items);
    }

    [Fact]
    public void CategoryRenamePreservesManualClassificationAndSpecialBehavior()
    {
        var state = new CabiDockState();
        var item = Item("notes.png");
        _service.AssignManually(item, "folders", state, _configuration);
        _configuration.Categories.Single(category => category.Id == "folders").Name = "我的資料夾";
        _configuration.Categories.Single(category => category.Id == "other").Name = "未分類";

        _service.Reconcile([item], state, _configuration, rulesChanged: true);

        Assert.Equal("folders", Assert.Single(state.Items).CategoryId);
        Assert.Equal("folders", _service.Classify(Item("folder.txt", true), _configuration));
        Assert.Equal("other", _service.Classify(Item("no-extension"), _configuration));
    }

    private static DesktopItem Item(string name, bool directory = false) => new(@"C:\Desktop\" + name, directory);

    private void AddCustom(string id, string name, params string[] extensions) => _configuration.Categories.Add(new CategoryDefinition
    {
        Id = id, Name = name, IsCustom = true, Extensions = extensions.ToList()
    });
}
