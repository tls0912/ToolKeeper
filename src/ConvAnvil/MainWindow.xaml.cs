using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using ConvAnvil.Services;
using Microsoft.Win32;
using ToolKeeper.UI;

namespace ConvAnvil;

public partial class MainWindow : AppWindow
{
    internal const int TextInputLimit = 262_144;
    internal const int BytesInputLimit = 1_048_576;
    internal const int PreviewLimit = 24_000;
    private static readonly FormatChoice[] Formats =
    [
        new("Hex · 41 42", ByteFormat.Hex),
        new("Hex · 0x41 0x42", ByteFormat.HexPrefix),
        new("Hex · 41,42", ByteFormat.HexComma),
        new(@"Hex · \x41\x42", ByteFormat.HexEscape),
        new("Decimal · 65 66", ByteFormat.Decimal),
        new("Binary · 01000001", ByteFormat.Binary)
    ];
    private bool _ready;
    private bool _settingFile;
    private bool _closed;
    private readonly SemaphoreSlim _filePreviewGate = new(1, 1);
    private long _fileRevision;
    private long _textRevision;
    private long _bytesRevision;
    private FileSnapshot? _file;
    private byte[]? _convertedFile;
    private string _sourceText = "";
    private string _targetText = "";
    private string? _encodedNotation;
    private string? _decodedText;

    public MainWindow()
    {
        InitializeComponent();
        PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "ConvAnvil", "ui.json");
        AboutAuthor = "不告訴你";
        UiPreferencesChanged += (_, _) => UpdateProductPreferences();
        ApplyUiPreferences();
        foreach (var picker in new[] { SourceEncoding, TargetEncoding, TextEncoding, BytesEncoding })
        {
            picker.ItemsSource = EncodingCatalog.All;
            picker.SelectedItem = EncodingCatalog.Default;
        }
        TextFormat.ItemsSource = Formats;
        BytesFormat.ItemsSource = Formats;
        TextFormat.SelectedIndex = 0;
        BytesFormat.SelectedIndex = 0;
        Closed += (_, _) => { _closed = true; _fileRevision++; _textRevision++; _bytesRevision++; };
        _ready = true;
    }

    public async Task LoadFileAsync(string path)
    {
        var revision = ++_fileRevision;
        _file = null;
        _convertedFile = null;
        _sourceText = _targetText = "";
        SourcePreview.Clear();
        TargetPreview.Clear();
        ClearDetails();
        SaveFileButton.IsEnabled = false;
        OpenFileButton.IsEnabled = false;
        SetFileOptionsEnabled(false);
        SetResult(FileResult, () => T("Reading and inspecting the file…", "正在讀取並診斷檔案…", "ファイルを読み取り、診断中…"));
        SetText(StatusLabel, () => T("Reading file…", "正在讀取檔案…", "ファイルを読み込み中…"));
        try
        {
            var snapshot = await FileConversionService.ReadAsync(path);
            if (_closed || revision != _fileRevision) return;
            _file = snapshot;
            SetText(FileNameLabel, () => Path.GetFileName(snapshot.Path));
            FilePathLabel.Text = snapshot.Path;
            FilePathLabel.ToolTip = snapshot.Path;
            var detection = snapshot.Detection;
            SetText(DetectionLabel, () => DescribeDetection(snapshot));
            _settingFile = true;
            // Some detector candidates may be supported by .NET but outside the pinned shortlist.
            SourceEncoding.ItemsSource = detection.Candidate is { } candidate && !EncodingCatalog.All.Contains(candidate)
                ? EncodingCatalog.All.Concat([candidate]).ToArray() : EncodingCatalog.All;
            SourceEncoding.SelectedItem = detection.Candidate ?? EncodingCatalog.Default;
            _settingFile = false;
            SetText(StatusLabel, () => T($"Read {snapshot.Bytes.Length:N0} bytes · Preview limited to {PreviewLimit:N0} characters; conversion checks the entire file",
                $"已讀取 {snapshot.Bytes.Length:N0} bytes · 預覽最多 {PreviewLimit:N0} 個字元；轉換檢查整份檔案",
                $"{snapshot.Bytes.Length:N0} bytes 読み込み済み · プレビューは最大 {PreviewLimit:N0} 文字、変換はファイル全体を確認"));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (_closed || revision != _fileRevision) return;
            SetText(FileNameLabel, () => T("Unable to open file", "無法開啟檔案", "ファイルを開けません"));
            FilePathLabel.Text = path;
            SetText(DetectionLabel, () => T("No file loaded.", "尚未載入檔案。", "ファイルが読み込まれていません。"));
            SetResult(FileResult, () => T("Unable to open file: ", "無法開啟檔案：", "ファイルを開けません：") + ex.Message, true);
            SetText(StatusLabel, () => T("File not loaded", "檔案未載入", "ファイル未読み込み"));
        }
        finally
        {
            if (!_closed && revision == _fileRevision)
            {
                OpenFileButton.IsEnabled = true;
                SetFileOptionsEnabled(true);
                _settingFile = false;
            }
        }
        if (!_closed && revision == _fileRevision && _file is not null) await RebuildFilePreviewAsync();
    }

    internal async Task RebuildFilePreviewAsync()
    {
        if (!_ready || _settingFile || _file is null) return;
        var revision = ++_fileRevision;
        var snapshot = _file;
        var source = (EncodingOption)SourceEncoding.SelectedItem;
        var target = (EncodingOption)TargetEncoding.SelectedItem;
        var lineEnding = FileLineEnding.SelectedIndex;
        _convertedFile = null;
        _sourceText = _targetText = "";
        SaveFileButton.IsEnabled = false;
        SourcePreview.Clear();
        TargetPreview.Clear();
        ClearDetails();
        SetResult(FileResult, () => T("Checking the full content and conversion…", "正在檢查完整內容與轉換結果…", "内容全体と変換結果を確認中…"));
        await _filePreviewGate.WaitAsync();
        try
        {
            if (_closed || revision != _fileRevision) return;
            var result = await Task.Run(() =>
            {
                var text = EncodingConversionService.Decode(snapshot.Bytes, source);
                var sourceInspection = TextInspector.Inspect(text);
                var normalized = Normalize(text, lineEnding);
                try
                {
                    var bytes = EncodingConversionService.Encode(normalized, target);
                    // Keep the verified original text: decoding BOM-prefixed output could hide a
                    // genuine initial U+FEFF character in UTF-8 without a BOM.
                    return new FilePreview(text, normalized, bytes, sourceInspection, TextInspector.Inspect(normalized), null);
                }
                catch (EncodingConversionException ex)
                {
                    return new FilePreview(text, "", null, sourceInspection, null, ex.Message);
                }
            });
            if (_closed || revision != _fileRevision) return;
            _sourceText = result.Source;
            _targetText = result.Target;
            _convertedFile = result.Bytes;
            SetText(SourceDetails, () => Describe(result.SourceInspection, snapshot.Bytes.Length));
            SetText(TargetDetails, () => result.Bytes is null ? "" : Describe(result.TargetInspection!, result.Bytes.Length));
            RefreshFileDisplay();
            SaveFileButton.IsEnabled = result.Bytes is not null;
            SetResult(FileResult, () => result.Error is not null
                ? T($"Conversion failed: {result.Error} Choose an Encoding that supports the content.",
                    $"無法轉換：{result.Error} 請改選可表示內容的 Encoding。",
                    $"変換できません：{result.Error} 内容に対応した Encoding を選択してください。")
                : T("The entire file passed conversion checks. Saving creates a new file and preserves the original.",
                    "完整內容已通過轉換檢查。另存時建立新檔，來源檔保持原樣。",
                    "内容全体の変換を確認しました。新しいファイルを保存し、元ファイルは保持します。"), result.Error is not null);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (_closed || revision != _fileRevision) return;
            SetResult(FileResult, () => T($"Source decoding failed: {ex.Message} Adjust the source Encoding.",
                $"來源解碼失敗：{ex.Message} 請調整來源 Encoding。", $"入力のデコードに失敗：{ex.Message} 入力 Encoding を変更してください。"), true);
        }
        finally { _filePreviewGate.Release(); }
    }

    private void RefreshFileDisplay()
    {
        var controls = FileControls.IsChecked == true;
        SourcePreview.Text = DisplayPreview(_sourceText, controls);
        TargetPreview.Text = DisplayPreview(_targetText, controls);
    }

    private async void OpenFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Open text file", "開啟文字檔", "テキストファイルを開く"),
            Filter = T("Text files", "文字檔案", "テキストファイル") + "|*.txt;*.csv;*.log;*.md;*.json;*.xml;*.ini|" + T("All files", "所有檔案", "すべてのファイル") + "|*.*"
        };
        if (dialog.ShowDialog(this) == true) await LoadFileAsync(dialog.FileName);
    }

    private async void SaveFileClick(object sender, RoutedEventArgs e)
    {
        if (_file is null || _convertedFile is null) return;
        var original = _file.Path;
        var bytes = _convertedFile;
        var revision = _fileRevision;
        var dialog = new SaveFileDialog
        {
            Title = T("Save converted file (use a new name)", "另存轉換結果（請使用新檔名）", "変換結果を保存（新しい名前を使用）"),
            Filter = T("All files", "所有檔案", "すべてのファイル") + "|*.*", OverwritePrompt = false,
            InitialDirectory = Path.GetDirectoryName(original),
            FileName = Path.GetFileNameWithoutExtension(original) + ".converted" + Path.GetExtension(original)
        };
        if (dialog.ShowDialog(this) != true) return;
        SaveFileButton.IsEnabled = false;
        try
        {
            await FileConversionService.SaveNewAsync(dialog.FileName, bytes, original);
            if (!_closed) SetText(StatusLabel, () => T($"Saved {bytes.Length:N0} bytes: {dialog.FileName}",
                $"已另存 {bytes.Length:N0} bytes：{dialog.FileName}", $"{bytes.Length:N0} bytes を保存：{dialog.FileName}"));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (!_closed) SetResult(FileResult, () => T("Not saved: ", "未儲存：", "保存できません：") + ex.Message, true);
        }
        finally
        {
            if (!_closed && revision == _fileRevision) SaveFileButton.IsEnabled = _convertedFile is not null;
        }
    }

    internal async Task EncodeTextAsync()
    {
        var revision = ++_textRevision;
        ClearEncodedResult();
        var input = TextEntry.Text;
        if (input.Length > TextInputLimit)
        {
            TextControlPreview.Clear();
            SetResult(TextResult, () => T($"Text exceeds {TextInputLimit:N0} UTF-16 units. Reduce the input or use File. Nothing was truncated.",
                $"文字超過 {TextInputLimit:N0} 個 UTF-16 單位；請縮小輸入，或使用 File 區。內容未截斷。",
                $"テキストが {TextInputLimit:N0} UTF-16 単位を超えています。入力を減らすか File を使用してください。内容は切り捨てていません。"), true);
            return;
        }
        var encoding = (EncodingOption)TextEncoding.SelectedItem;
        var format = ((FormatChoice)TextFormat.SelectedItem).Value;
        SetResult(TextResult, () => T("Converting…", "正在轉換…", "変換中…"));
        try
        {
            var result = await Task.Run(() =>
            {
                var bytes = EncodingConversionService.Encode(input, encoding);
                return (Notation: ByteNotation.Format(bytes, format), Inspection: TextInspector.Inspect(input), ByteCount: bytes.Length);
            });
            if (_closed || revision != _textRevision) return;
            _encodedNotation = result.Notation;
            EncodedOutput.Text = DisplayPreview(result.Notation);
            TextControlPreview.Text = DisplayPreview(input, true);
            CopyBytesButton.IsEnabled = SendBytesButton.IsEnabled = true;
            SetResult(TextResult, () => Describe(result.Inspection, result.ByteCount) + T(" · Bytes include the selected BOM; copying uses the full result.",
                " · Bytes 包含所選 BOM；複製會取得完整結果。", " · Bytes は選択した BOM を含みます。コピーは結果全体を取得します。"));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (_closed || revision != _textRevision) return;
            TextControlPreview.Text = DisplayPreview(input, true);
            SetResult(TextResult, () => T("Conversion failed: ", "轉換失敗：", "変換に失敗：") + ex.Message, true);
            if (ex is EncodingConversionException conversion && !conversion.IsByteOffset)
                SelectError(TextEntry, conversion.Offset);
        }
    }

    internal async Task DecodeBytesAsync()
    {
        var revision = ++_bytesRevision;
        ClearDecodedResult();
        var input = BytesInput.Text;
        if (input.Length > BytesInputLimit)
        {
            SetResult(BytesResult, () => T($"Input exceeds {BytesInputLimit:N0} characters. Reduce the data. Nothing was truncated.",
                $"輸入超過 {BytesInputLimit:N0} 個字元；請縮小資料。內容未截斷。",
                $"入力が {BytesInputLimit:N0} 文字を超えています。データを減らしてください。内容は切り捨てていません。"), true);
            return;
        }
        var encoding = (EncodingOption)BytesEncoding.SelectedItem;
        var format = ((FormatChoice)BytesFormat.SelectedItem).Value;
        var stripBom = StripByteBom.IsChecked == true;
        SetResult(BytesResult, () => T("Parsing and decoding…", "正在解析並解碼…", "解析してデコード中…"));
        try
        {
            var result = await Task.Run(() =>
            {
                var bytes = ByteNotation.Parse(input, format);
                var text = EncodingConversionService.Decode(bytes, encoding, stripBom);
                return (Text: text, Inspection: TextInspector.Inspect(text), ByteCount: bytes.Length);
            });
            if (_closed || revision != _bytesRevision) return;
            _decodedText = result.Text;
            DecodedOutput.Text = DisplayPreview(result.Text);
            BytesControlPreview.Text = DisplayPreview(result.Text, true);
            var containsNul = result.Text.Contains('\0');
            CopyTextButton.IsEnabled = !containsNul;
            SetResult(BytesResult, () => Describe(result.Inspection, result.ByteCount) + (containsNul
                ? T(" · Contains NUL, which the text clipboard cannot preserve. Keep the Bytes.",
                    " · 含 NUL，文字剪貼簿無法完整保存；請保留 Bytes。", " · NUL を含むため、テキストのクリップボードでは保持できません。Bytes を保存してください。")
                : T(" · Copying uses the full decoded text.", " · 複製會取得完整解碼文字。", " · コピーはデコードしたテキスト全体を取得します。")));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            if (_closed || revision != _bytesRevision) return;
            SetResult(BytesResult, () => T("Decoding failed: ", "解碼失敗：", "デコードに失敗：") + ex.Message, true);
            if (ex is ByteParseException parse) SelectError(BytesInput, parse.Offset);
        }
    }

    private void InvalidateText()
    {
        if (!_ready) return;
        _textRevision++;
        ClearEncodedResult();
        TextControlPreview.Text = TextEntry.Text.Length <= TextInputLimit ? DisplayPreview(TextEntry.Text, true) : "";
        SetResult(TextResult, () => T("Input or options changed. Press Text → Bytes to convert again. Literal \\r\\n is not converted to a line break.",
            "輸入或選項已變更，請按 Text → Bytes 重新轉換。字面文字 \\r\\n 不會自動轉為換行。",
            "入力または設定が変更されました。Text → Bytes で再変換してください。文字列の \\r\\n は改行に変換されません。"));
    }

    private void InvalidateBytes()
    {
        if (!_ready) return;
        _bytesRevision++;
        ClearDecodedResult();
        SetResult(BytesResult, () => T("Input or options changed. Press Bytes → Text to decode again.",
            "輸入或選項已變更，請按 Bytes → Text 重新解碼。", "入力または設定が変更されました。Bytes → Text で再度デコードしてください。"));
    }

    private void ClearEncodedResult()
    {
        _encodedNotation = null;
        EncodedOutput.Clear();
        CopyBytesButton.IsEnabled = SendBytesButton.IsEnabled = false;
    }

    private void ClearDecodedResult()
    {
        _decodedText = null;
        DecodedOutput.Clear();
        BytesControlPreview.Clear();
        CopyTextButton.IsEnabled = false;
    }

    private async void FileOptionsChanged(object sender, SelectionChangedEventArgs e) => await RebuildFilePreviewAsync();
    private void FileControlsChanged(object sender, RoutedEventArgs e) { if (_ready) RefreshFileDisplay(); }
    private void TextOptionsChanged(object sender, SelectionChangedEventArgs e) => InvalidateText();
    private void TextInputChanged(object sender, TextChangedEventArgs e) => InvalidateText();
    private void BytesOptionsChanged(object sender, SelectionChangedEventArgs e) => InvalidateBytes();
    private void BytesInputChanged(object sender, TextChangedEventArgs e) => InvalidateBytes();
    private void BytesBomChanged(object sender, RoutedEventArgs e) => InvalidateBytes();
    private async void EncodeClick(object sender, RoutedEventArgs e) => await EncodeTextAsync();
    private async void DecodeClick(object sender, RoutedEventArgs e) => await DecodeBytesAsync();
    private void NormalizeLfClick(object sender, RoutedEventArgs e) => NormalizeInput(false);
    private void NormalizeCrLfClick(object sender, RoutedEventArgs e) => NormalizeInput(true);

    private void NormalizeInput(bool crlf)
    {
        if (TextEntry.Text.Length > TextInputLimit)
        {
            SetResult(TextResult, () => T($"Text exceeds {TextInputLimit:N0} UTF-16 units. Reduce the input.",
                $"文字超過 {TextInputLimit:N0} 個 UTF-16 單位；請縮小輸入。", $"テキストが {TextInputLimit:N0} UTF-16 単位を超えています。入力を減らしてください。"), true);
            return;
        }
        var normalized = TextInspector.NormalizeLineEndings(TextEntry.Text, crlf);
        TextEntry.SelectAll();
        TextEntry.SelectedText = normalized;
        TextEntry.Select(0, 0);
        SetResult(TextResult, () => T($"Line endings changed to {(crlf ? "CRLF" : "LF")}. Use Ctrl+Z to undo, or Text → Bytes to convert.",
            $"換行已改為 {(crlf ? "CRLF" : "LF")}；可按 Ctrl+Z 還原，再按 Text → Bytes 轉換。",
            $"改行を {(crlf ? "CRLF" : "LF")} に変更しました。Ctrl+Z で元に戻すか、Text → Bytes で変換できます。"));
    }

    private void CopyBytesClick(object sender, RoutedEventArgs e) { if (_encodedNotation is not null) CopyResult(_encodedNotation); }
    private void CopyTextClick(object sender, RoutedEventArgs e) { if (_decodedText is not null) CopyResult(_decodedText); }

    private void CopyResult(string value)
    {
        // Windows Unicode clipboard text cannot retain embedded NUL characters.
        if (value.Contains('\0'))
        {
            SetText(StatusLabel, () => T("The result contains NUL, which the Windows text clipboard cannot preserve. Use Bytes to retain all data.",
                "結果含 NUL，Windows 文字剪貼簿無法完整保存；請使用 Bytes 表示以保留所有資料。",
                "結果に NUL が含まれ、Windows のテキストクリップボードでは保持できません。全データを保存するには Bytes を使用してください。"));
            return;
        }
        try
        {
            if (value.Length == 0) { SetText(StatusLabel, () => T("The result is empty. The clipboard was left unchanged.", "結果為空，剪貼簿保持原樣。", "結果が空のため、クリップボードは変更していません。")); return; }
            Clipboard.SetText(value);
            SetText(StatusLabel, () => T($"Copied the full result ({value.Length:N0} UTF-16 units)",
                $"已複製完整結果（{value.Length:N0} 個 UTF-16 單位）", $"結果全体をコピーしました（{value.Length:N0} UTF-16 単位）"));
        }
        catch (ExternalException) { SetText(StatusLabel, () => T("The clipboard is busy. Try again shortly.", "剪貼簿正在使用中，請稍後再試。", "クリップボードが使用中です。しばらくしてから再試行してください。")); }
    }

    private async void SendBytesClick(object sender, RoutedEventArgs e)
    {
        if (_encodedNotation is null) return;
        if (_encodedNotation.Length > BytesInputLimit)
        {
            SetResult(TextResult, () => T($"The result exceeds the Bytes limit of {BytesInputLimit:N0} characters. You can still copy all Bytes.",
                $"結果超過 Bytes 區的 {BytesInputLimit:N0} 字元上限。仍可複製完整 Bytes。",
                $"結果が Bytes の上限 {BytesInputLimit:N0} 文字を超えています。Bytes 全体のコピーは可能です。"), true);
            return;
        }
        BytesEncoding.SelectedItem = TextEncoding.SelectedItem;
        StripByteBom.IsChecked = ((EncodingOption)TextEncoding.SelectedItem).EmitBom;
        BytesFormat.SelectedItem = TextFormat.SelectedItem;
        BytesInput.Text = _encodedNotation;
        WorkspaceTabs.SelectedItem = BytesTab;
        await DecodeBytesAsync();
    }

    private string DisplayPreview(string text, bool controls = false) => Preview(text, controls, ResolvedLanguage);

    internal static string Preview(string text, bool controls = false, string language = "zh-TW")
    {
        var length = Math.Min(text.Length, PreviewLimit);
        if (length < text.Length && length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        var result = text[..length];
        if (controls) result = TextInspector.VisualizeControls(result);
        return length < text.Length ? result + "\n\n⋯ " + UiLanguage.Text(language,
            $"Preview shows the first {length:N0} UTF-16 units; conversion and copying use the full content.",
            $"預覽僅顯示前 {length:N0} 個 UTF-16 單位；轉換與複製使用完整內容。",
            $"プレビューは先頭 {length:N0} UTF-16 単位です。変換とコピーは内容全体を使用します。") : result;
    }

    private static string Normalize(string text, int choice) => choice switch
    {
        1 => TextInspector.NormalizeLineEndings(text, false),
        2 => TextInspector.NormalizeLineEndings(text, true),
        _ => text
    };

    private string Describe(TextInspection info, int bytes)
    {
        var lineEnding = info.LineEnding switch
        {
            "None" => T("No line breaks", "無換行", "改行なし"),
            "Mixed" => T("Mixed", "混合換行", "改行の混在"),
            _ => info.LineEnding
        };
        return T($"{info.UnicodeScalarCount:N0} Unicode characters", $"{info.UnicodeScalarCount:N0} 個 Unicode 字元", $"{info.UnicodeScalarCount:N0} Unicode 文字")
            + $" · {bytes:N0} bytes · {lineEnding}\nCRLF {info.CrLfCount:N0} / LF {info.LfCount:N0} / CR {info.CrCount:N0} · TAB {info.TabCount:N0} / NUL {info.NulCount:N0} / STX {info.StxCount:N0} / ETX {info.EtxCount:N0}";
    }

    private static void SelectError(TextBox input, int offset)
    {
        if (input.IsVisible && PresentationSource.FromVisual(input) is not null) input.Focus();
        input.Select(Math.Clamp(offset, 0, input.Text.Length), offset < input.Text.Length ? 1 : 0);
    }

    private void SetResult(TextBlock output, Func<string> message, bool error = false)
    {
        SetText(output, message);
        output.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "SuccessBrush");
    }

    private void SetFileOptionsEnabled(bool enabled)
    {
        SourceEncoding.IsEnabled = TargetEncoding.IsEnabled = FileLineEnding.IsEnabled = enabled;
    }

    private static bool IsExpected(Exception ex) => ex is IOException or UnauthorizedAccessException
        or FormatException or ArgumentException or NotSupportedException or OperationCanceledException;

    public sealed record FormatChoice(string Name, ByteFormat Value);
    private sealed record FilePreview(string Source, string Target, byte[]? Bytes, TextInspection SourceInspection, TextInspection? TargetInspection, string? Error);
}
