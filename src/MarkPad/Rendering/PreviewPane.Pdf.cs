using System.IO;
using System.Text;
using System.Text.Json;
using MarkPad.Services;

namespace MarkPad.Rendering;

public sealed partial class PreviewPane
{
    private bool _exportingPdf;

    /// <summary>Export a snapshot in a dedicated pane, leaving the reader's scroll, folds and selection alone.</summary>
    public async Task ExportPdfAsync(string markdown, string? filePath, PreviewOptions options, string outputPath,
        bool overwrite = false, IReadOnlyCollection<int>? outlineLevels = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_exportingPdf) throw new InvalidOperationException("A PDF export is already in progress.");
        var destination = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(destination), ".pdf", StringComparison.OrdinalIgnoreCase)
            || filePath is not null && string.Equals(destination, Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a PDF path different from the source document.", nameof(outputPath));

        var levels = (outlineLevels ?? [1, 2, 3, 4, 5, 6]).Where(level => level is >= 1 and <= 6).Distinct().ToArray();
        _exportingPdf = true;
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".markpad-{Guid.NewGuid():N}.pdf.tmp");
        try
        {
            await ShowAsync(markdown, filePath, options with { Dark = false, Ink = false, ReadOnly = true, DocumentTitle = Path.GetFileName(destination) }).WaitAsync(TimeSpan.FromSeconds(45));
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
            // PDF bookmarks come from Chromium's accessibility heading tree. Remove only
            // excluded heading roles in this dedicated export page; keep every visible title.
            var labels = Headings.Select(heading => new[] { heading.Id, heading.Title }).ToArray();
            await _browser.ExecuteScriptAsync($$"""
                (() => {
                    const levels = new Set({{JsonSerializer.Serialize(levels)}});
                    const labels = new Map({{JsonSerializer.Serialize(labels)}});
                    document.querySelectorAll('#document h1,#document h2,#document h3,#document h4,#document h5,#document h6').forEach(heading => {
                        const level = Number(heading.tagName.slice(1));
                        if (levels.has(level)) {
                            heading.setAttribute('role', 'heading');
                            heading.setAttribute('aria-level', String(level));
                            heading.setAttribute('aria-label', labels.get(heading.id) ?? heading.textContent);
                        } else {
                            heading.setAttribute('role', 'presentation');
                            heading.removeAttribute('aria-level');
                            heading.removeAttribute('aria-label');
                        }
                    });
                })();
                """);
            // Build completely beside the destination before replacing it. A failed export must
            // never truncate an existing PDF or leave a partial file under its final name.
            await PrintPdfAsync(temporary, levels.Length > 0);
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            _exportingPdf = false;
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task PrintPdfAsync(string temporary, bool includeOutline)
    {
        var core = _browser.CoreWebView2;
        var parameters = JsonSerializer.Serialize(new
        {
            landscape = false, displayHeaderFooter = false, printBackground = true, scale = 1,
            paperWidth = 210 / 25.4, paperHeight = 297 / 25.4,
            marginTop = 15 / 25.4, marginBottom = 15 / 25.4, marginLeft = 15 / 25.4, marginRight = 15 / 25.4,
            preferCSSPageSize = false, generateTaggedPDF = true, generateDocumentOutline = includeOutline,
            transferMode = "ReturnAsStream"
        });
        // DevTools exposes PDF outlines, which the native print-settings API does not.
        using var printed = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.printToPDF", parameters)
            .WaitAsync(TimeSpan.FromSeconds(60)));
        var handle = printed.RootElement.GetProperty("stream").GetString()
            ?? throw new IOException("PDF export returned no stream.");
        try
        {
            await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            var readParameters = JsonSerializer.Serialize(new { handle, size = 65536 });
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (true)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("PDF export stream took too long to read.");
                using var chunk = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("IO.read", readParameters)
                    .WaitAsync(TimeSpan.FromSeconds(15)));
                var data = chunk.RootElement.GetProperty("data").GetString() ?? "";
                var encoded = chunk.RootElement.TryGetProperty("base64Encoded", out var base64) && base64.GetBoolean();
                await output.WriteAsync(encoded ? Convert.FromBase64String(data) : Encoding.UTF8.GetBytes(data));
                if (chunk.RootElement.GetProperty("eof").GetBoolean()) break;
            }
            await output.FlushAsync();
            if (output.Length == 0) throw new IOException("PDF export returned an empty document.");
        }
        finally
        {
            try
            {
                await core.CallDevToolsProtocolMethodAsync("IO.close", JsonSerializer.Serialize(new { handle }))
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) { LocalLog.Write(ex); }
        }
    }
}
