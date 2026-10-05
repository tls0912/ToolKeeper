using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using TransLamp.Core;
using Xunit;
using Xunit.Abstractions;

namespace TransLamp.Tests;

/// <summary>Opt in with TRANSLAMP_DOWNLOAD_VERIFICATION containing Runtime/ and the reviewed official .argosmodel files.</summary>
public sealed class DownloadedLanguagePackTests(ITestOutputHelper output)
{
    [DownloadedResourcesFact]
    public async Task OfficialCatalogArchivesInstallAndTranslateOnCpu()
    {
        var resources = Environment.GetEnvironmentVariable("TRANSLAMP_DOWNLOAD_VERIFICATION")!;
        var root = Path.Combine(Path.GetTempPath(), "TransLamp.DownloadRealTests", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new TranslationEngine(Path.Combine(resources, "Runtime"));
            var installer = new LanguagePackService(root, engine.ValidatePackAsync);
            using var client = new HttpClient(new DownloadedArchiveHandler(resources));
            var service = new LanguagePackDownloadService(installer, client);
            var results = new List<object>();
            var failures = new List<string>();
            foreach (var pack in LanguagePackCatalog.Packs)
            {
                try
                {
                    var result = await VerifyTranslation(service, engine, pack);
                    results.Add(result);
                    output.WriteLine(JsonSerializer.Serialize(result, EvidenceOptions));
                }
                catch (Exception error)
                {
                    failures.Add($"{pack.Id}: {error.GetType().Name}: {error.Message}");
                    results.Add(new { pack.Id, succeeded = false, error = error.Message });
                }
            }
            File.WriteAllText(Path.Combine(resources, "installation-verification.json"), JsonSerializer.Serialize(new
            {
                verifiedAtUtc = DateTimeOffset.UtcNow,
                method = "Production DownloadAndInstallAsync with exact official archives streamed by a local HTTP handler; real TranslationEngine validation and CPU translation.",
                catalogCount = LanguagePackCatalog.Packs.Count, succeeded = failures.Count == 0, results
            }, EvidenceOptions));
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Assert.Equal(LanguagePackCatalog.Packs.Count, installer.GetInstalledPacks().Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [OnlineDownloadResourcesFact]
    public async Task ProductionHttpClientDownloadsInstallsAndTranslatesOfficialArabicModel()
    {
        var resources = Environment.GetEnvironmentVariable("TRANSLAMP_DOWNLOAD_VERIFICATION")!;
        var root = Path.Combine(Path.GetTempPath(), "TransLamp.DownloadNetworkTests", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new TranslationEngine(Path.Combine(resources, "Runtime"));
            var installer = new LanguagePackService(root, engine.ValidatePackAsync);
            var service = new LanguagePackDownloadService(installer);
            var pack = LanguagePackCatalog.Packs.Single(pack => pack.Id == "en-ar");
            var result = await VerifyTranslation(service, engine, pack);
            File.WriteAllText(Path.Combine(resources, "network-verification.json"), JsonSerializer.Serialize(new
            {
                verifiedAtUtc = DateTimeOffset.UtcNow,
                method = "Production default HttpClient over official HTTPS; production conversion, staging validation, install and CPU translation.",
                result
            }, EvidenceOptions));
            output.WriteLine(JsonSerializer.Serialize(result, EvidenceOptions));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static async Task<object> VerifyTranslation(LanguagePackDownloadService service, TranslationEngine engine,
        DownloadableLanguagePack pack)
    {
        var timer = Stopwatch.StartNew();
        var installed = await service.DownloadAndInstallAsync(pack);
        var input = Inputs[pack.SourceLanguage];
        var translated = await engine.TranslateAsync(input, installed);
        Assert.False(string.IsNullOrWhiteSpace(translated));
        Assert.NotEqual(input, translated);
        Assert.DoesNotContain("▁", translated);
        Assert.DoesNotContain("</s>", translated);
        Assert.Contains("Original upstream README", File.ReadAllText(Path.Combine(installed.DirectoryPath, "NOTICE")));
        if (pack.TargetLanguage == "en") Assert.Contains("computer", translated.ToLowerInvariant());
        return new
        {
            pack.Id, pack.Version, pack.Source, size = pack.SizeBytes, pack.Sha256, pack.ArchiveRoot,
            license = installed.Manifest.LicenseIdentifier, runtimeValidated = true, succeeded = true,
            legacyVocabulary = installed.Manifest.Files.Any(file => file.Path == "model/shared_vocabulary.txt"),
            input, output = translated, elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 3)
        };
    }

    private static readonly JsonSerializerOptions EvidenceOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly IReadOnlyDictionary<string, string> Inputs = new Dictionary<string, string>
    {
        ["en"] = "The connection failed. Please restart the computer.",
        ["zh"] = "连接失败。请重新启动计算机。",
        ["zt"] = "連線失敗。請重新啟動電腦。",
        ["fr"] = "La connexion a échoué. Veuillez redémarrer l'ordinateur.",
        ["pt"] = "A conexão falhou. Por favor, reinicie o computador.",
        ["ja"] = "接続に失敗しました。コンピューターを再起動してください。",
        ["ko"] = "연결에 실패했습니다. 컴퓨터를 다시 시작하세요.",
        ["es"] = "La conexión falló. Por favor, reinicie el ordenador.",
        ["de"] = "Die Verbindung ist fehlgeschlagen. Bitte starten Sie den Computer neu.",
        ["it"] = "La connessione non è riuscita. Riavvia il computer.",
        ["ru"] = "Соединение не удалось. Пожалуйста, перезагрузите компьютер.",
        ["ar"] = "فشل الاتصال. يرجى إعادة تشغيل الكمبيوتر.",
        ["hi"] = "कनेक्शन विफल हो गया। कृपया कंप्यूटर को पुनः आरंभ करें।",
        ["th"] = "การเชื่อมต่อล้มเหลว โปรดรีสตาร์ทคอมพิวเตอร์",
        ["vi"] = "Kết nối không thành công. Vui lòng khởi động lại máy tính."
    };

    private sealed class DownloadedArchiveHandler(string resources) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filename = Path.GetFileName(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(File.OpenRead(Path.Combine(resources, filename)))
            });
        }
    }
}

public sealed class OnlineDownloadResourcesFactAttribute : FactAttribute
{
    public OnlineDownloadResourcesFactAttribute()
    {
        var resources = Environment.GetEnvironmentVariable("TRANSLAMP_DOWNLOAD_VERIFICATION");
        if (Environment.GetEnvironmentVariable("TRANSLAMP_VERIFY_NETWORK") != "1" ||
            string.IsNullOrWhiteSpace(resources) || !File.Exists(Path.Combine(resources, "Runtime", "python.exe")))
            Skip = "Set TRANSLAMP_VERIFY_NETWORK=1 and TRANSLAMP_DOWNLOAD_VERIFICATION to opt into a real official HTTPS download.";
    }
}

public sealed class DownloadedResourcesFactAttribute : FactAttribute
{
    public DownloadedResourcesFactAttribute()
    {
        var resources = Environment.GetEnvironmentVariable("TRANSLAMP_DOWNLOAD_VERIFICATION");
        if (string.IsNullOrWhiteSpace(resources) || !File.Exists(Path.Combine(resources, "Runtime", "python.exe")))
            Skip = "Set TRANSLAMP_DOWNLOAD_VERIFICATION to downloaded official models and the local CPU Runtime.";
    }
}
