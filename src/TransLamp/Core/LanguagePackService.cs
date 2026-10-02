using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TransLamp.Core;

/// <summary>Installs data-only, hash-verified packs. No network access or executable package content.</summary>
public sealed class LanguagePackService
{
    public const string SupportedRuntime = "ctranslate2-sentencepiece-v1";
    private const long MaximumPackBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumManifestBytes = 128 * 1024;
    public string RootDirectory { get; }

    public LanguagePackService(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "TransLamp", "LanguagePacks"));
    }

    public IReadOnlyList<InstalledLanguagePack> GetInstalledPacks()
    {
        if (!Directory.Exists(RootDirectory)) return [];
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
        CancellationToken cancellationToken = default) => Task.Run(
            () => Import(archivePath, progress, cancellationToken), cancellationToken);

    public async Task<int> InstallBundledAsync(string directory, IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) return 0;
        var installed = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.tlpack").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var archive = ZipFile.OpenRead(path))
            {
                var manifest = ReadArchiveManifest(archive);
                ValidateManifest(manifest);
                if (File.Exists(Path.Combine(RootDirectory, ".removed-" + manifest.Id)) ||
                    GetInstalledPacks().Any(pack => pack.Manifest.Id == manifest.Id)) continue;
            }
            await ImportAsync(path, progress, cancellationToken).ConfigureAwait(false);
            installed++;
        }
        return installed;
    }

    public void Remove(string id)
    {
        ValidateId(id);
        if (!Directory.Exists(RootDirectory)) return;
        using var writeLock = AcquireWriteLock();
        var directory = Path.Combine(RootDirectory, id);
        if (!Directory.Exists(directory)) return;
        EnsureRegularDirectory(directory);
        EnsureNoReparsePoints(directory);
        Directory.Delete(directory, recursive: true);
        // Respect an explicit removal even when the Offline Kit still contains its original archive.
        File.WriteAllText(Path.Combine(RootDirectory, ".removed-" + id), DateTimeOffset.UtcNow.ToString("O"));
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

    private InstalledLanguagePack Import(string archivePath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RootDirectory);
        EnsureRegularDirectory(RootDirectory);
        using var writeLock = AcquireWriteLock();
        string? staging = null;
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var manifest = ReadArchiveManifest(archive);
            ValidateManifest(manifest);
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
            }
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, LanguagePackManifest.JsonOptions));
            // UTC timestamp is local installation metadata, not part of the model's signed/hashed payload.
            File.WriteAllText(Path.Combine(staging, "installed-at.txt"), DateTimeOffset.UtcNow.ToString("O"));
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(RootDirectory, manifest.Id);
            var backup = Path.Combine(RootDirectory, ".previous-" + Guid.NewGuid().ToString("N"));
            var hadPrevious = Directory.Exists(target);
            if (hadPrevious)
            {
                EnsureRegularDirectory(target);
                EnsureNoReparsePoints(target);
                Directory.Move(target, backup);
            }
            try { Directory.Move(staging, target); }
            catch
            {
                if (hadPrevious) Directory.Move(backup, target);
                throw;
            }
            staging = null;
            if (hadPrevious) TryDeleteOwnedDirectory(backup);
            var removedMarker = Path.Combine(RootDirectory, ".removed-" + manifest.Id);
            if (File.Exists(removedMarker)) File.Delete(removedMarker);
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
        try { return new FileStream(Path.Combine(root, ".write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite); }
        catch (IOException error) { throw new TransLampException("pack-busy", "語言包正在安裝或移除，請稍後重試。", error); }
    }

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
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static TransLampException InvalidPack() => new("invalid-pack", "語言包格式、內容或授權資訊不完整，無法安裝。");
}
