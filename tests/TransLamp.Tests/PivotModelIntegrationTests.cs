using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using TransLamp.Core;
using Xunit;
using Xunit.Abstractions;

namespace TransLamp.Tests;

/// <summary>Opt in with TRANSLAMP_PIVOT_RESOURCES containing Runtime/ and four reviewed official local .argosmodel archives.</summary>
public sealed class PivotModelIntegrationTests(ITestOutputHelper output)
{
    private static readonly string[] RequiredPackIds = ["zh-en", "en-fr", "fr-en", "en-zh"];

    [PivotResourcesFact]
    public async Task OfficialModelsTranslateChineseAndFrenchThroughEnglishOnCpu()
    {
        var resources = Path.GetFullPath(Environment.GetEnvironmentVariable("TRANSLAMP_PIVOT_RESOURCES")!);
        var temporaryBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TransLamp.PivotRealTests"));
        var root = Path.GetFullPath(Path.Combine(temporaryBase, Guid.NewGuid().ToString("N")));
        var engine = new TranslationEngine(Path.Combine(resources, "Runtime"));
        Assert.True(engine.IsAvailable, "TRANSLAMP_PIVOT_RESOURCES must contain the actual CPU Runtime/python.exe and Runtime/translate.py.");
        var catalog = RequiredPackIds.Select(id => LanguagePackCatalog.Packs.Single(pack => pack.Id == id)).ToArray();
        foreach (var pack in catalog)
            Assert.True(File.Exists(Path.Combine(resources, Path.GetFileName(pack.DownloadUri.AbsolutePath))),
                $"The prepared local official archive is missing: {pack.Id}.");
        try
        {
            var installer = new LanguagePackService(root, engine.ValidatePackAsync);
            using var handler = new LocalArchiveHandler(resources, catalog);
            using var client = new HttpClient(handler);
            var downloader = new LanguagePackDownloadService(installer, client);
            foreach (var pack in catalog) await downloader.DownloadAndInstallAsync(pack);
            Assert.Equal(4, installer.GetInstalledPacks().Count);
            Assert.Equal(RequiredPackIds, handler.RequestedPackIds);

            await VerifyRoute(engine, installer, "zh", "fr", "连接失败。请重新启动计算机。");
            await VerifyRoute(engine, installer, "fr", "zh", "La connexion a échoué. Veuillez redémarrer l'ordinateur.");
        }
        finally
        {
            if (!root.StartsWith(temporaryBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Pivot fixture cleanup must stay within its temporary base.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private async Task VerifyRoute(TranslationEngine engine, LanguagePackService installer,
        string source, string target, string input)
    {
        var route = TranslationRoute.Resolve(source, target);
        Assert.NotNull(route);
        Assert.True(route.ViaEnglish);
        Assert.Equal(2, route.Steps.Count);
        var installed = route.Steps.Select(step => installer.Find(step.SourceLanguage, step.TargetLanguage)!).ToArray();
        Assert.All(installed, pack => Assert.NotNull(pack));
        var updates = new List<TranslationRouteProgress>();
        var timer = Stopwatch.StartNew();
        var translated = await engine.TranslateRouteAsync(input, installed, new InlineProgress(updates.Add));
        timer.Stop();

        var nonempty = !string.IsNullOrWhiteSpace(translated);
        var changed = translated != input;
        // These checks identify basic target-language markers; they do not assess full semantic fidelity.
        var targetLanguagePresent = target == "zh"
            ? Regex.IsMatch(translated, "[\\u4e00-\\u9fff]")
            : Regex.IsMatch(translated, @"\b(connexion|ordinateur|veuillez|redémarr\w*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var stages = updates.Select(update => update.Stage).Distinct().ToArray();
        var completedStages = updates.Where(update => update.Segment.Completed == update.Segment.Total)
            .Select(update => update.Stage).Distinct().ToArray();
        output.WriteLine(JsonSerializer.Serialize(new
        {
            verifiedAtUtc = DateTimeOffset.UtcNow,
            method = "Production DownloadAndInstallAsync with four reviewed official archives served locally; actual bundled CPU TranslationEngine.TranslateRouteAsync.",
            scope = "CPU execution, two completed stages and basic target-language markers; full semantic fidelity was not assessed.",
            sourceLanguage = source, targetLanguage = target,
            packs = route.Steps.Select(pack => new { pack.Id, pack.Version, pack.Sha256 }),
            input, output = translated, elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 3),
            checks = new { nonempty, changed, targetLanguagePresent, stages, completedStages },
            progress = updates
        }, EvidenceOptions));

        Assert.True(nonempty);
        Assert.True(changed);
        Assert.True(targetLanguagePresent, $"Expected {target} language markers in the recorded translation.");
        Assert.DoesNotContain("▁", translated);
        Assert.DoesNotContain("</s>", translated);
        Assert.Equal(new[] { 1, 2 }, stages);
        Assert.Equal(new[] { 1, 2 }, completedStages);
        Assert.All(updates, update => Assert.Equal(2, update.Stages));
        Assert.Equal(updates.Select(update => update.Stage).Order(), updates.Select(update => update.Stage));
    }

    private static readonly JsonSerializerOptions EvidenceOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class InlineProgress(Action<TranslationRouteProgress> report) : IProgress<TranslationRouteProgress>
    {
        public void Report(TranslationRouteProgress value) => report(value);
    }

    // This transport opens only the four explicitly reviewed local files and cannot access the network.
    private sealed class LocalArchiveHandler(string resources, IReadOnlyList<DownloadableLanguagePack> catalog)
        : HttpMessageHandler
    {
        public List<string> RequestedPackIds { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pack = catalog.SingleOrDefault(pack => pack.DownloadUri == request.RequestUri)
                ?? throw new InvalidOperationException("The pivot test requested an archive outside its four local models.");
            RequestedPackIds.Add(pack.Id);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StreamContent(File.OpenRead(Path.Combine(resources, Path.GetFileName(pack.DownloadUri.AbsolutePath))))
            });
        }
    }

    private sealed class PivotResourcesFactAttribute : FactAttribute
    {
        public PivotResourcesFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRANSLAMP_PIVOT_RESOURCES")))
                Skip = "Set TRANSLAMP_PIVOT_RESOURCES to the local CPU Runtime and official zh-en/en-fr/fr-en/en-zh .argosmodel archives.";
        }
    }
}
