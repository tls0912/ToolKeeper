using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TransLamp.Core;
using Xunit;

namespace TransLamp.Tests;

public sealed class LanguagePackTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TransLamp.Tests", Guid.NewGuid().ToString("N"));
    private LanguagePackService Service => new(Path.Combine(_directory, "installed"));

    [Fact]
    public async Task InstalledPackWorksAfterOriginalArchiveIsRemoved()
    {
        var archive = MakePack();
        var installed = await Service.ImportAsync(archive);
        File.Delete(archive);
        await LanguagePackService.VerifyAsync(installed);
        Assert.Equal("en-zh", Assert.Single(Service.GetInstalledPacks()).Manifest.Id);
        Assert.Equal(installed.DirectoryPath, Service.Find("en", "zh")!.DirectoryPath);
        Assert.Null(Service.Find("zh", "en"));
        Assert.True(File.Exists(Path.Combine(installed.DirectoryPath, "installed-at.txt")));
        Assert.True(installed.SizeBytes > 0);
    }

    [Fact]
    public async Task BadHashCannotReplaceExistingModel()
    {
        var original = await Service.ImportAsync(MakePack());
        var before = File.ReadAllBytes(Path.Combine(original.DirectoryPath, "model/model.bin"));
        var corrupt = MakePack(manifest => manifest with
        {
            Files = manifest.Files.Select(file => file.Path == "model/model.bin" ? file with { Sha256 = new string('0', 64) } : file).ToList()
        });
        var error = await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(corrupt));
        Assert.Equal("checksum-mismatch", error.Code);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(original.DirectoryPath, "model/model.bin")));
        Assert.Single(Service.GetInstalledPacks());
        Assert.Empty(Directory.GetDirectories(Service.RootDirectory, ".install-*"));
    }

    [Fact]
    public async Task ValidReplacementIsAtomicAndCancellationPreservesIt()
    {
        await Service.ImportAsync(MakePack());
        var newer = MakePack(manifest => manifest with { PackageVersion = "2.0.0" });
        await Service.ImportAsync(newer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.ImportAsync(newer, cancellationToken: cancellation.Token));
        Assert.Equal("2.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
        Assert.Empty(Directory.GetDirectories(Service.RootDirectory, ".previous-*"));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("model/../../outside.txt")]
    [InlineData("model\\model.bin")]
    [InlineData("model/model.bin:stream")]
    [InlineData("translate.py")]
    [InlineData("model/runtime.dll")]
    public async Task RejectsTraversalAndExecutablePayloads(string path)
    {
        await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(MakePack(extraPath: path)));
        Assert.Empty(Service.GetInstalledPacks());
        Assert.False(File.Exists(Path.Combine(_directory, "outside.txt")));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("license")]
    [InlineData("direction")]
    [InlineData("size")]
    [InlineData("duplicates")]
    [InlineData("null-files")]
    public async Task RejectsIncompatibleOrIncompleteMetadata(string invalid)
    {
        var archive = MakePack(manifest => invalid switch
        {
            "runtime" => manifest with { Runtime = "unknown-v2" },
            "license" => manifest with { LicenseIdentifier = "" },
            "direction" => manifest with { TargetLanguage = "en" },
            "size" => manifest with { Files = manifest.Files.Select(file => file with { Size = file.Size + 1 }).ToList() },
            "duplicates" => manifest with { Files = [.. manifest.Files, manifest.Files[0]] },
            "null-files" => manifest with { Files = null! },
            _ => throw new InvalidOperationException()
        });
        await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(archive));
        Assert.Empty(Service.GetInstalledPacks());
    }

    [Fact]
    public async Task RejectsDuplicateUnlistedAndSymbolicLinkEntries()
    {
        var archive = MakePack();
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) WriteEntry(zip, "NOTICE", "Duplicate");
        await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(archive));
        archive = MakePack();
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) WriteEntry(zip, "unlisted.txt", "unlisted");
        await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(archive));
        archive = MakePack();
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update)) zip.GetEntry("NOTICE")!.ExternalAttributes = unchecked((int)0xA1FF0000);
        await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(archive));
    }

    [Fact]
    public async Task VerificationDetectsInstalledModelTampering()
    {
        var pack = await Service.ImportAsync(MakePack());
        File.WriteAllText(Path.Combine(pack.DirectoryPath, "model/model.bin"), "tampered model");
        await Assert.ThrowsAsync<TransLampException>(() => LanguagePackService.VerifyAsync(pack));
    }

    [Fact]
    public async Task RemoveOnlyDeletesSelectedPackAndRejectsTraversal()
    {
        await Service.ImportAsync(MakePack());
        await Service.ImportAsync(MakePack(manifest => manifest with { Id = "zh-en", SourceLanguage = "zh", TargetLanguage = "en" }));
        Assert.Throws<TransLampException>(() => Service.Remove("../installed"));
        Service.Remove("en-zh");
        Assert.Equal("zh-en", Assert.Single(Service.GetInstalledPacks()).Manifest.Id);
    }

    [Fact]
    public async Task BundledPacksDoNotOverwriteUsersInstalledVersion()
    {
        var archive = MakePack();
        var installed = await Service.ImportAsync(archive);
        File.WriteAllText(Path.Combine(installed.DirectoryPath, "manifest.json"),
            JsonSerializer.Serialize(installed.Manifest with { PackageVersion = "9.0.0" }, LanguagePackManifest.JsonOptions));
        Assert.Equal(0, await Service.InstallBundledAsync(Path.GetDirectoryName(archive)!));
        Assert.Equal("9.0.0", Service.Find("en", "zh")!.Manifest.PackageVersion);
    }

    [Fact]
    public async Task RemovedBundledPackStaysRemovedUntilExplicitImport()
    {
        var archive = MakePack();
        await Service.ImportAsync(archive);
        Service.Remove("en-zh");
        Assert.Equal(0, await Service.InstallBundledAsync(Path.GetDirectoryName(archive)!));
        Assert.Empty(Service.GetInstalledPacks());
        await Service.ImportAsync(archive);
        Assert.Single(Service.GetInstalledPacks());
        Assert.False(File.Exists(Path.Combine(Service.RootDirectory, ".removed-en-zh")));
    }

    [Fact]
    public async Task CompetingInstallerReturnsBusyAndMalformedPackDoesNotHideHealthyOne()
    {
        await Service.ImportAsync(MakePack());
        using (var writeLock = new FileStream(Path.Combine(Service.RootDirectory, ".write.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var error = await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(MakePack()));
            Assert.Equal("pack-busy", error.Code);
        }
        Directory.CreateDirectory(Path.Combine(Service.RootDirectory, "zh-en"));
        File.WriteAllText(Path.Combine(Service.RootDirectory, "zh-en", "manifest.json"), "broken");
        Assert.Single(Service.GetInstalledPacks());
    }

    [Fact]
    public async Task CancellationDuringExtractionLeavesNoPartialInstall()
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<string>(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.ImportAsync(MakePack(), progress, cancellation.Token));
        Assert.Empty(Service.GetInstalledPacks());
        Assert.Empty(Directory.GetDirectories(Service.RootDirectory, ".install-*"));
    }

    private string MakePack(Func<LanguagePackManifest, LanguagePackManifest>? changeManifest = null, string? extraPath = null)
    {
        Directory.CreateDirectory(Path.Combine(_directory, "archives"));
        var payload = new Dictionary<string, string>
        {
            ["LICENSE"] = "Fixture license; not a translation model.", ["NOTICE"] = "Unit test data only.",
            ["sentencepiece.model"] = "test tokenizer", ["model/model.bin"] = "test model",
            ["model/config.json"] = "{}", ["model/shared_vocabulary.json"] = "[]"
        };
        if (extraPath is not null) payload[extraPath] = "must be rejected";
        var manifest = new LanguagePackManifest
        {
            SchemaVersion = 1, Id = "en-zh", SourceLanguage = "en", TargetLanguage = "zh", DisplayName = "English → 中文",
            PackageVersion = "1.0.0", ModelName = "Fixture", ModelVersion = "1", Runtime = LanguagePackService.SupportedRuntime,
            ModelSource = "https://example.org/unit-test-only", LicenseIdentifier = "MIT",
            Files = payload.Select(pair => new LanguagePackFile
            {
                Path = pair.Key, Size = Encoding.UTF8.GetByteCount(pair.Value),
                Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value)))
            }).ToList()
        };
        manifest = changeManifest?.Invoke(manifest) ?? manifest;
        var path = Path.Combine(_directory, "archives", Guid.NewGuid().ToString("N") + ".tlpack");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest, LanguagePackManifest.JsonOptions));
        foreach (var pair in payload) WriteEntry(archive, pair.Key, pair.Value);
        return path;
    }
    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}
