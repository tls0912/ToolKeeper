using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ToolKeeper.Services;
using ToolKeeper.UI;

namespace ToolKeeper.Modules;

public partial class ImageToIcoWindow : AppWindow
{
    private readonly System.Collections.ObjectModel.ObservableCollection<IconConversionItem> _iconItems = [];
    private CancellationTokenSource? _iconCancellation;
    private Func<string>? _iconStatusText;
    private bool _closed;

    public ImageToIcoWindow()
    {
        InitializeComponent();
        PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "ToolKeeper", "ui.json");
        AboutAuthor = "不告訴你";
        IconResults.ItemsSource = _iconItems;
        _iconStatusText = () => T("No images selected.", "尚未選擇圖片。", "画像が選択されていません。");
        UiPreferencesChanged += (_, _) => UpdatePreferences();
        ApplyUiPreferences();
    }

    private void UpdatePreferences()
    {
        foreach (var (key, english, chinese, japanese) in Labels)
            Resources["ToolKeeper." + key] = T(english, chinese, japanese);
        Description = T("Convert PNG, JPG and BMP images into multi-size icons.",
            "將 PNG、JPG、BMP 圖片轉成多尺寸 ICO。", "PNG・JPG・BMP 画像を複数サイズの ICO に変換します。");
        foreach (var item in _iconItems) item.RefreshMessage();
        if (_iconStatusText is not null) IconStatus.Text = _iconStatusText();
    }

    private void SetIconStatus(Func<string> text)
    {
        _iconStatusText = text;
        IconStatus.Text = text();
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

    private async void ChooseImagesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = T("Choose images to convert to ICO", "選擇要轉成 ICO 的圖片", "ICO に変換する画像を選択"), Filter = T("Images (PNG, JPG, BMP)|*.png;*.jpg;*.jpeg;*.bmp", "圖片（PNG、JPG、BMP）|*.png;*.jpg;*.jpeg;*.bmp", "画像（PNG、JPG、BMP）|*.png;*.jpg;*.jpeg;*.bmp"), Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await ConvertImagesAsync(dialog.FileNames);
    }

    private void CancelIconsClick(object sender, RoutedEventArgs e)
    {
        _iconCancellation?.Cancel();
        CancelIconsButton.IsEnabled = false;
        SetIconStatus(() => T("Cancelling… Completed ICO files will be preserved.", "正在取消…已完成的 ICO 會保留。", "キャンセル中…作成済みの ICO は保持されます。"));
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

    private void IconDragOver(object sender, DragEventArgs e) => UpdateDrag(sender, e, _iconCancellation is null && DroppedFiles(e).Length > 0);

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
        _iconCancellation?.Cancel();
    }

    private static readonly (string Key, string English, string Chinese, string Japanese)[] Labels =
    [
        ("IconTitle", "Image → ICO", "Image → ICO", "画像 → ICO"),
        ("IconDropHint", "Drop images here to create ICO files", "把圖片拖進這裡，直接產生 ICO", "画像をドロップして ICO ファイルを作成"),
        ("IconFormats", "PNG · JPG · BMP / Multiple images supported", "PNG · JPG · BMP ／ 可一次放入多張", "PNG · JPG · BMP ／ 複数画像に対応"),
        ("ChooseImages", "Choose images", "選擇圖片", "画像を選択"),
        ("OriginalFolder", "Save beside the source image", "輸出到原圖片資料夾", "元の画像と同じフォルダーに保存"),
        ("IconEmpty", "Drop images and you're done.", "放入圖片，就完成。", "画像をドロップするだけ。"),
        ("IconResults", "ICO conversion results", "ICO 轉換結果", "ICO 変換結果"),
        ("ShowInFolder", "Show in folder", "在資料夾中顯示", "フォルダーで表示"),
        ("IconSizes", "16–256 px sizes · Keep proportions and transparency", "16–256 px 多尺寸 · 保留比例與透明背景", "16–256 px の複数サイズ · 比率と透明度を保持"),
        ("IconNames", "Duplicate names are numbered; originals are preserved.", "同名檔案會自動編號，保留原圖。", "同名ファイルには番号を付け、元の画像を保持します。"),
        ("NoImages", "No images selected.", "尚未選擇圖片。", "画像が選択されていません。"),
        ("Badge", "Free · Local · Offline", "免費 · 本機處理 · 離線可用", "無料 · ローカル · オフライン"),
        ("Cancel", "Cancel", "取消", "キャンセル"),
        ("Privacy", "Your files stay on this computer. Every operation runs locally.", "檔案留在你的電腦，每次操作都在本機完成。", "ファイルはこのコンピューターに保持され、すべての処理はローカルで実行されます。"),
    ];
}
