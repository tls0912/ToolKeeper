using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("TransLamp.Tests")]

namespace TransLamp.Core;

/// <summary>Downloads only reviewed data archives and installs through the existing transactional importer.</summary>
public sealed class LanguagePackDownloadService
{
    private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    private static readonly string[] PayloadPaths = ["sentencepiece.model", "model/model.bin", "model/config.json",
        "model/shared_vocabulary.json", "model/shared_vocabulary.txt", "model/vocabulary.json",
        "model/source_vocabulary.json", "model/target_vocabulary.json"];
    private readonly LanguagePackService _installer;
    private readonly HttpClient _client;
    private readonly IReadOnlyList<DownloadableLanguagePack> _catalog;

    public LanguagePackDownloadService(LanguagePackService installer, HttpClient? client = null)
        : this(installer, client ?? SharedClient, LanguagePackCatalog.Packs) { }

    // Tests provide a bounded structural archive and their own HTTP transport; production always uses the bundled catalog.
    internal LanguagePackDownloadService(LanguagePackService installer, HttpClient client,
        IReadOnlyList<DownloadableLanguagePack> catalog)
    {
        _installer = installer;
        _client = client;
        _catalog = catalog;
    }

    public async Task<InstalledLanguagePack> DownloadAndInstallAsync(DownloadableLanguagePack pack,
        IProgress<LanguagePackDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!_catalog.Contains(pack))
            throw new TransLampException("unsupported-download", "這個語言包不在可下載的核對目錄中。");
        cancellationToken.ThrowIfCancellationRequested();
        var temporary = Path.Combine(Path.GetTempPath(), "TransLamp.Downloads", Guid.NewGuid().ToString("N"));
        EnsureRegularDirectory(temporary);
        Directory.CreateDirectory(temporary);
        EnsureRegularDirectory(temporary);
        try
        {
            var sourceArchive = Path.Combine(temporary, "source.argosmodel");
            using (var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                downloadTimeout.CancelAfter(TimeSpan.FromMinutes(30));
                await DownloadAsync(pack, sourceArchive, progress, downloadTimeout.Token).ConfigureAwait(false);
            }
            progress?.Report(new(pack.SizeBytes, pack.SizeBytes, "Installing"));
            var installArchive = Path.Combine(temporary, "install.tlpack");
            await Task.Run(() => ConvertArchive(pack, sourceArchive, installArchive, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            return await _installer.ImportAsync(installArchive, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error)
        {
            throw new TransLampException("download-failed", "語言包下載失敗，請檢查網路連線後重試。", error);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransLampException("download-timeout", "語言包下載逾時，請稍後重試。", error);
        }
        catch (InvalidDataException error)
        {
            throw new TransLampException("invalid-download", "下載的語言包內容不相容，原有語言包未變更。", error);
        }
        catch (JsonException error)
        {
            throw new TransLampException("invalid-download", "下載的語言包資訊不相容，原有語言包未變更。", error);
        }
        catch (DecoderFallbackException error)
        {
            throw new TransLampException("invalid-download", "下載的語言包資訊編碼不相容，原有語言包未變更。", error);
        }
        finally
        {
            // Only this invocation's two regular, locally created files can be removed.
            EnsureRegularDirectory(temporary);
            foreach (var name in new[] { "source.argosmodel", "install.tlpack" })
            {
                var path = Path.Combine(temporary, name);
                if (File.Exists(path)) File.Delete(path);
            }
            Directory.Delete(temporary);
        }
    }

    private async Task DownloadAsync(DownloadableLanguagePack pack, string destination,
        IProgress<LanguagePackDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pack.DownloadUri);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is not null && (finalUri.Scheme != "https" || finalUri.Host != pack.DownloadUri.Host))
            throw new TransLampException("invalid-download", "語言包下載來源不符合已核對的 HTTPS 來源。");
        if (response.Content.Headers.ContentLength is { } size && size != pack.SizeBytes) throw ChecksumMismatch();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long downloaded = 0;
        progress?.Report(new(0, pack.SizeBytes, "Downloading"));
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            downloaded += read;
            if (downloaded > pack.SizeBytes) throw ChecksumMismatch();
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(new(downloaded, pack.SizeBytes, "Downloading"));
        }
        if (downloaded != pack.SizeBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase))
            throw ChecksumMismatch();
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ConvertArchive(DownloadableLanguagePack pack, string source, string destination, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count is < 5 or > 1000) throw InvalidArchive();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName.TrimEnd('/');
            if (name.Length == 0 || name.Contains('\\') || name.Contains(':') ||
                name.Split('/').Any(part => part is "" or "." or "..") ||
                !name.StartsWith(pack.ArchiveRoot + "/", StringComparison.Ordinal) && name != pack.ArchiveRoot ||
                !seen.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                entry.Length < 0 || entry.Length > MaximumExpandedBytes) throw InvalidArchive();
            expandedBytes += entry.Length;
            if (expandedBytes > MaximumExpandedBytes) throw InvalidArchive();
        }
        using (var metadata = JsonDocument.Parse(ReadText(RequiredEntry(archive, pack, "metadata.json"), 128 * 1024),
            new JsonDocumentOptions { MaxDepth = 8 }))
        {
            var value = metadata.RootElement;
            if (value.ValueKind != JsonValueKind.Object || !Matches(value, "from_code", pack.SourceLanguage) ||
                !Matches(value, "to_code", pack.TargetLanguage) || !Matches(value, "package_version", pack.Version)) throw InvalidArchive();
        }
        var readme = ReadText(RequiredEntry(archive, pack, "README.md"), 512 * 1024);
        if (pack.License.Contains("CC-BY-4.0", StringComparison.Ordinal) && !readme.Contains("CC-BY 4.0", StringComparison.Ordinal))
            throw InvalidArchive();
        var manifest = new LanguagePackManifest
        {
            SchemaVersion = 1, Id = pack.Id, SourceLanguage = pack.SourceLanguage, TargetLanguage = pack.TargetLanguage,
            DisplayName = pack.DisplayName, PackageVersion = "1.0.0",
            ModelName = pack.License.Contains("CC-BY-4.0", StringComparison.Ordinal)
                ? $"OPUS-MT {pack.Id} / Argos Translate" : $"Argos Translate {pack.Id}",
            ModelVersion = pack.Version, Runtime = LanguagePackService.SupportedRuntime, ModelSource = pack.Source,
            LicenseIdentifier = pack.License
        };
        using var output = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var relative in PayloadPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.GetEntry(pack.ArchiveRoot + "/" + relative);
            if (entry is null) continue;
            if (entry.Length <= 0) throw InvalidArchive();
            using var input = entry.Open();
            using var payload = output.CreateEntry(relative, CompressionLevel.Fastest).Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long total = 0;
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += read;
                if (total > entry.Length) throw InvalidArchive();
                hash.AppendData(buffer, 0, read);
                payload.Write(buffer, 0, read);
            }
            if (total != entry.Length) throw InvalidArchive();
            manifest.Files.Add(new() { Path = relative, Size = total, Sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() });
        }
        var license = "Argos model/packaging/contributions: MIT (chosen from MIT OR CC0).\n\n" + ReadLicense("Argos-MIT.txt");
        if (pack.License.Contains("CC-BY-4.0", StringComparison.Ordinal))
            license = "OPUS-MT source model: CC-BY-4.0.\n\n" + ReadLicense("CC-BY-4.0.txt") + "\n\n" + license;
        AddText(output, manifest, "LICENSE", license);
        AddText(output, manifest, "NOTICE", $"TransLamp {pack.Id} language pack 1.0.0\n\n" +
            $"Model version: {pack.Version}\nSource: {pack.Source}\nSource package SHA256: {pack.Sha256}\n" +
            "Package index: https://github.com/argosopentech/argospm-index\n" +
            "Argos model license clarification: https://github.com/argosopentech/argos-translate/issues/533#issuecomment-5160080718\n\n" +
            "Changes: original model/tokenizer bytes retained; unused Stanza data omitted; manifest, checksums and license texts added.\n" +
            "No model retraining or upstream endorsement is implied.\n\nOriginal upstream README:\n\n" + readme);
        using var writer = new StreamWriter(output.CreateEntry("manifest.json").Open(), new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(manifest, LanguagePackManifest.JsonOptions));
    }

    private static bool Matches(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static ZipArchiveEntry RequiredEntry(ZipArchive archive, DownloadableLanguagePack pack, string path) =>
        archive.GetEntry(pack.ArchiveRoot + "/" + path) ?? throw InvalidArchive();

    private static string ReadText(ZipArchiveEntry entry, int maximumBytes)
    {
        if (entry.Length <= 0 || entry.Length > maximumBytes) throw InvalidArchive();
        using var input = entry.Open();
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int read;
        while ((read = input.Read(bytes)) > 0)
        {
            if (buffer.Length + read > maximumBytes) throw InvalidArchive();
            buffer.Write(bytes, 0, read);
        }
        if (buffer.Length != entry.Length) throw InvalidArchive();
        return new UTF8Encoding(false, true).GetString(buffer.ToArray());
    }

    private static void AddText(ZipArchive archive, LanguagePackManifest manifest, string path, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        using var stream = archive.CreateEntry(path, CompressionLevel.Fastest).Open();
        stream.Write(bytes);
        manifest.Files.Add(new() { Path = path, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
    }

    private static string ReadLicense(string name)
    {
        using var stream = typeof(LanguagePackDownloadService).Assembly.GetManifestResourceStream("TransLamp.DownloadLicenses." + name)
            ?? throw new TransLampException("license-missing", "缺少語言包授權資料，請重新取得完整程式。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void EnsureRegularDirectory(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw InvalidArchive();
    }

    private static TransLampException InvalidArchive() => new("invalid-download", "下載的語言包格式或方向不相容，原有語言包未變更。");
    private static TransLampException ChecksumMismatch() => new("checksum-mismatch", "語言包下載完整性檢查失敗，請重新下載。");
}
