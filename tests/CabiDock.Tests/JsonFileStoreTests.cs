using System.IO;
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

    private static CabiDockState State(string name, string category = "images", ClassificationSource source = ClassificationSource.Auto) => new()
    {
        Items = [new ClassifiedItem { FullPath = @"C:\Desktop\" + name, Identity = "test:" + name, CategoryId = category, Source = source }]
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
