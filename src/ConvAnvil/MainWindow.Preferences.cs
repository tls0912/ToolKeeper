using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ConvAnvil.Services;
using ToolKeeper.UI;

namespace ConvAnvil;

public partial class MainWindow
{
    private readonly Dictionary<TextBlock, Func<string>> _localizedMessages = [];
    private string? _displayLanguage;

    private static readonly (string Key, string English, string Chinese, string Japanese)[] InterfaceText =
    [
        ("Conv.Description", """Inspect text encoding, preview conversions, and convert between text and bytes.""", """檢查文字編碼、預覽轉檔結果，並在文字與位元組之間轉換。""", """文字コードを調べ、変換結果を確認し、テキストとバイトを相互変換します。"""),
        ("Conv.FileTab", """File""", """File · 檔案""", """File · ファイル"""),
        ("Conv.TextTab", """Text""", """Text · 文字""", """Text · テキスト"""),
        ("Conv.BytesTab", """Bytes""", """Bytes · 位元組""", """Bytes · バイト"""),
        ("Conv.OpenFile", """Open file…""", """開啟檔案…""", """ファイルを開く…"""),
        ("Conv.FilePrompt", """Open a text file to inspect before converting""", """開啟文字檔，先檢查再轉換""", """テキストファイルを開き、変換前に確認"""),
        ("Conv.FileSupport", """TXT, CSV, LOG and other text files · Up to 16 MiB per file""", """支援 TXT、CSV、LOG 等文字檔 · 單檔上限 16 MiB""", """TXT、CSV、LOG など · 1 ファイル最大 16 MiB"""),
        ("Conv.SourceEncodingLabel", """Source Encoding · Override detection""", """來源 Encoding · 可手動更正推測""", """入力 Encoding · 推定結果を変更可能"""),
        ("Conv.SourceEncodingName", """Source Encoding""", """來源 Encoding""", """入力 Encoding"""),
        ("Conv.TargetEncodingLabel", """Output Encoding / BOM""", """輸出 Encoding / BOM""", """出力 Encoding / BOM"""),
        ("Conv.TargetEncodingName", """Output Encoding""", """輸出 Encoding""", """出力 Encoding"""),
        ("Conv.LineEnding", """Output line endings""", """輸出換行""", """出力の改行"""),
        ("Conv.Preserve", """Preserve""", """保留原樣""", """元のまま"""),
        ("Conv.DetectionHint", """Encoding detection is an estimate. Check the preview before saving a new file.""", """編碼偵測是推測。請確認預覽內容，再另存新檔。""", """文字コードは推定です。プレビューを確認してから新規保存してください。"""),
        ("Conv.SourceContent", """01 / Source content""", """01 / 來源內容""", """01 / 入力内容"""),
        ("Conv.SourcePreview", """Source content preview""", """來源內容預覽""", """入力内容のプレビュー"""),
        ("Conv.TargetContent", """02 / Conversion preview""", """02 / 轉換結果預覽""", """02 / 変換結果のプレビュー"""),
        ("Conv.TargetPreview", """Conversion preview""", """轉換結果預覽""", """変換結果のプレビュー"""),
        ("Conv.FileControls", """Show control character markers""", """顯示控制字元標記""", """制御文字のマーカーを表示"""),
        ("Conv.FileResultHint", """The entire file is checked for conversion loss before saving.""", """另存新檔前，會檢查整份內容是否能完整轉換。""", """新規保存の前に、内容全体を変換できるか確認します。"""),
        ("Conv.SaveFile", """Save converted file…""", """另存轉換結果…""", """変換結果を新規保存…"""),
        ("Conv.TextEncoding", """Text Encoding""", """文字 Encoding""", """テキスト Encoding"""),
        ("Conv.ByteDisplayFormat", """Bytes display format""", """Bytes 顯示格式""", """Bytes の表示形式"""),
        ("Conv.TextEntryLabel", """Text input · Paste or type""", """文字輸入 · 貼上或直接輸入""", """テキスト入力 · 貼り付けまたは入力"""),
        ("Conv.TextEntry", """Text input""", """文字輸入""", """テキスト入力"""),
        ("Conv.BytesOutput", """Bytes result""", """Bytes 結果""", """Bytes の結果"""),
        ("Conv.CopyBytes", """Copy Bytes""", """複製 Bytes""", """Bytes をコピー"""),
        ("Conv.SendBytes", """Send to Bytes""", """送往 Bytes 區""", """Bytes に送る"""),
        ("Conv.NormalizeLf", """Line endings → LF""", """換行 → LF""", """改行 → LF"""),
        ("Conv.NormalizeCrLf", """Line endings → CRLF""", """換行 → CRLF""", """改行 → CRLF"""),
        ("Conv.TextControlsLabel", """Control characters · ⟦CR⟧ ⟦LF⟧ ⟦TAB⟧ ⟦NUL⟧ · Literal backslashes appear as \\""", """控制字元檢視 · ⟦CR⟧ ⟦LF⟧ ⟦TAB⟧ ⟦NUL⟧ · 實際反斜線顯示為 \\""", """制御文字 · ⟦CR⟧ ⟦LF⟧ ⟦TAB⟧ ⟦NUL⟧ · バックスラッシュは \\ と表示"""),
        ("Conv.TextControls", """Text control character view""", """文字控制字元檢視""", """テキストの制御文字表示"""),
        ("Conv.TextResultHint", """Up to 262,144 UTF-16 units; literal \r\n is not converted to a line break.""", """支援最多 262,144 個 UTF-16 單位；\r\n 字面文字不會自動轉為換行。""", """最大 262,144 UTF-16 単位。文字列の \r\n は改行に変換されません。"""),
        ("Conv.BytesInputFormat", """Input number format""", """輸入數值格式""", """入力の数値形式"""),
        ("Conv.DecodeEncoding", """Decoding Encoding""", """解碼 Encoding""", """デコード Encoding"""),
        ("Conv.HexHint", """Hex accepts: 41 42 43  /  0x41 0x42 0x43  /  41,42,43  /  \x41\x42\x43""", """Hex 支援：41 42 43  /  0x41 0x42 0x43  /  41,42,43  /  \x41\x42\x43""", """Hex 対応形式：41 42 43  /  0x41 0x42 0x43  /  41,42,43  /  \x41\x42\x43"""),
        ("Conv.BytesInput", """Bytes input""", """Bytes 輸入""", """Bytes 入力"""),
        ("Conv.DecodedText", """Decoded text""", """解碼文字""", """デコードしたテキスト"""),
        ("Conv.CopyText", """Copy text""", """複製文字""", """テキストをコピー"""),
        ("Conv.StripBom", """Remove a leading BOM matching the selected Encoding""", """移除與所選 Encoding 相符的起始 BOM""", """選択した Encoding と一致する先頭 BOM を除去"""),
        ("Conv.BytesControlsLabel", """Control characters · NUL 00 / STX 02 / ETX 03 / TAB 09 / LF 0A / CR 0D""", """控制字元檢視 · NUL 00 / STX 02 / ETX 03 / TAB 09 / LF 0A / CR 0D""", """制御文字 · NUL 00 / STX 02 / ETX 03 / TAB 09 / LF 0A / CR 0D"""),
        ("Conv.BytesControls", """Bytes control character view""", """Bytes 控制字元檢視""", """Bytes の制御文字表示"""),
        ("Conv.BytesResultHint", """Choose a number format before decoding. Decimal and Binary are never guessed. Limit: 1,048,576 characters.""", """先選數值格式，再解碼；Decimal 與 Binary 不會自動猜測。輸入上限 1,048,576 個字元。""", """形式を選択してからデコードします。Decimal と Binary は推定しません。最大 1,048,576 文字。"""),
        ("Conv.Ready", """Ready · 003 / Preview 0.1""", """準備就緒 · 003 / 開發版 0.1""", """準備完了 · 003 / 開発版 0.1"""),
    ];

    private void UpdateProductPreferences()
    {
        SubName = T("Text, Encoding & Byte Converter", "文字、編碼與位元組轉換器", "テキスト・文字コード・バイト変換");
        foreach (var (key, english, chinese, japanese) in InterfaceText)
            Resources[key] = T(english, chinese, japanese);
        foreach (var (label, message) in _localizedMessages)
            label.Text = message();
        if (_displayLanguage == ResolvedLanguage) return;
        if (_displayLanguage is null)
        {
            foreach (var picker in new[] { SourceEncoding, TargetEncoding, TextEncoding, BytesEncoding })
            {
                // A stable template updates the cached selection display as well as
                // dropdown items, without changing any selected encoding objects.
                var label = new FrameworkElementFactory(typeof(TextBlock));
                var binding = new MultiBinding { Converter = new EncodingNameConverter() };
                binding.Bindings.Add(new Binding(nameof(EncodingOption.Name)));
                binding.Bindings.Add(new Binding(nameof(SelectedLanguage)) { Source = this });
                label.SetBinding(TextBlock.TextProperty, binding);
                picker.DisplayMemberPath = "";
                picker.ItemTemplate = new DataTemplate { VisualTree = label };
            }
        }
        _displayLanguage = ResolvedLanguage;
        if (!_ready) return;
        RefreshPreviewLanguage(SourcePreview, _sourceText, FileControls.IsChecked == true);
        RefreshPreviewLanguage(TargetPreview, _targetText, FileControls.IsChecked == true);
        RefreshPreviewLanguage(EncodedOutput, _encodedNotation ?? "");
        RefreshPreviewLanguage(DecodedOutput, _decodedText ?? "");
        RefreshPreviewLanguage(BytesControlPreview, _decodedText ?? "", true);
        if (TextEntry.Text.Length <= TextInputLimit)
            RefreshPreviewLanguage(TextControlPreview, TextEntry.Text, true);
    }

    private void RefreshPreviewLanguage(TextBox output, string text, bool controls = false)
    {
        if (text.Length <= PreviewLimit) return;
        var selection = output.SelectionStart;
        var length = output.SelectionLength;
        var horizontal = output.HorizontalOffset;
        var vertical = output.VerticalOffset;
        output.Text = DisplayPreview(text, controls);
        output.Select(selection, length);
        output.ScrollToHorizontalOffset(horizontal);
        output.ScrollToVerticalOffset(vertical);
    }

    private string DescribeDetection(FileSnapshot snapshot)
    {
        var detection = snapshot.Detection;
        var bom = detection.BomLength == 0 ? T("None", "無", "なし") : detection.BomName;
        var candidate = detection.Candidate is null
            ? T("Unknown; choose manually", "無法判定，請手動選擇", "不明、手動で選択してください")
            : EncodingDisplayName(detection.Candidate.Name, ResolvedLanguage);
        var confidence = detection.Confidence is double value
            ? T($" · Detector confidence {value:P0}", $" · 偵測器信心 {value:P0}", $" · 推定の信頼度 {value:P0}") : "";
        var reason = detection.Basis switch
        {
            EncodingDetectionBasis.Bom => T("Selected from BOM; the entire file must still pass decoding checks.",
                "依 BOM 選擇來源編碼；BOM 是格式標記，仍須通過完整內容解碼檢查。", "BOM に基づいて選択しました。内容全体のデコード確認が必要です。"),
            EncodingDetectionBasis.Empty => T("An empty file has no detectable encoding; using UTF-8.",
                "空檔案無法判定編碼；暫以 UTF-8 開啟。", "空のファイルは文字コードを判定できないため、UTF-8 で開きます。"),
            EncodingDetectionBasis.Ascii => T("ASCII-range bytes cannot identify the original encoding; using UTF-8. Files with NUL may also be UTF-16/32 without a BOM; check manually.",
                "內容只有 ASCII 範圍位元組，無法判定原始編碼；暫以 UTF-8 開啟。含 NUL 的檔案也可能是無 BOM 的 UTF-16／32，請手動確認。",
                "ASCII 範囲のみのため元の文字コードは不明です。UTF-8 で開きます。NUL を含む場合は BOM なし UTF-16/32 の可能性もあります。確認してください。"),
            EncodingDetectionBasis.Unrecognized => T("UTF.Unknown found no candidate. Choose a source Encoding manually.",
                "UTF.Unknown 無法提出候選編碼；請手動選擇來源編碼。", "UTF.Unknown は候補を検出できませんでした。入力 Encoding を選択してください。"),
            _ when detection.Candidate is null => T($"UTF.Unknown suggests {detection.DetectedEncodingName}, which is unavailable in this list. Choose manually; detection is not a guarantee.",
                detection.Reason, $"UTF.Unknown の推定は {detection.DetectedEncodingName} ですが、リストにありません。手動で確認してください。推定は保証ではありません。"),
            _ => T($"UTF.Unknown suggests {detection.DetectedEncodingName}. Check the text and source Encoding; detection is not a guarantee.",
                detection.Reason, $"UTF.Unknown の推定は {detection.DetectedEncodingName} です。文字と入力 Encoding を確認してください。推定は保証ではありません。")
        };
        return T($"BOM: {bom} · Estimate: {candidate}", $"BOM：{bom} · 推測：{candidate}", $"BOM：{bom} · 推定：{candidate}")
            + confidence + "\n" + reason + T(" Check the source preview.", " 請確認來源預覽。", " 入力のプレビューを確認してください。");
    }

    private static string EncodingDisplayName(string name, string language) => name
        .Replace("無 BOM", UiLanguage.Text(language, "no BOM", "無 BOM", "BOM なし"))
        .Replace("有 BOM", UiLanguage.Text(language, "with BOM", "有 BOM", "BOM あり"));

    private sealed class EncodingNameConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            EncodingDisplayName(values[0] as string ?? "", UiLanguage.Resolve(values[1] as string));
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }

    // Keep the message formatter, rather than a previously translated string, so a
    // language change can refresh status text without re-running or clearing work.
    private void SetText(TextBlock output, Func<string> message)
    {
        _localizedMessages[output] = message;
        output.Text = message();
    }

    private void ClearDetails()
    {
        _localizedMessages.Remove(SourceDetails);
        _localizedMessages.Remove(TargetDetails);
        SourceDetails.Text = TargetDetails.Text = "";
    }
}
