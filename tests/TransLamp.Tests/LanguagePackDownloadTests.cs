using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TransLamp.Core;
using Xunit;

namespace TransLamp.Tests;

public sealed class LanguagePackDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TransLamp.DownloadTests", Guid.NewGuid().ToString("N"));
    private LanguagePackService Installer => new(_root, (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; });

    [Fact]
    public async Task StreamedDownloadVerifiesConvertsAndInstallsDataWithAttribution()
    {
        var (pack, bytes) = Fixture();
        var progress = new List<LanguagePackDownloadProgress>();
        using var client = Client(bytes);
        var service = new LanguagePackDownloadService(Installer, client, [pack]);
        var installed = await service.DownloadAndInstallAsync(pack, new InlineProgress(progress.Add));
        await LanguagePackService.VerifyAsync(installed);
        Assert.Equal("en-fr", installed.Manifest.Id);
        Assert.Equal(pack.Version, installed.Manifest.ModelVersion);
        Assert.Equal(pack.Source, installed.Manifest.ModelSource);
        Assert.Contains("Original upstream README", File.ReadAllText(Path.Combine(installed.DirectoryPath, "NOTICE")));
        Assert.Contains("Creative Commons", File.ReadAllText(Path.Combine(installed.DirectoryPath, "LICENSE")));
        Assert.False(File.Exists(Path.Combine(installed.DirectoryPath, "stanza", "unused.pt")));
        Assert.All(installed.Manifest.Files, file => Assert.DoesNotContain("metadata.json", file.Path));
        Assert.Contains(progress, value => value.Stage == "Downloading" && value.BytesDownloaded == 0);
        Assert.True(progress.Count(value => value.Stage == "Downloading") > 2);
        Assert.Contains(progress, value => value.Stage == "Downloading" && value.BytesDownloaded == pack.SizeBytes && value.Fraction == 1);
        Assert.Equal("Installing", progress[^1].Stage);
        Assert.Single(Installer.GetInstalledPacks());
    }

    [Fact]
    public async Task LegacyTextVocabularyInstallsWithoutInventingConfigOrChangingModelData()
    {
        var (pack, bytes) = Fixture(legacy: true);
        Assert.Equal("en_fr", pack.ArchiveRoot);
        Assert.EndsWith("/translate-en_fr-1_1.argosmodel", pack.Source);
        using var client = Client(bytes);
        var installed = await new LanguagePackDownloadService(Installer, client, [pack]).DownloadAndInstallAsync(pack);
        await LanguagePackService.VerifyAsync(installed);
        Assert.Equal("structural model", File.ReadAllText(Path.Combine(installed.DirectoryPath, "model/model.bin")));
        Assert.Equal("<unk>\n<s>\n</s>\n▁example\n", File.ReadAllText(Path.Combine(installed.DirectoryPath, "model/shared_vocabulary.txt")));
        Assert.False(File.Exists(Path.Combine(installed.DirectoryPath, "model/config.json")));
        Assert.DoesNotContain(installed.Manifest.Files, file => file.Path.EndsWith(".json"));
        Assert.Equal("MIT", installed.Manifest.LicenseIdentifier);
        Assert.Contains("Legacy model test attribution", File.ReadAllText(Path.Combine(installed.DirectoryPath, "NOTICE")));
        Assert.Contains("MIT License", File.ReadAllText(Path.Combine(installed.DirectoryPath, "LICENSE")));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("extra")]
    [InlineData("header")]
    public async Task InvalidIntegrityCannotInstallOrReplace(string failure)
    {
        var (pack, bytes) = Fixture();
        using (var originalClient = Client(bytes))
            await new LanguagePackDownloadService(Installer, originalClient, [pack]).DownloadAndInstallAsync(pack);
        var original = Installer.Find("en", "fr")!;
        var originalBytes = File.ReadAllBytes(Path.Combine(original.DirectoryPath, "model/model.bin"));
        var responseBytes = failure switch
        {
            "hash" => bytes.Select((value, index) => index == 0 ? (byte)(value ^ 1) : value).ToArray(),
            "truncated" => bytes[..^1],
            "extra" => [.. bytes, 1],
            _ => bytes
        };
        using var client = Client(responseBytes, contentLength: failure == "header" ? pack.SizeBytes + 1 : null);
        var error = await Assert.ThrowsAsync<TransLampException>(() =>
            new LanguagePackDownloadService(Installer, client, [pack]).DownloadAndInstallAsync(pack));
        Assert.Equal("checksum-mismatch", error.Code);
        Assert.Equal(originalBytes, File.ReadAllBytes(Path.Combine(original.DirectoryPath, "model/model.bin")));
        Assert.Single(Installer.GetInstalledPacks());
    }

    [Theory]
    [InlineData("Downloading")]
    [InlineData("Installing")]
    public async Task CancellationStopsBeforeCommit(string stage)
    {
        var (pack, bytes) = Fixture();
        using var cancellation = new CancellationTokenSource();
        using var client = Client(bytes);
        var progress = new InlineProgress(value => { if (value.Stage == stage && value.BytesDownloaded > 0) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LanguagePackDownloadService(Installer, client, [pack]).DownloadAndInstallAsync(pack, progress, cancellation.Token));
        Assert.Empty(Installer.GetInstalledPacks());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidatorFailureUsesImporterRollbackAndPreservesPriorModel(bool legacy)
    {
        var (pack, bytes) = Fixture(legacy: legacy);
        using var client = Client(bytes);
        await new LanguagePackDownloadService(Installer, client, [pack]).DownloadAndInstallAsync(pack);
        var original = Installer.Find("en", "fr")!;
        var failedInstaller = new LanguagePackService(_root, (_, _) =>
            Task.FromException(new TransLampException("invalid-model", "Native loader rejected model.")));
        var error = await Assert.ThrowsAsync<TransLampException>(() =>
            new LanguagePackDownloadService(failedInstaller, client, [pack]).DownloadAndInstallAsync(pack));
        Assert.Equal("invalid-model", error.Code);
        await LanguagePackService.VerifyAsync(original);
        Assert.Single(Installer.GetInstalledPacks());
        Assert.Empty(Directory.GetDirectories(_root, ".install-*"));
        Assert.Empty(Directory.GetFiles(_root, ".transaction-*"));
    }

    [Theory]
    [InlineData("direction")]
    [InlineData("version")]
    [InlineData("traversal")]
    [InlineData("symlink")]
    [InlineData("duplicate")]
    [InlineData("missing-tokenizer")]
    [InlineData("missing-vocabulary")]
    [InlineData("missing-config")]
    [InlineData("license")]
    public async Task HashConsistentUnsafeOrIncompatibleSourceIsRejected(string failure)
    {
        var (pack, bytes) = Fixture(failure);
        using var client = Client(bytes);
        await Assert.ThrowsAsync<TransLampException>(() =>
            new LanguagePackDownloadService(Installer, client, [pack]).DownloadAndInstallAsync(pack));
        Assert.Empty(Installer.GetInstalledPacks());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionServiceRejectsModifiedCatalogEntryBeforeAnyHttpRequest(bool replaceFilename)
    {
        var handler = new ResponseHandler(_ => throw new InvalidOperationException("HTTP should not be attempted."));
        using var client = new HttpClient(handler);
        var original = LanguagePackCatalog.Packs[0];
        var altered = replaceFilename ? original with { DownloadFileName = "unreviewed.argosmodel" }
            : original with { Sha256 = new string('0', 64) };
        var error = await Assert.ThrowsAsync<TransLampException>(() =>
            new LanguagePackDownloadService(Installer, client).DownloadAndInstallAsync(altered));
        Assert.Equal("unsupported-download", error.Code);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task NetworkFailureHasExplicitErrorAndNoInstalledData()
    {
        var (pack, bytes) = Fixture();
        using var client = new HttpClient(new ResponseHandler(_ => new(HttpStatusCode.ServiceUnavailable)));
        var error = await Assert.ThrowsAsync<TransLampException>(() =>
            new LanguagePackDownloadService(Installer, client, [pack]).DownloadAndInstallAsync(pack));
        Assert.Equal("download-failed", error.Code);
        Assert.Empty(Installer.GetInstalledPacks());
    }

    [Fact]
    public void CatalogHasOnlyReviewedHttpsDirectionsAndFixedIntegrityMetadata()
    {
        Assert.Equal(27, LanguagePackCatalog.Packs.Count);
        Assert.Equal(27, LanguagePackCatalog.Packs.Select(pack => pack.Id).Distinct().Count());
        Assert.Equal(15, LanguagePackCatalog.Packs.SelectMany(pack => new[] { pack.SourceLanguage, pack.TargetLanguage }).Distinct().Count());
        foreach (var pack in LanguagePackCatalog.Packs)
        {
            Assert.Equal("https", pack.DownloadUri.Scheme);
            Assert.Equal("argos-net.com", pack.DownloadUri.Host);
            Assert.True(pack.SizeBytes > 50_000_000);
            Assert.Matches("^[a-f0-9]{64}$", pack.Sha256);
            Assert.Equal(pack.SourceLanguage + "-" + pack.TargetLanguage, pack.Id);
            if (pack.Id != "en-es")
                Assert.Contains(LanguagePackCatalog.Packs, other => other.SourceLanguage == pack.TargetLanguage && other.TargetLanguage == pack.SourceLanguage);
        }
        Assert.False(LanguagePackCatalog.SupportsDirection("fr", "pt"));
        Assert.True(LanguagePackCatalog.SupportsDirection("en", "ja"));
        Assert.True(LanguagePackCatalog.SupportsDirection("zt", "en"));
        Assert.True(LanguagePackCatalog.SupportsDirection("en", "es"));
        Assert.False(LanguagePackCatalog.SupportsDirection("es", "en"));
        Assert.False(LanguagePackCatalog.SupportsDirection("en", "id"));
        Assert.False(LanguagePackCatalog.SupportsDirection("id", "en"));
    }

    private static HttpClient Client(byte[] bytes, long? contentLength = null) => new(new ResponseHandler(request =>
    {
        var content = new StreamContent(new ChunkedStream(bytes));
        if (contentLength is not null) content.Headers.ContentLength = contentLength;
        return new(HttpStatusCode.OK) { Content = content, RequestMessage = request };
    }));

    private static (DownloadableLanguagePack Pack, byte[] Bytes) Fixture(string? invalid = null, bool legacy = false)
    {
        var root = legacy ? "en_fr" : "translate-en_fr-1_9";
        var version = legacy ? "1.1" : "1.9";
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, root + "/metadata.json", JsonSerializer.Serialize(new
            {
                from_code = "en", to_code = invalid == "direction" ? "zh" : "fr",
                package_version = invalid == "version" ? "2.0" : version
            }));
            Write(zip, root + "/README.md", invalid == "license" ? "License not declared." :
                legacy ? "Legacy model test attribution." : "Authors: structural test fixture. Licensed CC-BY 4.0");
            if (invalid != "missing-tokenizer") Write(zip, root + "/sentencepiece.model", "structural tokenizer");
            Write(zip, root + "/model/model.bin", "structural model");
            if (!legacy && invalid != "missing-config") Write(zip, root + "/model/config.json", "{}");
            if (invalid != "missing-vocabulary")
                Write(zip, root + (legacy ? "/model/shared_vocabulary.txt" : "/model/shared_vocabulary.json"),
                    legacy ? "<unk>\n<s>\n</s>\n▁example\n" : "[]");
            Write(zip, root + "/stanza/unused.pt", "Unused data never installed.");
            if (invalid == "traversal") Write(zip, root + "/../outside.txt", "must never be extracted");
            if (invalid == "duplicate") Write(zip, root + "/README.md", "duplicate");
            if (invalid == "symlink") Write(zip, root + "/link", "target").ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        var bytes = memory.ToArray();
        return (new("en-fr", "en", "fr", "English → Français", version, bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), root, legacy ? "MIT" : "CC-BY-4.0 AND MIT",
            legacy ? "translate-en_fr-1_1.argosmodel" : null), bytes);
    }

    private static ZipArchiveEntry Write(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
        return entry;
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            return Task.FromResult(response(request));
        }
    }

    private sealed class ChunkedStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(37, buffer.Length)], cancellationToken);
    }

    private sealed class InlineProgress(Action<LanguagePackDownloadProgress> action) : IProgress<LanguagePackDownloadProgress>
    {
        public void Report(LanguagePackDownloadProgress value) => action(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
