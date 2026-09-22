using System.IO;
using Microsoft.Web.WebView2.Core;

namespace MarkPad.Rendering;

public sealed partial class PreviewPane
{
    private bool _exportingPdf;

    /// <summary>Export a snapshot in a dedicated pane, leaving the reader's scroll, folds and selection alone.</summary>
    public async Task ExportPdfAsync(string markdown, string? filePath, PreviewOptions options, string outputPath, bool overwrite = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_exportingPdf) throw new InvalidOperationException("A PDF export is already in progress.");
        var destination = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(destination), ".pdf", StringComparison.OrdinalIgnoreCase)
            || filePath is not null && string.Equals(destination, Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a PDF path different from the source document.", nameof(outputPath));

        _exportingPdf = true;
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".markpad-{Guid.NewGuid():N}.pdf.tmp");
        try
        {
            await ShowAsync(markdown, filePath, options with { Dark = false, ReadOnly = true }).WaitAsync(TimeSpan.FromSeconds(45));
            if (_disposed || _browserFailed || _current is null || _ready?.Task.IsCompletedSuccessfully != true || !_ready.Task.Result)
                throw new InvalidOperationException(MarkdownRenderer.Translate(options.Language,
                    "The document preview is unavailable. PDF export could not be completed.",
                    "文件預覽無法載入，未能完成 PDF 匯出。",
                    "文書のプレビューを読み込めないため、PDF を書き出せませんでした。"));

            var token = _current.Token;
            // Lazy images and closed details can be outside the viewport. Prepare all content,
            // then await a flag because ExecuteScriptAsync does not await JavaScript promises.
            await _browser.ExecuteScriptAsync("""
                window.markpadPdfReady = false;
                (async () => {
                    document.querySelectorAll('details').forEach(node => node.open = true);
                    const images = [...document.querySelectorAll('#document img')];
                    images.forEach(img => img.loading = 'eager');
                    await Promise.all(images.map(img => img.decode().catch(() => {})));
                    void document.body.offsetHeight;
                    await document.fonts.ready;
                    window.markpadPdfReady = true;
                })();
                """);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (await ExecuteAsync("window.markpadPdfReady === true") != "true")
            {
                if (_disposed || _browserFailed || _current?.Token != token || DateTime.UtcNow >= deadline)
                    throw new TimeoutException(MarkdownRenderer.Translate(options.Language,
                        "PDF content took too long to prepare. Please try again.", "PDF 內容準備逾時，請重試。", "PDF の準備がタイムアウトしました。再試行してください。"));
                await Task.Delay(50);
            }

            // Chromium also uses the native background for page margins outside the HTML body.
            _browser.DefaultBackgroundColor = System.Drawing.Color.White;
            var settings = _browser.CoreWebView2.Environment.CreatePrintSettings();
            settings.Orientation = CoreWebView2PrintOrientation.Portrait;
            settings.PageWidth = 210 / 25.4;
            settings.PageHeight = 297 / 25.4;
            settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 15 / 25.4;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.ShouldPrintSelectionOnly = false;

            using var pdf = await _browser.CoreWebView2.PrintToPdfStreamAsync(settings);
            // Build completely beside the destination before replacing it. A failed export must
            // never truncate an existing PDF or leave a partial file under its final name.
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await pdf.CopyToAsync(output);
                await output.FlushAsync();
                if (output.Length == 0) throw new IOException("PDF export returned an empty document.");
            }
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            _exportingPdf = false;
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
