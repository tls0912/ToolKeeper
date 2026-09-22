using System.IO;
using System.Globalization;
using System.Windows;
using MarkPad.Rendering;
using MarkPad.Services;

namespace MarkPad;

public partial class MainWindow
{
    private bool _exportingPdf;

    private async Task ExportPdfAsync()
    {
        var tab = _current;
        if (tab is null || _disposed || _closing || _exportingPdf) return;
        if (_saving.Contains(tab.Id) || _closingDocuments.Contains(tab.Id))
        {
            Toast(T("This document is still being processed. Please export when it finishes.",
                "文件正在處理，完成後即可匯出 PDF。", "文書を処理中です。完了後に PDF を書き出してください。"));
            return;
        }
        _exportingPdf = true;
        BuildActions();
        try
        {
            // Save even when automatic saving is disabled. Cancelling Save As, a conflict or
            // another edit during the save must stop the export before any PDF is touched.
            var diskState = App.Files.RefreshStatus(tab);
            if ((tab.IsDirty || tab.FilePath is null || tab.IsMissing || diskState == FileChangeStatus.Changed)
                && !await SaveAsync(tab)) return;
            if (_disposed || !Documents.Contains(tab) || tab.FilePath is null || tab.IsDirty) return;
            var sourcePath = tab.FilePath;
            var markdown = tab.Content;
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var destination = Path.Combine(Path.GetDirectoryName(sourcePath)!, $"{Path.GetFileNameWithoutExtension(sourcePath)}_{stamp}.pdf");
            if (string.Equals(sourcePath, destination, StringComparison.OrdinalIgnoreCase)
                || App.AllDocuments.Any(other => string.Equals(other.FilePath, destination, StringComparison.OrdinalIgnoreCase)))
            {
                Toast(T("The PDF path is an open document. Rename the Markdown file before exporting.",
                    "PDF 路徑與已開啟的文件相同，請先將 Markdown 另存新檔。",
                    "PDF の保存先が開いている文書と同じです。Markdown を別名で保存してください。"));
                return;
            }
            var overwrite = File.Exists(destination);
            if (overwrite && Choose(T($"Replace the existing PDF?\n{destination}",
                    $"要覆寫現有的 PDF 嗎？\n{destination}", $"既存の PDF を上書きしますか？\n{destination}"),
                    ("replace", T("Replace", "覆寫", "上書き")), ("cancel", T("Cancel", "取消", "キャンセル"))) != "replace") return;
            if (_disposed) return;

            Toast(T("Exporting PDF…", "正在匯出 PDF…", "PDF を書き出しています…"));
            using var export = new PreviewPane(Path.Combine(App.Preferences.DataDirectory, "WebView2"))
            {
                Opacity = 0, IsHitTestVisible = false, Focusable = false,
                Width = 794, Height = 1123, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            // A loaded, separate browser keeps export independent of tab switches, edit mode,
            // search, folds and scroll position, without opening another visible window.
            RootGrid.Children.Insert(0, export);
            try
            {
                var options = new PreviewOptions(false, PreviewFontName, Settings.PreviewFontSize,
                    Settings.CodeLineNumbers, Settings.EmojiShortcodes, UiLanguage, true);
                await export.ExportPdfAsync(markdown, sourcePath, options, destination, overwrite);
            }
            finally { RootGrid.Children.Remove(export); }
            Toast(T($"PDF exported: {Path.GetFileName(destination)}", $"已匯出 PDF：{Path.GetFileName(destination)}", $"PDF を書き出しました：{Path.GetFileName(destination)}"));
        }
        finally
        {
            _exportingPdf = false;
            if (!_disposed) BuildActions();
        }
    }
}
