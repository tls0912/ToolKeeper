using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TransLamp.Core;

/// <summary>Installs data-only, hash-verified packs. No network access or executable package content.</summary>
public sealed class LanguagePackService
{
    public const string SupportedRuntime = "ctranslate2-sentencepiece-v1";
    private const long MaximumPackBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumManifestBytes = 128 * 1024;
    private readonly Func<InstalledLanguagePack, CancellationToken, Task> _validator;
    public string RootDirectory { get; }

    public LanguagePackService(string? rootDirectory = null,
        Func<InstalledLanguagePack, CancellationToken, Task>? validator = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "TransLamp", "LanguagePacks"));
        _validator = validator ?? new TranslationEngine().ValidatePackAsync;
    }

    public IReadOnlyList<InstalledLanguagePack> GetInstalledPacks()
    {
        if (!Directory.Exists(RootDirectory)) return [];
        using (var readLock = AcquireReadLock(RootDirectory))
        {
            // Cleanup left after a committed update does not require exclusive access just to list packs.
            if (!NeedsRecovery()) return ReadInstalledPacks();
        }
        // Recovery must finish before readers can observe the interval between directory moves.
        using var writeLock = AcquireWriteLock();
        RecoverPendingOperations();
        return ReadInstalledPacks();
    }

    private bool NeedsRecovery()
    {
        if (Directory.EnumerateFiles(RootDirectory, ".transaction-*.json").Any()) return true;
        foreach (var backup in Directory.EnumerateDirectories(RootDirectory, ".previous-*"))
        {
            if (!OwnedDirectoryName(Path.GetFileName(backup), ".previous-")) continue;
            try
            {
                EnsureRegularDirectory(backup);
                var manifest = ReadManifest(Path.Combine(backup, "manifest.json"));
                ValidateManifest(manifest);
                if (!Directory.Exists(Path.Combine(RootDirectory, manifest.Id)) && !File.Exists(RemovedMarker(manifest.Id))) return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TransLampException) { }
        }
        return false;
    }

    private IReadOnlyList<InstalledLanguagePack> ReadInstalledPacks()
    {
        var packs = new List<InstalledLanguagePack>();
        foreach (var directory in Directory.EnumerateDirectories(RootDirectory))
        {
            if (Path.GetFileName(directory).StartsWith('.')) continue;
            try
            {
                EnsureRegularDirectory(directory);
                var manifest = ReadManifest(Path.Combine(directory, "manifest.json"));
                ValidateManifest(manifest);
                if (!StringComparer.Ordinal.Equals(Path.GetFileName(directory), manifest.Id)) continue;
                if (manifest.Files.Any(file => !File.Exists(Path.Combine(directory, file.Path)))) continue;
                packs.Add(new(manifest, directory, manifest.Files.Sum(file => file.Size)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TransLampException)
            {
                // One incomplete/corrupt pack must not hide other installed languages.
            }
        }
        return packs.OrderBy(pack => pack.Manifest.Id, StringComparer.Ordinal).ToArray();
    }

    public InstalledLanguagePack? Find(string sourceLanguage, string targetLanguage) => GetInstalledPacks()
        .FirstOrDefault(pack => pack.Manifest.SourceLanguage == sourceLanguage && pack.Manifest.TargetLanguage == targetLanguage);

    public Task<InstalledLanguagePack> ImportAsync(string archivePath, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
            (await Import(archivePath, progress, cancellationToken, onlyIfMissing: false).ConfigureAwait(false))!, cancellationToken);

    public async Task<int> InstallBundledAsync(string directory, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = await InstallBundledWithResultAsync(directory, progress, cancellationToken).ConfigureAwait(false);
        if (result.Failures.Count > 0)
            throw new TransLampException("bundled-pack-failed", $"已安裝 {result.InstalledCount} 個語言包；{result.Failures.Count} 個語言包準備失敗，請重新取得或匯入。");
        return result.InstalledCount;
    }

    public async Task<LanguagePackInstallResult> InstallBundledWithResultAsync(string directory,
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(directory)) return new(0, []);
        var installed = 0;
        var failures = new List<LanguagePackInstallFailure>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.tlpack").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await Task.Run(() => Import(path, progress, cancellationToken, onlyIfMissing: true), cancellationToken)
                    .ConfigureAwait(false) is not null) installed++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TransLampException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                failures.Add(new(path, (error as TransLampException)?.Code ?? "invalid-pack",
                    error is TransLampException ? error.Message : "語言包無法讀取，請重新取得完整語言包。"));
            }
        }
        return new(installed, failures);
    }

    public void Remove(string id)
    {
        ValidateId(id);
        if (!Directory.Exists(RootDirectory)) return;
        using var writeLock = AcquireWriteLock();
        RecoverPendingOperations();
        var directory = Path.Combine(RootDirectory, id);
        if (!Directory.Exists(directory)) return;
        EnsureRegularDirectory(directory);
        EnsureNoReparsePoints(directory);
        var transaction = new PackTransaction(id, "remove", null, ".removing-" + id + "-" + Guid.NewGuid().ToString("N"),
            HadPrevious: true, File.Exists(RemovedMarker(id)));
        WriteTransaction(transaction);
        try
        {
            // Never recursively delete the visible pack: a locked file must not leave it half deleted.
            Directory.Move(directory, Path.Combine(RootDirectory, transaction.BackupDirectory));
            WriteDurableFile(RemovedMarker(id), DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(TransactionPath(id));
        }
        catch
        {
            RollBack(transaction);
            throw;
        }
        TryDeleteOwnedDirectory(Path.Combine(RootDirectory, transaction.BackupDirectory));
    }

    public static Task VerifyAsync(InstalledLanguagePack pack, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            ValidateManifest(pack.Manifest);
            EnsureRegularDirectory(pack.DirectoryPath);
            foreach (var file in pack.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(pack.DirectoryPath, file.Path);
                EnsureSafeFile(pack.DirectoryPath, path);
                using var stream = File.OpenRead(path);
                VerifyStream(stream, file, null, cancellationToken);
            }
        }, cancellationToken);

    private async Task<InstalledLanguagePack?> Import(string archivePath, IProgress<string>? progress,
        CancellationToken cancellationToken, bool onlyIfMissing)
    {
        Directory.CreateDirectory(RootDirectory);
        EnsureRegularDirectory(RootDirectory);
        using var writeLock = AcquireWriteLock();
        RecoverPendingOperations();
        string? staging = null;
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var manifest = ReadArchiveManifest(archive);
            ValidateManifest(manifest);
            // The existence/removal decision and the eventual commit share the same process-wide lock.
            if (onlyIfMissing && (File.Exists(RemovedMarker(manifest.Id)) ||
                ReadInstalledPacks().Any(pack => pack.Manifest.Id == manifest.Id))) return null;
            var listed = manifest.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (!seen.Add(entry.FullName) || IsSymlink(entry)) throw InvalidPack();
                if (entry.FullName == "manifest.json") continue;
                if (!listed.TryGetValue(entry.FullName, out var file) || entry.FullName != file.Path || entry.Length != file.Size)
                    throw InvalidPack();
            }
            if (archive.Entries.Count != listed.Count + 1) throw InvalidPack();

            staging = Path.Combine(RootDirectory, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(file.Path);
                var destination = Path.Combine(staging, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = archive.GetEntry(file.Path)!.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                VerifyStream(input, file, output, cancellationToken);
                output.Flush(flushToDisk: true);
            }
            WriteDurableFile(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, LanguagePackManifest.JsonOptions));
            // UTC timestamp is local installation metadata, not part of the model's signed/hashed payload.
            WriteDurableFile(Path.Combine(staging, "installed-at.txt"), DateTimeOffset.UtcNow.ToString("O"));
            var pack = new InstalledLanguagePack(manifest, staging, manifest.Files.Sum(file => file.Size));
            await _validator(pack, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(RootDirectory, manifest.Id);
            var transaction = new PackTransaction(manifest.Id, "install", Path.GetFileName(staging),
                ".previous-" + manifest.Id + "-" + Guid.NewGuid().ToString("N"),
                Directory.Exists(target), File.Exists(RemovedMarker(manifest.Id)));
            var backup = Path.Combine(RootDirectory, transaction.BackupDirectory);
            if (transaction.HadPrevious)
            {
                EnsureRegularDirectory(target);
                EnsureNoReparsePoints(target);
            }
            WriteTransaction(transaction);
            try
            {
                if (transaction.HadPrevious) Directory.Move(target, backup);
                Directory.Move(staging, target);
                if (File.Exists(RemovedMarker(manifest.Id))) File.Delete(RemovedMarker(manifest.Id));
                // Deleting the journal commits the operation. Until then the previous state is recoverable.
                File.Delete(TransactionPath(manifest.Id));
            }
            catch
            {
                // Keep the journal and both directories if rollback itself is blocked; startup can retry.
                staging = null;
                RollBack(transaction);
                throw;
            }
            staging = null;
            if (transaction.HadPrevious) TryDeleteOwnedDirectory(backup);
            return new(manifest, target, manifest.Files.Sum(file => file.Size));
        }
        catch (InvalidDataException error) { throw new TransLampException("invalid-pack", "語言包損壞或格式不相容，原有語言包未變更。", error); }
        catch (JsonException error) { throw new TransLampException("invalid-pack", "語言包資訊無法讀取，請重新取得完整語言包。", error); }
        finally { if (staging is not null) TryDeleteOwnedDirectory(staging); }
    }

    private FileStream AcquireWriteLock()
    {
        EnsureRegularDirectory(RootDirectory);
        try { return new FileStream(Path.Combine(RootDirectory, ".write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new TransLampException("pack-busy", "另一個視窗正在管理語言包，請稍後重試。", error); }
    }

    internal static FileStream AcquireTranslationLease(InstalledLanguagePack pack)
    {
        EnsureRegularDirectory(pack.DirectoryPath);
        var root = Directory.GetParent(pack.DirectoryPath)?.FullName ?? throw InvalidPack();
        return AcquireReadLock(root);
    }

    private static FileStream AcquireReadLock(string root)
    {
        EnsureRegularDirectory(root);
        try { return new FileStream(Path.Combine(root, ".write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite); }
        catch (IOException error) { throw new TransLampException("pack-busy", "語言包正在安裝或移除，請稍後重試。", error); }
    }

    private sealed record PackTransaction(string Id, string Kind, string? StagingDirectory, string BackupDirectory,
        bool HadPrevious, bool HadRemovedMarker);

    private string RemovedMarker(string id) => Path.Combine(RootDirectory, ".removed-" + id);
    private string TransactionPath(string id) => Path.Combine(RootDirectory, ".transaction-" + id + ".json");

    private void WriteTransaction(PackTransaction transaction)
    {
        var path = TransactionPath(transaction.Id);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            WriteDurableFile(temporary, JsonSerializer.Serialize(transaction, LanguagePackManifest.JsonOptions));
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Caller holds the exclusive root lock. An existing journal always means "restore the previous state".
    private void RecoverPendingOperations()
    {
        foreach (var path in Directory.EnumerateFiles(RootDirectory, ".transaction-*.json").ToArray())
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumManifestBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw InvalidPack();
            var transaction = JsonSerializer.Deserialize<PackTransaction>(File.ReadAllText(path), LanguagePackManifest.JsonOptions)
                ?? throw InvalidPack();
            if (transaction.Id is not ("en-zh" or "zh-en") || path != TransactionPath(transaction.Id) ||
                transaction.Kind is not ("install" or "remove") ||
                !OwnedDirectoryName(transaction.BackupDirectory, transaction.Kind == "install" ? ".previous-" : ".removing-") ||
                !transaction.BackupDirectory.StartsWith((transaction.Kind == "install" ? ".previous-" : ".removing-") + transaction.Id + "-", StringComparison.Ordinal) ||
                (transaction.Kind == "install" && !OwnedDirectoryName(transaction.StagingDirectory, ".install-")) ||
                (transaction.Kind == "remove" && (transaction.StagingDirectory is not null || !transaction.HadPrevious))) throw InvalidPack();
            RollBack(transaction);
        }

        // Also recover backups left by 0.1.0, which had two directory moves but no journal.
        foreach (var backup in Directory.EnumerateDirectories(RootDirectory, ".previous-*").ToArray())
        {
            if (!OwnedDirectoryName(Path.GetFileName(backup), ".previous-")) continue;
            EnsureRegularDirectory(backup);
            EnsureNoReparsePoints(backup);
            LanguagePackManifest manifest;
            try { manifest = ReadManifest(Path.Combine(backup, "manifest.json")); ValidateManifest(manifest); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TransLampException)
            { continue; } // Keep unidentifiable backups for manual recovery instead of destroying them.
            var target = Path.Combine(RootDirectory, manifest.Id);
            if (!Directory.Exists(target) && !File.Exists(RemovedMarker(manifest.Id)))
                Directory.Move(backup, target);
            else
                TryDeleteOwnedDirectory(backup);
        }
        // Without a journal these directories are either abandoned extraction or committed removal/cleanup.
        foreach (var directory in Directory.EnumerateDirectories(RootDirectory).ToArray())
        {
            var name = Path.GetFileName(directory);
            if (OwnedDirectoryName(name, ".install-") || OwnedDirectoryName(name, ".removing-"))
                TryDeleteOwnedDirectory(directory);
        }
    }

    private void RollBack(PackTransaction transaction)
    {
        var target = Path.Combine(RootDirectory, transaction.Id);
        var backup = Path.Combine(RootDirectory, transaction.BackupDirectory);
        var staging = transaction.StagingDirectory is null ? null : Path.Combine(RootDirectory, transaction.StagingDirectory);
        EnsureRegularDirectory(target);
        EnsureRegularDirectory(backup);
        if (staging is not null) EnsureRegularDirectory(staging);
        if (Directory.Exists(backup))
        {
            if (Directory.Exists(target))
            {
                if (staging is null || Directory.Exists(staging)) throw RecoveryFailed();
                Directory.Move(target, staging);
            }
            Directory.Move(backup, target);
        }
        else if (!transaction.HadPrevious && staging is not null && !Directory.Exists(staging) && Directory.Exists(target))
            Directory.Move(target, staging);
        else if (transaction.HadPrevious && !Directory.Exists(target))
            throw RecoveryFailed();

        var marker = RemovedMarker(transaction.Id);
        if (transaction.HadRemovedMarker && !File.Exists(marker))
            WriteDurableFile(marker, DateTimeOffset.UtcNow.ToString("O"));
        else if (!transaction.HadRemovedMarker && File.Exists(marker))
            File.Delete(marker);
        File.Delete(TransactionPath(transaction.Id));
        if (staging is not null) TryDeleteOwnedDirectory(staging);
    }

    private static bool OwnedDirectoryName(string? name, string prefix) => name is not null &&
        Regex.IsMatch(name, "\\A" + Regex.Escape(prefix) + (prefix == ".install-" ? "" : "(?:(?:en-zh|zh-en)-)?") + "[0-9a-f]{32}\\z");

    private static void WriteDurableFile(string path, string value)
    {
        EnsureRegularDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw InvalidPack();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(value));
        stream.Flush(flushToDisk: true);
    }

    private static TransLampException RecoveryFailed() => new("pack-recovery-failed",
        "語言包的安裝或移除尚未完成，原有資料已保留。請關閉其他 TransLamp 視窗後重試。");

    private static LanguagePackManifest ReadArchiveManifest(ZipArchive archive)
    {
        if (archive.Entries.Count > 128) throw InvalidPack();
        var entries = archive.Entries.Where(entry => entry.FullName == "manifest.json").ToArray();
        if (entries.Length != 1 || entries[0].Length > MaximumManifestBytes || entries[0].Length <= 0) throw InvalidPack();
        using var stream = entries[0].Open();
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = stream.Read(bytes)) > 0)
        {
            if (buffer.Length + read > MaximumManifestBytes) throw InvalidPack();
            buffer.Write(bytes, 0, read);
        }
        return JsonSerializer.Deserialize<LanguagePackManifest>(buffer.ToArray(), LanguagePackManifest.JsonOptions) ?? throw InvalidPack();
    }

    private static LanguagePackManifest ReadManifest(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumManifestBytes || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw InvalidPack();
        return JsonSerializer.Deserialize<LanguagePackManifest>(File.ReadAllText(path), LanguagePackManifest.JsonOptions) ?? throw InvalidPack();
    }

    private static void ValidateManifest(LanguagePackManifest manifest)
    {
        ValidateId(manifest.Id);
        if (manifest.SchemaVersion != 1 || manifest.Runtime != SupportedRuntime)
            throw new TransLampException("incompatible-runtime", "此語言包需要其他版本的 TransLamp，請取得相容的語言包。");
        if (!ValidLanguage(manifest.SourceLanguage) || !ValidLanguage(manifest.TargetLanguage) ||
            manifest.SourceLanguage == manifest.TargetLanguage || manifest.Id != $"{manifest.SourceLanguage}-{manifest.TargetLanguage}" ||
            !Version.TryParse(manifest.PackageVersion, out _) ||
            new[] { manifest.DisplayName, manifest.ModelName, manifest.ModelVersion, manifest.LicenseIdentifier }
                .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512) ||
            !Uri.TryCreate(manifest.ModelSource, UriKind.Absolute, out var source) || source.Scheme != "https") throw InvalidPack();
        if ((manifest.SourceLanguage, manifest.TargetLanguage) is not (("en", "zh") or ("zh", "en")))
            throw new TransLampException("unsupported-direction", "目前僅支援中文與 English 之間的雙向語言包。");
        if (manifest.Files is null || manifest.Files.Count is < 5 or > 100) throw InvalidPack();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || !ValidPayloadPath(file.Path) || !paths.Add(file.Path) || file.Size <= 0 ||
                file.Size > MaximumPackBytes || string.IsNullOrEmpty(file.Sha256) || !Regex.IsMatch(file.Sha256, "\\A[0-9a-fA-F]{64}\\z")) throw InvalidPack();
            total += file.Size;
            if (total > MaximumPackBytes) throw InvalidPack();
        }
        foreach (var required in new[] { "LICENSE", "NOTICE", "model/model.bin", "model/config.json", "sentencepiece.model" })
            if (!manifest.Files.Any(file => file.Path == required)) throw InvalidPack();
        if (!manifest.Files.Any(file => file.Path is "model/shared_vocabulary.json" or "model/vocabulary.json" or "model/source_vocabulary.json")) throw InvalidPack();
    }

    private static bool ValidLanguage(string? value) => value is not null && Regex.IsMatch(value, "\\A[a-z]{2,3}(?:-[a-z]{2,4})?\\z");
    private static void ValidateId(string? id)
    {
        if (id is null || !Regex.IsMatch(id, "\\A[a-z]{2,3}(?:-[a-z]{2,4})?-[a-z]{2,3}(?:-[a-z]{2,4})?\\z")) throw InvalidPack();
    }

    private static bool ValidPayloadPath(string? path) => path is "LICENSE" or "NOTICE" or "sentencepiece.model" or
        "model/model.bin" or "model/config.json" or "model/shared_vocabulary.json" or "model/vocabulary.json" or
        "model/source_vocabulary.json" or "model/target_vocabulary.json";

    private static void VerifyStream(Stream input, LanguagePackFile file, Stream? output, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > file.Size) throw InvalidPack();
            hash.AppendData(buffer, 0, read);
            output?.Write(buffer, 0, read);
        }
        if (total != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new TransLampException("checksum-mismatch", "語言包完整性檢查失敗，請重新取得完整語言包。");
    }

    private static bool IsSymlink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
    private static void EnsureRegularDirectory(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw InvalidPack();
    }
    private static void EnsureSafeFile(string root, string path)
    {
        EnsureRegularDirectory(Path.GetDirectoryName(path)!);
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw InvalidPack();
    }
    private static void EnsureNoReparsePoints(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint)) throw InvalidPack();
            if (attributes.HasFlag(FileAttributes.Directory)) EnsureNoReparsePoints(entry);
        }
    }
    private static void TryDeleteOwnedDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            EnsureRegularDirectory(path);
            EnsureNoReparsePoints(path);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (TransLampException) { }
    }
    private static TransLampException InvalidPack() => new("invalid-pack", "語言包格式、內容或授權資訊不完整，無法安裝。");
}
