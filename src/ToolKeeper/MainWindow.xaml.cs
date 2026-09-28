using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ToolKeeper.Services;
using ToolKeeper.UI;

namespace ToolKeeper;

public sealed class IconConversionItem(string sourcePath, string outputPath, Func<string> message, bool success) : INotifyPropertyChanged
{
    public IconConversionItem(string sourcePath, string outputPath, string message, bool success)
        : this(sourcePath, outputPath, () => message, success) { }

    public string SourcePath { get; } = sourcePath;
    public string OutputPath { get; } = outputPath;
    public bool Success { get; } = success;
    public string Message => message();
    public string FileName => Path.GetFileName(SourcePath);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshMessage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
}

public partial class MainWindow : AppWindow
{
    private readonly ObservableCollection<IconConversionItem> _iconItems = [];
    private CancellationTokenSource? _hashCancellation;
    private CancellationTokenSource? _iconCancellation;
    private FileHashResult? _hashResult;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        IconResults.ItemsSource = _iconItems;
        InitializeProductPreferences();
    }

    public async Task LoadHashFileAsync(string path)
    {
        if (_closed) return;
        _hashCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _hashCancellation = cancellation;
        ClearHashResult();
        HashFileName.Text = Path.GetFileName(path);
        HashFileName.ToolTip = path;
        SetHashStatus(() => T("Calculating… Drop another file to restart.", "正在計算…可拖入另一個檔案重新開始。", "計算中…別のファイルをドロップすると再開します。"));
        HashProgress.Value = 0;
        CancelHashButton.Visibility = Visibility.Visible;
        CancelHashButton.IsEnabled = true;
        var progress = new Progress<double>(value =>
        {
            if (!_closed && ReferenceEquals(_hashCancellation, cancellation)) HashProgress.Value = value;
        });
        try
        {
            var result = await FileHashService.ComputeAsync(path, progress, cancellation.Token);
            if (_closed || !ReferenceEquals(_hashCancellation, cancellation)) return;
            cancellation.Token.ThrowIfCancellationRequested();
            _hashResult = result;
            Md5Output.Text = result.Md5;
            Sha1Output.Text = result.Sha1;
            Sha256Output.Text = result.Sha256;
            CopyHashesButton.IsEnabled = true;
            HashProgress.Value = 1;
            SetHashStatus(() => T($"Done · {result.Length:N0} bytes", $"完成 · {result.Length:N0} bytes", $"完了 · {result.Length:N0} bytes"));
            UpdateComparison();
        }
        catch (OperationCanceledException)
        {
            if (!_closed && ReferenceEquals(_hashCancellation, cancellation))
            {
                SetHashStatus(() => T("Cancelled. Choose or drop a file to try again.", "已取消。可重新選擇或拖入檔案。", "キャンセルしました。ファイルを選択またはドロップしてください。"));
                HashProgress.Value = 0;
            }
        }
        catch (Exception exception)
        {
            if (!_closed && ReferenceEquals(_hashCancellation, cancellation))
            {
                SetHashStatus(() => T("Unable to calculate: ", "無法計算：", "計算できません：") + Explain(exception));
                HashProgress.Value = 0;
            }
        }
        finally
        {
            if (ReferenceEquals(_hashCancellation, cancellation))
            {
                _hashCancellation = null;
                CancelHashButton.IsEnabled = false;
                CancelHashButton.Visibility = Visibility.Collapsed;
            }
            cancellation.Dispose();
        }
    }

    public async Task ConvertImagesAsync(IEnumerable<string> paths)
    {
        if (_closed) return;
        if (_iconCancellation is not null)
        {
            SetIconStatus(() => T("Conversion is in progress. Wait or cancel this batch first.", "正在轉換，請等待完成，或先取消目前批次。", "変換中です。完了を待つか、現在の処理をキャンセルしてください。"));
            return;
        }
        var files = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) return;
        var cancellation = new CancellationTokenSource();
        _iconCancellation = cancellation;
        _iconItems.Clear();
        IconEmptyState.Visibility = Visibility.Collapsed;
        IconResults.Visibility = Visibility.Visible;
        ChooseImagesButton.IsEnabled = false;
        CancelIconsButton.Visibility = Visibility.Visible;
        CancelIconsButton.IsEnabled = true;
        var succeeded = 0;
        var failed = 0;
        try
        {
            for (var index = 0; index < files.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var current = index + 1;
                SetIconStatus(() => T($"Converting {current}/{files.Length}…", $"正在轉換 {current}／{files.Length}…", $"変換中 {current}／{files.Length}…"));
                try
                {
                    var result = await Task.Run(() => IconConversionService.Convert(files[index], cancellation.Token));
                    if (_closed) return;
                    _iconItems.Add(new(result.SourcePath, result.OutputPath, () => T("✓ Converted", "✓ 轉換完成", "✓ 変換完了"), true));
                    succeeded++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    if (_closed) return;
                    _iconItems.Add(new(files[index], "", () => T("Unable to convert: ", "無法轉換：", "変換できません：") + Explain(exception), false));
                    failed++;
                }
            }
            SetIconStatus(() => failed > 0
                ? T($"Done · {succeeded} succeeded, {failed} failed. See details above.", $"完成 · 成功 {succeeded} 張，失敗 {failed} 張，原因見上方。", $"完了 · 成功 {succeeded} 件、失敗 {failed} 件。詳細は上の一覧をご覧ください。")
                : T($"Done · {succeeded} saved beside the source images.", $"完成 · 成功 {succeeded} 張，已儲存到原資料夾。", $"完了 · {succeeded} 件を元のフォルダーに保存しました。"));
        }
        catch (OperationCanceledException)
        {
            if (!_closed) SetIconStatus(() => T($"Cancelled · {succeeded} completed, {failed} failed; remaining images skipped. Completed ICO files are preserved.", $"已取消 · 完成 {succeeded} 張，失敗 {failed} 張，其餘未處理。已完成的 ICO 保留。", $"キャンセル · 完了 {succeeded} 件、失敗 {failed} 件。残りは未処理です。作成済みの ICO は保持されます。"));
        }
        finally
        {
            _iconCancellation = null;
            cancellation.Dispose();
            ChooseImagesButton.IsEnabled = true;
            CancelIconsButton.IsEnabled = false;
            CancelIconsButton.Visibility = Visibility.Collapsed;
            if (_iconItems.Count == 0)
            {
                IconEmptyState.Visibility = Visibility.Visible;
                IconResults.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ClearHashResult()
    {
        _hashResult = null;
        Md5Output.Clear();
        Sha1Output.Clear();
        Sha256Output.Clear();
        CopyHashesButton.IsEnabled = false;
        UpdateComparison();
    }

    private void ExpectedHashChanged(object sender, TextChangedEventArgs e)
    {
        if (HashComparison is not null) UpdateComparison();
    }

    private void UpdateComparison()
    {
        HashComparison.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var expected = ExpectedHash.Text.Trim();
        if (expected.Length == 0)
            HashComparison.Text = T("Compare MD5, SHA-1 or SHA-256.", "可比對 MD5、SHA-1 或 SHA-256。", "MD5、SHA-1、SHA-256 を照合できます。");
        else if (!FileHashService.IsValidExpectedHash(expected))
        {
            HashComparison.Text = T("Paste a 32, 40 or 64 digit hexadecimal hash.", "請貼上 32、40 或 64 位十六進位 HASH。", "32、40、64 桁の 16 進数ハッシュを貼り付けてください。");
            HashComparison.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
        }
        else if (_hashResult is null)
            HashComparison.Text = T("Expected hash ready. Waiting for the file calculation.", "已備妥預期 HASH，等待檔案計算完成。", "照合するハッシュを入力済みです。ファイルの計算を待っています。");
        else
        {
            var algorithm = FileHashService.MatchAlgorithm(_hashResult, expected);
            HashComparison.Text = algorithm is null
                ? T("✕ Mismatch · Check the file and expected hash.", "✕ 不相符 · 請確認檔案與預期 HASH。", "✕ 不一致 · ファイルと照合するハッシュを確認してください。")
                : T($"✓ {algorithm} matches · File hash verified.", $"✓ {algorithm} 相符 · 檔案 HASH 一致。", $"✓ {algorithm} が一致 · ファイルのハッシュを確認しました。");
            HashComparison.SetResourceReference(TextBlock.ForegroundProperty, algorithm is null ? "ErrorBrush" : "SuccessBrush");
        }
    }

    private async void ChooseHashClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = T("Choose a file to check its hashes", "選擇要檢查 HASH 的檔案", "ハッシュを確認するファイルを選択"), Filter = T("All files|*.*", "所有檔案|*.*", "すべてのファイル|*.*"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await LoadHashFileAsync(dialog.FileName);
    }

    private async void ChooseImagesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = T("Choose images to convert to ICO", "選擇要轉成 ICO 的圖片", "ICO に変換する画像を選択"), Filter = T("Images (PNG, JPG, BMP)|*.png;*.jpg;*.jpeg;*.bmp", "圖片（PNG、JPG、BMP）|*.png;*.jpg;*.jpeg;*.bmp", "画像（PNG、JPG、BMP）|*.png;*.jpg;*.jpeg;*.bmp"), Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await ConvertImagesAsync(dialog.FileNames);
    }

    private void CancelHashClick(object sender, RoutedEventArgs e)
    {
        _hashCancellation?.Cancel();
        CancelHashButton.IsEnabled = false;
        SetHashStatus(() => T("Cancelling…", "正在取消…", "キャンセル中…"));
    }

    private void CancelIconsClick(object sender, RoutedEventArgs e)
    {
        _iconCancellation?.Cancel();
        CancelIconsButton.IsEnabled = false;
        SetIconStatus(() => T("Cancelling… Completed ICO files will be preserved.", "正在取消…已完成的 ICO 會保留。", "キャンセル中…作成済みの ICO は保持されます。"));
    }

    private void CopyHashesClick(object sender, RoutedEventArgs e)
    {
        if (_hashResult is null) return;
        try
        {
            Clipboard.SetText($"MD5: {_hashResult.Md5}\r\nSHA-1: {_hashResult.Sha1}\r\nSHA-256: {_hashResult.Sha256}");
            SetHashStatus(() => T("Copied MD5, SHA-1 and SHA-256.", "已複製 MD5、SHA-1 與 SHA-256。", "MD5、SHA-1、SHA-256 をコピーしました。"));
        }
        catch (ExternalException) { SetHashStatus(() => T("Clipboard is busy. Try again or select and copy a result.", "剪貼簿忙碌，請稍後重試，或選取結果後複製。", "クリップボードを使用中です。再試行するか、結果を選択してコピーしてください。")); }
    }

    private void ShowIconClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: IconConversionItem { Success: true } item }) return;
        try
        {
            if (!File.Exists(item.OutputPath)) throw new FileNotFoundException();
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.OutputPath}\"") { UseShellExecute = true });
        }
        catch (Exception exception) { SetIconStatus(() => T("Unable to open folder: ", "無法開啟資料夾：", "フォルダーを開けません：") + Explain(exception)); }
    }

    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];

    private void HashDragOver(object sender, DragEventArgs e) => UpdateDrag(sender, e, DroppedFiles(e).Length == 1);
    private void IconDragOver(object sender, DragEventArgs e) => UpdateDrag(sender, e, _iconCancellation is null && DroppedFiles(e).Length > 0);

    private static void UpdateDrag(object sender, DragEventArgs e, bool accepts)
    {
        accepts = accepts && e.AllowedEffects.HasFlag(DragDropEffects.Copy);
        e.Effects = accepts ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (sender is Border border) border.SetResourceReference(Border.BackgroundProperty, accepts ? "HoverBrush" : "SurfaceBrush");
    }

    private static void ResetDropZone(object sender)
    {
        if (sender is Border border) border.ClearValue(Border.BackgroundProperty);
    }

    private void DropZoneDragLeave(object sender, DragEventArgs e) => ResetDropZone(sender);

    private async void HashDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        // The source receives the Drop effect, independently of DragOver feedback.
        // Never report Move: these tools only read the supplied source files.
        e.Effects = DragDropEffects.None;
        ResetDropZone(sender);
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return;
        var paths = DroppedFiles(e);
        if (paths.Length == 1)
        {
            e.Effects = DragDropEffects.Copy;
            await LoadHashFileAsync(paths[0]);
        }
        else
        {
            _hashCancellation?.Cancel();
            _hashCancellation = null;
            ClearHashResult();
            HashProgress.Value = 0;
            HashFileName.SetResourceReference(TextBlock.TextProperty, "ToolKeeper.OneFile");
            HashFileName.ToolTip = null;
            CancelHashButton.Visibility = Visibility.Collapsed;
            SetHashStatus(() => T("Please drop one file at a time.", "請一次拖入一個檔案。", "一度に 1 ファイルをドロップしてください。"));
        }
    }

    private async void IconDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        ResetDropZone(sender);
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return;
        if (_iconCancellation is not null)
        {
            SetIconStatus(() => T("Conversion is in progress. Wait or cancel this batch first.", "正在轉換，請等待完成，或先取消目前批次。", "変換中です。完了を待つか、現在の処理をキャンセルしてください。"));
            return;
        }
        var paths = DroppedFiles(e);
        if (paths.Length > 0)
        {
            e.Effects = DragDropEffects.Copy;
            await ConvertImagesAsync(paths);
        }
        else SetIconStatus(() => T("Drop PNG, JPG or BMP files from File Explorer.", "請從檔案總管拖入 PNG、JPG 或 BMP 檔案。", "エクスプローラーから PNG、JPG、BMP ファイルをドロップしてください。"));
    }

    private string Explain(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => T("File not found. It may have been moved or deleted.", "找不到檔案，可能已被移動或刪除。", "ファイルが見つかりません。移動または削除された可能性があります。"),
        UnauthorizedAccessException => T("Cannot access the file or folder. Check permissions and select a file, not a folder.", "無法存取檔案或資料夾，請確認權限，且不要拖入資料夾。", "ファイルまたはフォルダーにアクセスできません。権限を確認し、フォルダーではなくファイルを選択してください。"),
        NotSupportedException or FileFormatException => T("Unsupported or damaged image. Use a valid PNG, JPG or BMP.", "圖片格式不受支援或內容已損壞，請使用有效的 PNG、JPG 或 BMP。", "未対応または破損した画像です。有効な PNG、JPG、BMP を使用してください。"),
        IOException => T("Cannot read or write the file. Check file locks, folder permissions and free disk space. ", "無法讀寫檔案。請確認檔案未被占用、資料夾可寫入且磁碟空間足夠。 ", "ファイルを読み書きできません。ファイルの使用状況、権限、空き容量を確認してください。 ") + exception.Message,
        _ => exception.Message
    };

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        _closed = true;
        _hashCancellation?.Cancel();
        _iconCancellation?.Cancel();
    }
}
