using System.ComponentModel;
using System.IO;
using System.Windows;

namespace ToolKeeper;

public partial class MainWindow
{
    private Func<string>? _hashStatusText;
    private Func<string>? _iconStatusText;
    private readonly ProductItem[] _products =
    [
        new("汗青", "Markdown reading and editing", "Markdown 閱讀與編輯", "Markdown の閲覧と編集"),
        new("CabiDock", "Automatic desktop file organization", "桌面檔案自動分類", "デスクトップのファイルを自動整理"),
        new("ConvAnvil", "Text, encoding and byte conversion", "文字、編碼與位元組轉換", "テキスト・エンコード・バイト変換")
    ];

    private void InitializeProductPreferences()
    {
        PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "ToolKeeper", "ui.json");
        AboutAuthor = "不告訴你";
        Products.ItemsSource = _products;
        _hashStatusText = () => T("No file selected.", "尚未選擇檔案。", "ファイルが選択されていません。");
        _iconStatusText = () => T("No images selected.", "尚未選擇圖片。", "画像が選択されていません。");
        UiPreferencesChanged += (_, _) => UpdateProductPreferences();
        ApplyUiPreferences();
    }

    private void UpdateProductPreferences()
    {
        Description = T("Make small tasks easier. Check file hashes and convert images to ICO.",
            "讓小事，變得順手。檢查檔案雜湊、將圖片轉為 ICO。", "小さな作業を手軽に。ファイルのハッシュ確認と画像の ICO 変換。");
        foreach (var (key, english, chinese, japanese) in Labels)
            Resources["ToolKeeper." + key] = T(english, chinese, japanese);
        foreach (var product in _products) product.Translate(ResolvedLanguage);
        foreach (var item in _iconItems) item.RefreshMessage();
        if (HashFileName.ToolTip is null)
            HashFileName.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "ToolKeeper.OneFile");
        if (_hashStatusText is not null) HashStatus.Text = _hashStatusText();
        if (_iconStatusText is not null) IconStatus.Text = _iconStatusText();
        UpdateComparison();
    }

    private void SetHashStatus(Func<string> text)
    {
        _hashStatusText = text;
        HashStatus.Text = text();
    }

    private void SetIconStatus(Func<string> text)
    {
        _iconStatusText = text;
        IconStatus.Text = text();
    }

    private static readonly (string Key, string English, string Chinese, string Japanese)[] Labels =
    [
        ("HashHelp", "Hash result; select and copy", "雜湊結果，可選取並複製", "ハッシュ結果：選択してコピーできます"),
        ("Badge", "Free · Local · Offline", "免費 · 本機處理 · 離線可用", "無料 · ローカル · オフライン"),
        ("HashTitle", "Hash Checker", "Hash Checker", "ハッシュ確認"),
        ("HashDropHint", "Drop a file here to calculate its hashes", "把檔案拖進這裡，立即顯示 HASH", "ファイルをドロップしてハッシュを計算"),
        ("ChooseFile", "Choose file", "選擇檔案", "ファイルを選択"),
        ("Cancel", "Cancel", "取消", "キャンセル"),
        ("OneFile", "One file at a time", "一次檢查一個檔案", "一度に 1 ファイル"),
        ("CopyAll", "Copy all", "複製全部", "すべてコピー"),
        ("CompareHint", "Paste an expected hash to compare", "貼上預期 HASH，自動比對", "照合するハッシュを貼り付け"),
        ("ExpectedHash", "Expected hash", "預期雜湊值", "照合するハッシュ"),
        ("CompareEmpty", "Compare MD5, SHA-1 or SHA-256.", "可比對 MD5、SHA-1 或 SHA-256。", "MD5、SHA-1、SHA-256 を照合できます。"),
        ("NoFile", "No file selected.", "尚未選擇檔案。", "ファイルが選択されていません。"),
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
        ("ToolsTitle", "Tools", "工具列表", "ツール一覧"),
        ("ToolsPreview", "Catalog and launcher · Preview", "目錄與 Launcher · 規劃預覽", "カタログとランチャー · プレビュー"),
        ("OpenGet", "Open / Get", "開啟／取得", "開く／入手"),
        ("ComingLater", "Catalog and launcher actions are not available yet", "目錄與 Launcher 功能尚未實作", "カタログとランチャーの操作は準備中です"),
        ("Privacy", "Your files stay on this computer. Every operation runs locally.", "檔案留在你的電腦，每次操作都在本機完成。", "ファイルはこのコンピューターに保持され、すべての処理はローカルで実行されます。")
    ];

    private sealed class ProductItem(string name, string english, string chinese, string japanese) : INotifyPropertyChanged
    {
        public string Name { get; } = name;
        public string Description { get; private set; } = "";
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Translate(string language)
        {
            Description = UI.UiLanguage.Text(language, english, chinese, japanese);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }
}
