using System.IO;
using TransLamp.Core;
using Xunit;

namespace TransLamp.Tests;

public sealed class TranslationEngineTests
{
    [Theory]
    [InlineData("", "empty-input")]
    [InlineData("   ", "empty-input")]
    [InlineData("hello", "runtime-missing")]
    public async Task MissingPrerequisitesHaveExplicitErrorsWithoutFallback(string text, string code)
    {
        var engine = new TranslationEngine(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var exception = await Assert.ThrowsAsync<TransLampException>(() => engine.TranslateAsync(text, null!));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task OversizedInputIsRejectedRatherThanSilentlyTruncated()
    {
        var engine = new TranslationEngine();
        var exception = await Assert.ThrowsAsync<TransLampException>(() => engine.TranslateAsync(new string('a', 20_001), null!));
        Assert.Equal("input-too-long", exception.Code);
    }
}

/// <summary>Opt in with TRANSLAMP_RESOURCES pointing to a prepared Runtime/ and LanguagePacks/ directory.</summary>
public sealed class RealTranslationTests
{
    [RealResourcesFact]
    public async Task ImportedModelsTranslateBothDirections()
    {
        var resources = Environment.GetEnvironmentVariable("TRANSLAMP_RESOURCES")!;
        var directory = Path.Combine(Path.GetTempPath(), "TransLamp.RealTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new LanguagePackService(directory);
            Assert.Equal(2, await service.InstallBundledAsync(Path.Combine(resources, "LanguagePacks")));
            var engine = new TranslationEngine(Path.Combine(resources, "Runtime"));
            var chinese = await engine.TranslateAsync("The connection timed out. Please restart the computer.", service.Find("en", "zh")!);
            var english = await engine.TranslateAsync("连接失败，请重新启动电脑。", service.Find("zh", "en")!);
            Assert.Matches("[\\u4e00-\\u9fff]", chinese);
            Assert.Matches("[a-zA-Z]{3,}", english);
            Assert.DoesNotContain("▁", chinese);
            Assert.DoesNotContain("▁", english);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [RealResourcesFact]
    public async Task RunningTranslationLocksPacksAndCancellationReleasesThem()
    {
        var resources = Environment.GetEnvironmentVariable("TRANSLAMP_RESOURCES")!;
        var directory = Path.Combine(Path.GetTempPath(), "TransLamp.RealTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new LanguagePackService(directory);
            await service.InstallBundledAsync(Path.Combine(resources, "LanguagePacks"));
            var engine = new TranslationEngine(Path.Combine(resources, "Runtime"));
            using var cancel = new CancellationTokenSource();
            var sawRunningModel = false;
            var progress = new InlineProgress(value =>
            {
                if (sawRunningModel) return;
                sawRunningModel = true;
                var error = Assert.Throws<TransLampException>(() => service.Remove("en-zh"));
                Assert.Equal("pack-busy", error.Code);
                cancel.Cancel();
            });
            var text = string.Join("\n", Enumerable.Repeat("Please check the connection and restart the computer.", 80));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                engine.TranslateAsync(text, service.Find("en", "zh")!, progress, cancel.Token));
            Assert.True(sawRunningModel);
            service.Remove("en-zh");
            Assert.Null(service.Find("en", "zh"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class InlineProgress(Action<TranslationProgress> report) : IProgress<TranslationProgress>
    {
        public void Report(TranslationProgress value) => report(value);
    }

    private sealed class RealResourcesFactAttribute : FactAttribute
    {
        public RealResourcesFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRANSLAMP_RESOURCES")))
                Skip = "Set TRANSLAMP_RESOURCES to run actual CPU model integration.";
        }
    }
}
