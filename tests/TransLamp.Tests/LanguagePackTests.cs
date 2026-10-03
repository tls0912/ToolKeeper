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
    // Structural fixtures intentionally contain no usable translation model. Only these tests bypass CPU loading.
    private LanguagePackService Service => new(Path.Combine(_directory, "installed"), AcceptStructuralFixture);
    private static Task AcceptStructuralFixture(InstalledLanguagePack pack, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

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

    [Theory]
    [InlineData("invalid-model")]
    [InlineData("validation-timeout")]
    public async Task ModelValidationFailurePreservesPreviousVersionAndRemovalState(string failureCode)
    {
        await Service.ImportAsync(MakePack());
        var called = false;
        var service = new LanguagePackService(Service.RootDirectory, (pack, token) =>
        {
            called = true;
            Assert.StartsWith(".install-", Path.GetFileName(pack.DirectoryPath));
            Assert.True(File.Exists(Path.Combine(pack.DirectoryPath, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(pack.DirectoryPath, "model/model.bin")));
            return Task.FromException(new TransLampException(failureCode, "Fixture model loader rejected the package."));
        });
        var error = await Assert.ThrowsAsync<TransLampException>(() => service.ImportAsync(
            MakePack(manifest => manifest with { PackageVersion = "2.0.0" })));
        Assert.Equal(failureCode, error.Code);
        Assert.True(called);
        Assert.Equal("1.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
        await LanguagePackService.VerifyAsync(Service.Find("en", "zh")!);
        AssertNoPendingTransaction();

        Service.Remove("en-zh");
        await Assert.ThrowsAsync<TransLampException>(() => service.ImportAsync(MakePack()));
        Assert.True(File.Exists(Path.Combine(Service.RootDirectory, ".removed-en-zh")));
        Assert.Empty(Service.GetInstalledPacks());
        AssertNoPendingTransaction();
    }

    [Fact]
    public async Task MissingRuntimeNeverBypassesValidationOfReplacement()
    {
        await Service.ImportAsync(MakePack());
        var engine = new TranslationEngine(Path.Combine(_directory, "missing-runtime"));
        var service = new LanguagePackService(Service.RootDirectory, engine.ValidatePackAsync);
        var error = await Assert.ThrowsAsync<TransLampException>(() => service.ImportAsync(
            MakePack(manifest => manifest with { PackageVersion = "2.0.0" })));
        Assert.Equal("runtime-missing", error.Code);
        Assert.Equal("1.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
        AssertNoPendingTransaction();
    }

    [Fact]
    public async Task CancellationWhileLoadingModelPreservesPreviousVersionAndReleasesLock()
    {
        await Service.ImportAsync(MakePack());
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new LanguagePackService(Service.RootDirectory, async (pack, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        var installing = service.ImportAsync(MakePack(manifest => manifest with { PackageVersion = "2.0.0" }), cancellationToken: cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installing);
        Assert.Equal("1.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
        await Service.ImportAsync(MakePack(manifest => manifest with { PackageVersion = "3.0.0" }));
        Assert.Equal("3.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
        AssertNoPendingTransaction();
    }

    [Theory]
    [InlineData("ja", "en")]
    [InlineData("en", "ja")]
    [InlineData("en", "zh-hant")]
    public async Task RejectsDirectionsUnsupportedByBundledRuntime(string source, string target)
    {
        var error = await Assert.ThrowsAsync<TransLampException>(() => Service.ImportAsync(MakePack(manifest => manifest with
        { Id = source + "-" + target, SourceLanguage = source, TargetLanguage = target })));
        Assert.Equal("unsupported-direction", error.Code);
        Assert.Empty(Service.GetInstalledPacks());
    }

    [Fact]
    public async Task LockedFileDuringRemovalNeverLeavesAPartiallyVisiblePack()
    {
        var pack = await Service.ImportAsync(MakePack());
        var before = Directory.GetFiles(pack.DirectoryPath, "*", SearchOption.AllDirectories).Order().ToArray();
        using (var held = new FileStream(Path.Combine(pack.DirectoryPath, "model/model.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => Service.Remove(pack.Manifest.Id));
            if (error is not null)
            {
                Assert.True(error is IOException or UnauthorizedAccessException, error.ToString());
                Assert.Equal(before, Directory.GetFiles(pack.DirectoryPath, "*", SearchOption.AllDirectories).Order());
                await LanguagePackService.VerifyAsync(pack);
                Assert.False(File.Exists(Path.Combine(Service.RootDirectory, ".removed-en-zh")));
            }
            else
            {
                Assert.False(Directory.Exists(pack.DirectoryPath));
                Assert.True(File.Exists(Path.Combine(Service.RootDirectory, ".removed-en-zh")));
            }
            Assert.Empty(Directory.GetFiles(Service.RootDirectory, ".transaction-*.json"));
        }
        // A later operation retries any deferred physical deletion.
        Service.Remove(pack.Manifest.Id);
        Assert.Empty(Service.GetInstalledPacks());
        Assert.Empty(Directory.GetDirectories(Service.RootDirectory, ".removing-*"));
    }

    [Fact]
    public async Task RemovalMetadataFailureRollsBackWithoutDeletingPayload()
    {
        var pack = await Service.ImportAsync(MakePack());
        Directory.CreateDirectory(Path.Combine(Service.RootDirectory, ".removed-en-zh"));
        var error = Record.Exception(() => Service.Remove(pack.Manifest.Id));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        await LanguagePackService.VerifyAsync(pack);
        Assert.Single(Service.GetInstalledPacks());
        AssertNoPendingTransaction();
        Assert.Empty(Directory.GetDirectories(Service.RootDirectory, ".removing-*"));
    }

    [Fact]
    public async Task FailureAfterReplacementMoveRestoresPreviousVersion()
    {
        var original = await Service.ImportAsync(MakePack());
        var marker = Path.Combine(Service.RootDirectory, ".removed-en-zh");
        File.WriteAllText(marker, "retained removal marker");
        using (var held = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(() => Service.ImportAsync(MakePack(manifest => manifest with { PackageVersion = "2.0.0" })));
            Assert.Equal("1.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
            await LanguagePackService.VerifyAsync(original);
        }
        Assert.Equal("retained removal marker", File.ReadAllText(marker));
        AssertNoPendingTransaction();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedReplacementRestoresPreviousVersionBeforeBundledPreparation(bool promoted)
    {
        var old = await Service.ImportAsync(MakePack(manifest => manifest with { PackageVersion = "9.0.0" }));
        var donor = new LanguagePackService(Path.Combine(_directory, "donor"), AcceptStructuralFixture);
        var newer = await donor.ImportAsync(MakePack(manifest => manifest with { PackageVersion = "10.0.0" }));
        var stagingName = ".install-" + Guid.NewGuid().ToString("N");
        var backupName = ".previous-en-zh-" + Guid.NewGuid().ToString("N");
        Directory.Move(newer.DirectoryPath, Path.Combine(Service.RootDirectory, stagingName));
        WriteJournal("install", stagingName, backupName, hadPrevious: true, hadRemovedMarker: false);
        Directory.Move(old.DirectoryPath, Path.Combine(Service.RootDirectory, backupName));
        if (promoted) Directory.Move(Path.Combine(Service.RootDirectory, stagingName), old.DirectoryPath);

        var bundles = Path.Combine(_directory, "bundled");
        Directory.CreateDirectory(bundles);
        File.Copy(MakePack(), Path.Combine(bundles, "older.tlpack"));
        var restarted = new LanguagePackService(Service.RootDirectory, AcceptStructuralFixture);
        Assert.Equal(0, (await restarted.InstallBundledWithResultAsync(bundles)).InstalledCount);
        Assert.Equal("9.0.0", Assert.Single(restarted.GetInstalledPacks()).Manifest.PackageVersion);
        await LanguagePackService.VerifyAsync(restarted.Find("en", "zh")!);
        AssertNoPendingTransaction();
    }

    [Fact]
    public async Task InterruptedFirstImportRestoresExplicitRemovalIntent()
    {
        var pack = await Service.ImportAsync(MakePack());
        // Simulate a new import after explicit removal: promotion happened, but journal was not committed.
        var stagingName = ".install-" + Guid.NewGuid().ToString("N");
        WriteJournal("install", stagingName, ".previous-en-zh-" + Guid.NewGuid().ToString("N"),
            hadPrevious: false, hadRemovedMarker: true);
        var restarted = new LanguagePackService(Service.RootDirectory, AcceptStructuralFixture);
        Assert.Empty(restarted.GetInstalledPacks());
        Assert.False(Directory.Exists(pack.DirectoryPath));
        Assert.True(File.Exists(Path.Combine(Service.RootDirectory, ".removed-en-zh")));
        Assert.Equal(0, await restarted.InstallBundledAsync(Path.Combine(_directory, "archives")));
        AssertNoPendingTransaction();
    }

    [Fact]
    public async Task InterruptedRemovalRestoresPackAndOriginalMarkerState()
    {
        var pack = await Service.ImportAsync(MakePack());
        var backupName = ".removing-en-zh-" + Guid.NewGuid().ToString("N");
        WriteJournal("remove", null, backupName, hadPrevious: true, hadRemovedMarker: false);
        Directory.Move(pack.DirectoryPath, Path.Combine(Service.RootDirectory, backupName));
        File.WriteAllText(Path.Combine(Service.RootDirectory, ".removed-en-zh"), "pending removal");
        var restarted = new LanguagePackService(Service.RootDirectory, AcceptStructuralFixture);
        await LanguagePackService.VerifyAsync(Assert.Single(restarted.GetInstalledPacks()));
        Assert.False(File.Exists(Path.Combine(Service.RootDirectory, ".removed-en-zh")));
        AssertNoPendingTransaction();
    }

    [Fact]
    public async Task LegacyInterruptedReplacementIsRecoveredOnFirstRead()
    {
        var pack = await Service.ImportAsync(MakePack(manifest => manifest with { PackageVersion = "9.0.0" }));
        Directory.Move(pack.DirectoryPath, Path.Combine(Service.RootDirectory, ".previous-" + Guid.NewGuid().ToString("N")));
        var abandoned = Path.Combine(Service.RootDirectory, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Combine(abandoned, "incomplete"), "partial extraction");
        Assert.Equal("9.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
        AssertNoPendingTransaction();
    }

    [Fact]
    public async Task DeferredBackupCleanupDoesNotBlockReadingDuringTranslation()
    {
        var pack = await Service.ImportAsync(MakePack());
        var backup = Path.Combine(Service.RootDirectory, ".previous-en-zh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        File.Copy(Path.Combine(pack.DirectoryPath, "manifest.json"), Path.Combine(backup, "manifest.json"));
        using var translationLease = new FileStream(Path.Combine(Service.RootDirectory, ".write.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Single(Service.GetInstalledPacks());
    }

    [Fact]
    public async Task DetailedBundledPreparationContinuesAfterCorruptArchive()
    {
        var valid = MakePack();
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(valid)!, "000-invalid.tlpack"), "not a zip");
        var result = await Service.InstallBundledWithResultAsync(Path.GetDirectoryName(valid)!);
        Assert.Equal(1, result.InstalledCount);
        Assert.EndsWith("000-invalid.tlpack", Assert.Single(result.Failures).ArchivePath);
        Assert.Equal("invalid-pack", result.Failures[0].Code);
        Assert.Single(Service.GetInstalledPacks());
    }

    [Fact]
    public async Task DetailedBundledPreparationPropagatesCancellation()
    {
        var archive = MakePack();
        using var cancel = new CancellationTokenSource();
        var service = new LanguagePackService(Service.RootDirectory, (pack, token) =>
        {
            cancel.Cancel();
            return Task.FromCanceled(token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.InstallBundledWithResultAsync(
            Path.GetDirectoryName(archive)!, cancellationToken: cancel.Token));
        Assert.Empty(Service.GetInstalledPacks());
        AssertNoPendingTransaction();
    }

    [Fact]
    public async Task ConcurrentBundledPreparationCannotOverrideManualImportOrRemoval()
    {
        var archive = MakePack();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manual = new LanguagePackService(Service.RootDirectory, async (pack, token) =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
        });
        var installing = manual.ImportAsync(MakePack(manifest => manifest with { PackageVersion = "9.0.0" }));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var busy = await Service.InstallBundledWithResultAsync(Path.GetDirectoryName(archive)!);
            Assert.Equal(0, busy.InstalledCount);
            Assert.All(busy.Failures, failure => Assert.Equal("pack-busy", failure.Code));
            Assert.NotEmpty(busy.Failures);
        }
        finally { release.TrySetResult(); }
        await installing;
        Assert.Equal(0, await Service.InstallBundledAsync(Path.GetDirectoryName(archive)!));
        Assert.Equal("9.0.0", Assert.Single(Service.GetInstalledPacks()).Manifest.PackageVersion);
        Service.Remove("en-zh");
        Assert.Equal(0, await Service.InstallBundledAsync(Path.GetDirectoryName(archive)!));
        Assert.Empty(Service.GetInstalledPacks());
    }

    private void WriteJournal(string kind, string? staging, string backup, bool hadPrevious, bool hadRemovedMarker) =>
        File.WriteAllText(Path.Combine(Service.RootDirectory, ".transaction-en-zh.json"),
            JsonSerializer.Serialize(new { id = "en-zh", kind, stagingDirectory = staging, backupDirectory = backup, hadPrevious, hadRemovedMarker }));

    private void AssertNoPendingTransaction()
    {
        Assert.Empty(Directory.GetFiles(Service.RootDirectory, ".transaction-*.json"));
        Assert.Empty(Directory.GetDirectories(Service.RootDirectory, ".install-*"));
        Assert.Empty(Directory.GetDirectories(Service.RootDirectory, ".previous-*"));
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
