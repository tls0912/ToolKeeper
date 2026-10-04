using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TransLamp.Core;

/// <summary>Executes only the application's bundled CPU runtime; model packs never supply code.</summary>
public sealed class TranslationEngine
{
    public const int MaximumInputCharacters = 20_000;
    public string RuntimeDirectory { get; }
    public bool IsAvailable => File.Exists(Path.Combine(RuntimeDirectory, "python.exe")) &&
        File.Exists(Path.Combine(RuntimeDirectory, "translate.py"));

    public TranslationEngine(string? runtimeDirectory = null) => RuntimeDirectory =
        Path.GetFullPath(runtimeDirectory ?? Path.Combine(AppContext.BaseDirectory, "Runtime"));

    public async Task<string> TranslateAsync(string text, InstalledLanguagePack pack,
        IProgress<TranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new TransLampException("empty-input", "請先輸入要翻譯的文字。");
        if (text.Length > MaximumInputCharacters) throw new TransLampException("input-too-long", "一次最多翻譯 20,000 個字元，請分次處理。");
        EnsureAvailable();
        cancellationToken.ThrowIfCancellationRequested();
        using var packLease = LanguagePackService.AcquireTranslationLease(pack);
        await LanguagePackService.VerifyAsync(pack, cancellationToken).ConfigureAwait(false);
        return (await ExecuteAsync(new
        {
            text, modelPath = pack.DirectoryPath,
            sourceLanguage = pack.Manifest.SourceLanguage, targetLanguage = pack.Manifest.TargetLanguage
        }, validateOnly: false, progress, cancellationToken).ConfigureAwait(false))!;
    }

    public Task<string> TranslateRouteAsync(string text, IReadOnlyList<InstalledLanguagePack> packs,
        IProgress<TranslationRouteProgress>? progress = null, CancellationToken cancellationToken = default) =>
        TranslateRouteAsync(text, packs, TranslateAsync, progress, cancellationToken);

    // The delegate permits deterministic orchestration tests; production always uses TranslateAsync's
    // pack lease, hash verification and bundled worker for each segment.
    internal async Task<string> TranslateRouteAsync(string text, IReadOnlyList<InstalledLanguagePack> packs,
        Func<string, InstalledLanguagePack, IProgress<TranslationProgress>?, CancellationToken, Task<string>> translate,
        IProgress<TranslationRouteProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new TransLampException("empty-input", "請先輸入要翻譯的文字。");
        if (text.Length > MaximumInputCharacters) throw new TransLampException("input-too-long", "一次最多翻譯 20,000 個字元，請分次處理。");
        var steps = packs?.ToArray();
        if (steps is null || steps.Length is < 1 or > 2 || steps.Any(pack => pack?.Manifest is null ||
            pack.Manifest.SourceLanguage == pack.Manifest.TargetLanguage ||
            !LanguagePackCatalog.SupportsDirection(pack.Manifest.SourceLanguage, pack.Manifest.TargetLanguage)) ||
            (steps.Length == 2 && (steps[0].Manifest.TargetLanguage != "en" ||
                steps[1].Manifest.SourceLanguage != "en" ||
                steps[0].Manifest.SourceLanguage == steps[1].Manifest.TargetLanguage)))
            throw new TransLampException("unsupported-direction", "目前不支援這個翻譯方向，請使用資料管理中列出的語言包。");

        var output = text;
        for (var index = 0; index < steps.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segmentProgress = progress is null ? null : new RouteProgress(progress, index + 1, steps.Length);
            output = await translate(output, steps[index], segmentProgress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (index < steps.Length - 1 && output.Length > MaximumInputCharacters)
                throw new TransLampException("intermediate-too-long", "英文中繼文字超過 20,000 個字元，請縮短原文或分次翻譯。");
        }
        return output;
    }

    // Forward synchronously so segment callbacks do not capture a context and outlive their stage.
    private sealed class RouteProgress(IProgress<TranslationRouteProgress> progress, int stage, int stages)
        : IProgress<TranslationProgress>
    {
        public void Report(TranslationProgress value) => progress.Report(new(stage, stages, value));
    }

    /// <summary>Loads a staged model in the bundled worker. The installer holds its exclusive directory lease.</summary>
    public async Task ValidatePackAsync(InstalledLanguagePack pack, CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        cancellationToken.ThrowIfCancellationRequested();
        await LanguagePackService.VerifyAsync(pack, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(new
        {
            action = "validate", modelPath = pack.DirectoryPath,
            sourceLanguage = pack.Manifest.SourceLanguage, targetLanguage = pack.Manifest.TargetLanguage
        }, validateOnly: true, progress: null, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable) throw new TransLampException("runtime-missing", "缺少離線翻譯執行元件，請使用完整的 TransLamp Offline Kit。");
    }

    private async Task<string?> ExecuteAsync(object request, bool validateOnly, IProgress<TranslationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(RuntimeDirectory, "python.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = RuntimeDirectory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add(Path.Combine(RuntimeDirectory, "translate.py"));
        start.Environment["HF_HUB_OFFLINE"] = "1";
        start.Environment["TRANSFORMERS_OFFLINE"] = "1";
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new InvalidOperationException(); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { throw new TransLampException("runtime-start", "無法啟動離線翻譯元件，請重新取得完整 Offline Kit。", error); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var stop = timeout.Token.Register(() => Kill(process));
        // Drain stderr without retaining input text or external exception details.
        var drain = DrainAsync(process.StandardError);
        try
        {
            var json = JsonSerializer.Serialize(request);
            await process.StandardInput.WriteLineAsync(json.AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            string? translation = null;
            var validated = false;
            var totalOutput = 0;
            while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                totalOutput += line.Length;
                if (totalOutput > 1_000_000) throw RuntimeFailure(validateOnly);
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
                var root = document.RootElement;
                if (root.TryGetProperty("error", out _)) throw RuntimeFailure(validateOnly);
                if (validateOnly)
                {
                    if (root.TryGetProperty("validated", out var accepted) && accepted.ValueKind == JsonValueKind.True)
                        validated = true;
                }
                else if (root.TryGetProperty("text", out var output)) translation = output.GetString();
                else if (root.TryGetProperty("progress", out var completed) && root.TryGetProperty("total", out var total) &&
                    completed.TryGetInt32(out var completedValue) && total.TryGetInt32(out var totalValue) &&
                    completedValue >= 0 && totalValue > 0 && completedValue <= totalValue)
                    progress?.Report(new(completedValue, totalValue));
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await drain.ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || (validateOnly ? !validated : string.IsNullOrWhiteSpace(translation)))
                throw RuntimeFailure(validateOnly);
            return translation;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw validateOnly
                ? new TransLampException("validation-timeout", "語言包載入驗證時間過長，已停止。原有語言包未變更。")
                : new TransLampException("translation-timeout", "翻譯時間過長，已停止。請減少文字後重試。");
        }
        catch (Exception error) when (error is JsonException or IOException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw RuntimeFailure(validateOnly, error);
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await drain.ConfigureAwait(false);
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
    private static TransLampException RuntimeFailure(bool validateOnly, Exception? inner = null) => validateOnly
        ? new("invalid-model", "語言包無法由目前離線引擎載入，原有語言包未變更。請取得相容且完整的語言包。", inner)
        : new("translation-failed", "離線翻譯未完成。請確認語言包與執行元件完整，或減少文字後重試。", inner);
}
