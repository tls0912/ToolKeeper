using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ToolKeeper.Services;
using ToolKeeper.UI;

namespace ToolKeeper.Modules;

public partial class HashCheckerWindow : AppWindow
{
    private CancellationTokenSource? _hashCancellation;
    private FileHashResult? _hashResult;
    private Func<string>? _hashStatusText;
    private bool _closed;

    public HashCheckerWindow()
    {
        InitializeComponent();
        PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "ToolKeeper", "ui.json");
        AboutAuthor = "不告訴你";
        _hashStatusText = () => T("No file selected.", "尚未選擇檔案。", "ファイルが選択されていません。");
        UiPreferencesChanged += (_, _) => UpdatePreferences();
        ApplyUiPreferences();
    }

    private void UpdatePreferences()
    {
        foreach (var (key, english, chinese, japanese) in Labels)
            Resources["ToolKeeper." + key] = T(english, chinese, japanese);
        Description = T("Calculate and compare MD5, SHA-1 and SHA-256 hashes.",
            "計算與比對檔案的 MD5、SHA-1 及 SHA-256。", "ファイルの MD5・SHA-1・SHA-256 を計算して照合します。");
        if (HashFileName.ToolTip is null)
            HashFileName.SetResourceReference(TextBlock.TextProperty, "ToolKeeper.OneFile");
        if (_hashStatusText is not null) HashStatus.Text = _hashStatusText();
        UpdateComparison();
    }

    private void SetHashStatus(Func<string> text)
    {
        _hashStatusText = text;
        HashStatus.Text = text();
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

    private void CancelHashClick(object sender, RoutedEventArgs e)
    {
        _hashCancellation?.Cancel();
        CancelHashButton.IsEnabled = false;
        SetHashStatus(() => T("Cancelling…", "正在取消…", "キャンセル中…"));
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

    private void HashDragOver(object sender, DragEventArgs e) => UpdateDrag(sender, e, IsSingleFile(DroppedFiles(e)));

    private async void HashDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        // The source receives the Drop effect, independently of DragOver feedback.
        // Never report Move: these tools only read the supplied source files.
        e.Effects = DragDropEffects.None;
        ResetDropZone(sender);
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return;
        var paths = DroppedFiles(e);
        if (IsSingleFile(paths))
        {
            e.Effects = DragDropEffects.Copy;
            await LoadHashFileAsync(paths[0]);
        }
        else if (_hashCancellation is null && _hashResult is null)
        {
            SetHashStatus(() => T("Please drop one existing file, not a folder.", "請拖入一個現有檔案，不接受資料夾。", "フォルダーではなく、存在するファイルを 1 つドロップしてください。"));
        }
    }

    private static bool IsSingleFile(string[] paths) => paths.Length == 1 && File.Exists(paths[0]);

    private static string[] DroppedFiles(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths ? paths : [];

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
    }

    private static readonly (string Key, string English, string Chinese, string Japanese)[] Labels =
    [
        ("HashHelp", "Hash result; select and copy", "雜湊結果，可選取並複製", "ハッシュ結果：選択してコピーできます"),
        ("HashTitle", "Hash Checker", "Hash Checker", "ハッシュ確認"),
        ("HashDropHint", "Drop a file here to calculate its hashes", "把檔案拖進這裡，立即顯示 HASH", "ファイルをドロップしてハッシュを計算"),
        ("ChooseFile", "Choose file", "選擇檔案", "ファイルを選択"),
        ("OneFile", "One file at a time", "一次檢查一個檔案", "一度に 1 ファイル"),
        ("CopyAll", "Copy all", "複製全部", "すべてコピー"),
        ("CompareHint", "Paste an expected hash to compare", "貼上預期 HASH，自動比對", "照合するハッシュを貼り付け"),
        ("ExpectedHash", "Expected hash", "預期雜湊值", "照合するハッシュ"),
        ("CompareEmpty", "Compare MD5, SHA-1 or SHA-256.", "可比對 MD5、SHA-1 或 SHA-256。", "MD5、SHA-1、SHA-256 を照合できます。"),
        ("NoFile", "No file selected.", "尚未選擇檔案。", "ファイルが選択されていません。"),
        ("Cancel", "Cancel", "取消", "キャンセル"),
        ("Privacy", "Your files stay on this computer. Every operation runs locally.", "檔案留在你的電腦，每次操作都在本機完成。", "ファイルはこのコンピューターに保持され、すべての処理はローカルで実行されます。"),
    ];
}
