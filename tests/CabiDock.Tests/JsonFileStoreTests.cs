using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CabiDock.Models;
using CabiDock.Services;
using Xunit;

namespace CabiDock.Tests;

public sealed class JsonFileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CabiDock.Tests", Guid.NewGuid().ToString("N"));
    private JsonFileStore<CabiDockState> CreateStore() => new(Path.Combine(_directory, "state.json"), StateService.ValidationError);

    [Fact]
    public void MissingFilesUseDefaultsWithoutWritingUntilSave()
    {
        var store = CreateStore();
        var result = store.Load(() => new CabiDockState());
        Assert.True(result.CanSave);
        Assert.Null(result.Error);
        Assert.Empty(result.Value.Items);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void ExistingButUnreadablePathIsNotTreatedAsAnEmptyProfile()
    {
        var store = CreateStore();
        // A directory produces an access error while File.Exists misleadingly returns false.
        Directory.CreateDirectory(store.FilePath);

        var result = store.Load(() => new CabiDockState());

        Assert.False(result.CanSave);
        Assert.NotNull(result.Error);
        Assert.False(store.Save(new CabiDockState()).Success);
        Assert.True(Directory.Exists(store.FilePath));
    }

    [Fact]
    public void RoundtripPersistsAutomaticManualAndGroupConfiguration()
    {
        var store = CreateStore();
        var state = State("one.png", "images", ClassificationSource.Auto);
        state.Items.Add(State("two.pdf", "archives", ClassificationSource.Manual).Items[0]);
        state.ConfigurationFingerprint = "saved-config-hash";
        state.Groups.Add("empty-category", new GroupLayout { X = 120, Y = 200, Width = 450, Height = 600 });

        Assert.True(store.Save(state).Success);
        var saved = CreateStore().Load(() => new CabiDockState());

        Assert.Null(saved.Error);
        Assert.Equal(2, saved.Value.Items.Count);
        Assert.Equal(ClassificationSource.Manual, saved.Value.Items[1].Source);
        Assert.Equal(600, saved.Value.Groups["empty-category"].Height);
        Assert.Equal("saved-config-hash", saved.Value.ConfigurationFingerprint);
    }

    [Fact]
    public void CorruptPrimaryRecoversBackupAndLaterSaveKeepsBackupHealthy()
    {
        var store = CreateStore();
        Assert.True(store.Save(State("first.png")).Success);
        Assert.True(store.Save(State("second.png")).Success);
        File.WriteAllText(store.FilePath, "{ broken");

        var recovered = CreateStore().Load(() => new CabiDockState());
        Assert.True(recovered.RecoveredFromBackup);
        Assert.True(recovered.CanSave);
        Assert.EndsWith("first.png", Assert.Single(recovered.Value.Items).FullPath);
        Assert.True(store.Save(recovered.Value).Success);

        var backup = new JsonFileStore<CabiDockState>(store.BackupPath, StateService.ValidationError).Load(() => new CabiDockState());
        Assert.Null(backup.Error);
        Assert.EndsWith("first.png", Assert.Single(backup.Value.Items).FullPath);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void UnrecoverableReadBlocksWritesAndPreservesDamagedFiles()
    {
        Directory.CreateDirectory(_directory);
        var store = CreateStore();
        File.WriteAllText(store.FilePath, "{ damaged primary");
        File.WriteAllText(store.BackupPath, "{ damaged backup");

        var loaded = store.Load(() => new CabiDockState());

        Assert.False(loaded.CanSave);
        Assert.NotNull(loaded.Error);
        Assert.False(store.Save(new CabiDockState()).Success);
        Assert.Equal("{ damaged primary", File.ReadAllText(store.FilePath));
        Assert.Equal("{ damaged backup", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void StructurallyInvalidStateRecoversValidBackup()
    {
        var store = CreateStore();
        store.Save(State("first.png"));
        store.Save(State("second.png"));
        File.WriteAllText(store.FilePath, "{\"items\":null,\"groups\":{}}");

        var loaded = CreateStore().Load(() => new CabiDockState());

        Assert.True(loaded.RecoveredFromBackup);
        Assert.Single(loaded.Value.Items);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("items")]
    [InlineData("groups")]
    [InlineData("source")]
    public void MissingRequiredStateMemberRecoversBackupAndKeepsItHealthy(string member)
    {
        var store = CreateStore();
        var original = State("first.png", "archives", ClassificationSource.Manual);
        original.Groups["archives"] = new GroupLayout { X = 70, Y = 90, Width = 440, Height = 350 };
        Assert.True(store.Save(original).Success);
        Assert.True(store.Save(State("second.png")).Success);
        var backup = File.ReadAllBytes(store.BackupPath);
        File.WriteAllText(store.FilePath, StateWithout(member));

        var recoveredStore = CreateStore();
        var loaded = recoveredStore.Load(() => new CabiDockState());

        Assert.True(loaded.RecoveredFromBackup);
        Assert.True(loaded.CanSave);
        Assert.NotNull(loaded.Error);
        var item = Assert.Single(loaded.Value.Items);
        Assert.EndsWith("first.png", item.FullPath);
        Assert.Equal("archives", item.CategoryId);
        Assert.Equal(ClassificationSource.Manual, item.Source);
        Assert.Equal(440, loaded.Value.Groups["archives"].Width);
        Assert.True(recoveredStore.Save(loaded.Value).Success);
        Assert.Equal(backup, File.ReadAllBytes(store.BackupPath));
    }

    [Theory]
    [InlineData("all", false)]
    [InlineData("items", false)]
    [InlineData("groups", false)]
    [InlineData("source", false)]
    [InlineData("all", true)]
    [InlineData("items", true)]
    [InlineData("groups", true)]
    [InlineData("source", true)]
    public void MissingRequiredStateMemberWithoutHealthyBackupBlocksWrites(string member, bool hasBackup)
    {
        Directory.CreateDirectory(_directory);
        var store = CreateStore();
        var incomplete = StateWithout(member);
        File.WriteAllText(store.FilePath, incomplete);
        if (hasBackup) File.WriteAllText(store.BackupPath, incomplete);

        var loaded = store.Load(() => new CabiDockState());

        Assert.False(loaded.CanSave);
        Assert.False(loaded.RecoveredFromBackup);
        Assert.NotNull(loaded.Error);
        Assert.False(store.Save(State("replacement.png")).Success);
        Assert.Equal(incomplete, File.ReadAllText(store.FilePath));
        if (hasBackup) Assert.Equal(incomplete, File.ReadAllText(store.BackupPath));
        else Assert.False(File.Exists(store.BackupPath));
    }

    [Fact]
    public void StateWithoutOptionalIdentityAndFingerprintStillLoadsSavedClassifications()
    {
        Directory.CreateDirectory(_directory);
        var store = CreateStore();
        File.WriteAllText(store.FilePath, """
            {
              "items": [
                { "fullPath": "C:\\Desktop\\automatic.png", "categoryId": "images", "source": "auto" },
                { "fullPath": "C:\\Desktop\\manual.png", "categoryId": "archives", "source": "manual" }
              ],
              "groups": { "archives": { "x": 70, "y": 90, "width": 440, "height": 350 } }
            }
            """);

        var loaded = store.Load(() => new CabiDockState());

        Assert.True(loaded.CanSave);
        Assert.False(loaded.RecoveredFromBackup);
        Assert.Null(loaded.Error);
        Assert.Null(loaded.Value.ConfigurationFingerprint);
        Assert.Equal(new[] { ClassificationSource.Auto, ClassificationSource.Manual }, loaded.Value.Items.Select(item => item.Source));
        Assert.All(loaded.Value.Items, item => Assert.Null(item.Identity));
        Assert.Equal(440, loaded.Value.Groups["archives"].Width);
    }

    [Fact]
    public void ExplicitEmptyStateCollectionsAreValid()
    {
        Directory.CreateDirectory(_directory);
        var store = CreateStore();
        File.WriteAllText(store.FilePath, """{"items":[],"groups":{}}""");

        var loaded = store.Load(() => new CabiDockState());

        Assert.True(loaded.CanSave);
        Assert.Null(loaded.Error);
        Assert.Empty(loaded.Value.Items);
        Assert.Empty(loaded.Value.Groups);
    }

    [Fact]
    public void LockedDestinationReturnsFailureAndPreservesUsableRecords()
    {
        var store = CreateStore();
        Assert.True(store.Save(State("first.png")).Success);
        var before = File.ReadAllBytes(store.FilePath);

        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(store.Save(new CabiDockState()).Success);

        Assert.Equal(before, File.ReadAllBytes(store.FilePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void InvalidValueDoesNotOverwriteValidFile()
    {
        var store = CreateStore();
        store.Save(State("first.png"));

        Assert.False(store.Save(new CabiDockState { Groups = { ["images"] = new GroupLayout { Width = double.NaN } } }).Success);
        Assert.Single(store.Load(() => new CabiDockState()).Value.Items);
    }

    [Fact]
    public void ConfigSemanticFailureRecoversPreviousValidVersion()
    {
        var store = new JsonFileStore<CabiDockConfiguration>(Path.Combine(_directory, "config.json"), ConfigurationService.ValidationError);
        var configuration = ConfigurationService.LoadDefaults();
        Assert.True(store.Save(configuration).Success);
        configuration.Categories[0].Name = "我的資料夾";
        Assert.True(store.Save(configuration).Success);
        File.WriteAllText(store.FilePath, "{\"categories\":[],\"keywordRules\":[]}");

        var loaded = store.Load(ConfigurationService.LoadDefaults);

        Assert.True(loaded.RecoveredFromBackup);
        Assert.Equal(7, loaded.Value.Categories.Count);
    }

    [Fact]
    public void GroupOpacityRoundtripPreservesClassificationSettings()
    {
        var store = new JsonFileStore<CabiDockConfiguration>(Path.Combine(_directory, "config.json"), ConfigurationService.ValidationError);
        var configuration = ConfigurationService.LoadDefaults();
        configuration.GroupOpacity = 0.65;
        configuration.KeywordRules.Add(new KeywordRule { Keyword = "invoice", CategoryId = "documents" });

        Assert.True(store.Save(configuration).Success);
        var loaded = store.Load(ConfigurationService.LoadDefaults);

        Assert.Null(loaded.Error);
        Assert.Equal(0.65, loaded.Value.GroupOpacity);
        Assert.Equal(7, loaded.Value.Categories.Count);
        Assert.Equal("invoice", Assert.Single(loaded.Value.KeywordRules).Keyword);
    }

    [Fact]
    public void LegacyConfigurationWithoutOpacityLoadsAsFullyOpaque()
    {
        var store = new JsonFileStore<CabiDockConfiguration>(Path.Combine(_directory, "config.json"), ConfigurationService.ValidationError);
        var configuration = ConfigurationService.LoadDefaults();
        configuration.Categories[0].Name = "原有分類";
        var legacyJson = JsonSerializer.SerializeToNode(configuration, JsonFileStore<CabiDockConfiguration>.SerializerOptions)!.AsObject();
        Assert.True(legacyJson.Remove("groupOpacity"));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, legacyJson.ToJsonString());

        var loaded = store.Load(ConfigurationService.LoadDefaults);

        Assert.Null(loaded.Error);
        Assert.False(loaded.RecoveredFromBackup);
        Assert.Equal(1, loaded.Value.GroupOpacity);
        Assert.Equal("原有分類", loaded.Value.Categories[0].Name);
    }

    [Fact]
    public void InvalidGroupOpacityDoesNotOverwriteSavedConfiguration()
    {
        var store = new JsonFileStore<CabiDockConfiguration>(Path.Combine(_directory, "config.json"), ConfigurationService.ValidationError);
        var configuration = ConfigurationService.LoadDefaults();
        configuration.GroupOpacity = 0.65;
        Assert.True(store.Save(configuration).Success);
        configuration.GroupOpacity = 0;

        Assert.False(store.Save(configuration).Success);
        Assert.Equal(0.65, store.Load(ConfigurationService.LoadDefaults).Value.GroupOpacity);
    }

    private static CabiDockState State(string name, string category = "images", ClassificationSource source = ClassificationSource.Auto) => new()
    {
        Items = [new ClassifiedItem { FullPath = @"C:\Desktop\" + name, Identity = "test:" + name, CategoryId = category, Source = source }]
    };

    private static string StateWithout(string member)
    {
        if (member == "all") return "{}";
        var json = JsonSerializer.SerializeToNode(State("incomplete.png"), JsonFileStore<CabiDockState>.SerializerOptions)!.AsObject();
        var owner = member == "source" ? json["items"]![0]!.AsObject() : json;
        Assert.True(owner.Remove(member));
        return json.ToJsonString();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
