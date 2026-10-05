using System.IO;
using TransLamp.Core;
using Xunit;

namespace TransLamp.Tests;

public sealed class TranslationRouteTests
{
    [Theory]
    [InlineData("en", "zh", "en-zh")]
    [InlineData("ja", "en", "ja-en")]
    [InlineData("en", "es", "en-es")]
    [InlineData("en", "zt", "en-zt")]
    public void ExistingDirectDirectionUsesOnePack(string source, string target, string id)
    {
        var route = TranslationRoute.Resolve(source, target);
        Assert.NotNull(route);
        Assert.False(route.ViaEnglish);
        Assert.Equal(id, Assert.Single(route.Steps).Id);
    }

    [Theory]
    [InlineData("zh", "ja", "zh-en", "en-ja")]
    [InlineData("ja", "zh", "ja-en", "en-zh")]
    [InlineData("zt", "fr", "zt-en", "en-fr")]
    [InlineData("fr", "zt", "fr-en", "en-zt")]
    public void NonEnglishDirectionsUseOrderedEnglishPacks(string source, string target, string first, string second)
    {
        var route = TranslationRoute.Resolve(source, target);
        Assert.NotNull(route);
        Assert.True(route.ViaEnglish);
        Assert.Equal(new[] { first, second }, route.Steps.Select(pack => pack.Id));
    }

    [Fact]
    public void DirectDirectionWinsEvenWhenEnglishRouteAlsoExists()
    {
        var first = CatalogPack("zh-en");
        var second = CatalogPack("en-ja");
        var direct = second with { Id = "zh-ja", SourceLanguage = "zh", DisplayName = "中文 → 日本語" };
        var route = TranslationRoute.Resolve("zh", "ja", new[] { first, second, direct });
        Assert.NotNull(route);
        Assert.False(route.ViaEnglish);
        Assert.Same(direct, Assert.Single(route.Steps));
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("es", "ja")]
    [InlineData("unknown", "ja")]
    [InlineData("ja", "unknown")]
    [InlineData("zh", "zh")]
    [InlineData("en", "en")]
    [InlineData("", "ja")]
    [InlineData("ja", "")]
    public void MissingReverseUnknownAndSameLanguagesAreUnsupported(string source, string target) =>
        Assert.Null(TranslationRoute.Resolve(source, target));

    [Fact]
    public void BothEnglishEdgesAreRequired()
    {
        Assert.Null(TranslationRoute.Resolve("zh", "ja", new[] { CatalogPack("zh-en") }));
        Assert.Null(TranslationRoute.Resolve("zh", "ja", new[] { CatalogPack("en-ja") }));
    }

    [Fact]
    public void OtherIntermediateLanguagesAreNotUsed()
    {
        var toJapanese = CatalogPack("en-ja") with { Id = "zh-ja", SourceLanguage = "zh" };
        var fromJapanese = CatalogPack("ja-en") with { Id = "ja-fr", TargetLanguage = "fr" };
        Assert.Null(TranslationRoute.Resolve("zh", "fr", new[] { toJapanese, fromJapanese }));
    }

    private static DownloadableLanguagePack CatalogPack(string id) => LanguagePackCatalog.Packs.Single(pack => pack.Id == id);
}

public sealed class TranslationRouteEngineTests
{
    [Fact]
    public async Task DirectRouteReturnsItsOnlyResult()
    {
        var pack = Pack("en-zh");
        var calls = 0;
        using var cancellation = new CancellationTokenSource();
        var result = await Engine().TranslateRouteAsync("original text", new[] { pack }, (text, selected, progress, token) =>
        {
            calls++;
            Assert.Equal("original text", text);
            Assert.Same(pack, selected);
            Assert.Equal(cancellation.Token, token);
            Assert.Null(progress);
            return Task.FromResult("最終結果");
        }, cancellationToken: cancellation.Token);
        Assert.Equal("最終結果", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EnglishRoutePassesTheCompleteFirstResultToTheSecondStage()
    {
        const string english = "The complete English output.\nA second paragraph with punctuation: 123!";
        var calls = new List<(string Id, string Text)>();
        var updates = new List<TranslationRouteProgress>();
        var progress = new InlineProgress(updates.Add);
        var result = await Engine().TranslateRouteAsync("原文\n第二段", new[] { Pack("zh-en"), Pack("en-ja") },
            (text, pack, segmentProgress, _) =>
            {
                calls.Add((pack.Manifest.Id, text));
                segmentProgress!.Report(new(0, 1));
                // The stage report must arrive synchronously before the next segment starts.
                Assert.Equal(calls.Count, updates[^1].Stage);
                segmentProgress.Report(new(1, 1));
                return Task.FromResult(calls.Count == 1 ? english : "最終日本語");
            }, progress);
        Assert.Equal("最終日本語", result);
        Assert.Equal(new[] { ("zh-en", "原文\n第二段"), ("en-ja", english) }, calls);
        Assert.Equal(new[] { 1, 1, 2, 2 }, updates.Select(update => update.Stage));
        Assert.All(updates, update => Assert.Equal(2, update.Stages));
        Assert.Equal(new[] { 0, 1, 0, 1 }, updates.Select(update => update.Segment.Completed));
    }

    [Fact]
    public async Task CancellationAfterFirstStagePreventsTheSecondStage()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine().TranslateRouteAsync("原文",
            new[] { Pack("zh-en"), Pack("en-ja") }, (_, _, _, _) =>
            {
                calls++;
                cancellation.Cancel();
                return Task.FromResult("English intermediate");
            }, cancellationToken: cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationDuringSecondStageDoesNotReturnIntermediateText()
    {
        using var cancellation = new CancellationTokenSource();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Engine().TranslateRouteAsync("原文", new[] { Pack("zh-en"), Pack("en-ja") },
            async (_, pack, _, token) =>
            {
                if (pack.Manifest.SourceLanguage == "zh") return "English intermediate";
                secondStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return "unreachable final output";
            }, cancellationToken: cancellation.Token);
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task PreCanceledRouteNeverStartsAStage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Engine().TranslateRouteAsync("原文",
            new[] { Pack("zh-en"), Pack("en-ja") }, (_, _, _, _) =>
            {
                calls++;
                return Task.FromResult("unexpected output");
            }, cancellationToken: cancellation.Token));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StageFailurePropagatesWithoutReturningPartialOutput(int failedStage)
    {
        var failure = new TransLampException("translation-failed", "fixture failure");
        var calls = 0;
        var error = await Assert.ThrowsAsync<TransLampException>(() => Engine().TranslateRouteAsync("原文",
            new[] { Pack("zh-en"), Pack("en-ja") }, (_, _, _, _) =>
            {
                calls++;
                return calls == failedStage ? Task.FromException<string>(failure) : Task.FromResult("English intermediate");
            }));
        Assert.Same(failure, error);
        Assert.Equal(failedStage, calls);
    }

    [Theory]
    [InlineData(20_000, null)]
    [InlineData(20_001, "intermediate-too-long")]
    public async Task IntermediateLimitPreservesTheWholeTextOrStopsBeforeSecondStage(int length, string? expectedCode)
    {
        var english = new string('e', length);
        var calls = 0;
        var task = Engine().TranslateRouteAsync("原文", new[] { Pack("zh-en"), Pack("en-ja") },
            (text, _, _, _) =>
            {
                calls++;
                if (calls == 1) return Task.FromResult(english);
                Assert.Equal(english, text);
                return Task.FromResult("最終結果");
            });
        if (expectedCode is null)
        {
            Assert.Equal("最終結果", await task);
            Assert.Equal(2, calls);
        }
        else
        {
            var error = await Assert.ThrowsAsync<TransLampException>(() => task);
            Assert.Equal(expectedCode, error.Code);
            Assert.Equal(1, calls);
        }
    }

    [Theory]
    [InlineData("", "empty-input")]
    [InlineData("   ", "empty-input")]
    public async Task RouteRetainsEmptyInputValidation(string text, string expectedCode)
    {
        var error = await Assert.ThrowsAsync<TransLampException>(() => Engine().TranslateRouteAsync(text, new[] { Pack("en-zh") }));
        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public async Task RouteRetainsInitialInputLimit()
    {
        var error = await Assert.ThrowsAsync<TransLampException>(() =>
            Engine().TranslateRouteAsync(new string('a', 20_001), new[] { Pack("en-zh") }));
        Assert.Equal("input-too-long", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zh-en,en-ja,ja-en")]
    [InlineData("es-en")]
    [InlineData("zh-zh")]
    [InlineData("zh-en,en-zh")]
    [InlineData("zh-en,fr-en")]
    [InlineData("en-ja,ja-en")]
    public async Task InvalidRoutesAreRejectedBeforeAnyTranslation(string ids)
    {
        InstalledLanguagePack[] packs = ids.Length == 0 ? [] : ids.Split(',').Select(Pack).ToArray();
        var calls = 0;
        var error = await Assert.ThrowsAsync<TransLampException>(() => Engine().TranslateRouteAsync("text", packs,
            (_, _, _, _) => { calls++; return Task.FromResult("unexpected output"); }));
        Assert.Equal("unsupported-direction", error.Code);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task PublicRouteUsesTheExistingRuntimeValidation()
    {
        var error = await Assert.ThrowsAsync<TransLampException>(() => Engine().TranslateRouteAsync("text", new[] { Pack("en-zh") }));
        Assert.Equal("runtime-missing", error.Code);
    }

    private static TranslationEngine Engine() => new(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

    private static InstalledLanguagePack Pack(string id)
    {
        var parts = id.Split('-');
        return new(new LanguagePackManifest { Id = id, SourceLanguage = parts[0], TargetLanguage = parts[1] }, "unused", 0);
    }

    private sealed class InlineProgress(Action<TranslationRouteProgress> report) : IProgress<TranslationRouteProgress>
    {
        public void Report(TranslationRouteProgress value) => report(value);
    }
}
