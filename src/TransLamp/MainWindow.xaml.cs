using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ToolKeeper.UI;
using TransLamp.Core;

namespace TransLamp;

public partial class MainWindow : AppWindow
{
    private readonly LanguagePackService _packService;
    private readonly TranslationEngine _engine;
    private readonly string _bundledDirectory;
    private IReadOnlyList<InstalledLanguagePack> _packs = [];
    private IReadOnlyList<TranslationLiteralDifference> _qualityDifferences = [];
    private CancellationTokenSource? _operation;
    private bool _initialized, _closed, _updatingLanguages, _loaded;
    private long _inputRevision;
    private Func<string>? _status;
    private bool _statusIsError;

    public MainWindow() : this(null, null) { }

    public MainWindow(LanguagePackService? packService, TranslationEngine? engine, string? bundledDirectory = null)
    {
        _engine = engine ?? new TranslationEngine();
        _packService = packService ?? new LanguagePackService(validator: _engine.ValidatePackAsync);
        _bundledDirectory = bundledDirectory ?? Path.Combine(AppContext.BaseDirectory, "LanguagePacks");
        InitializeComponent();
        PreferencesPath = Path.Combine(Path.GetDirectoryName(_packService.RootDirectory)!, "ui.json");
        AboutAuthor = "不告訴你";
        UiPreferencesChanged += (_, _) => UpdateLabels();
        _initialized = true;
        UpdateLabels();
        RefreshPacks();
        SetStatus(() => T("Ready. Paste text and choose a translation direction.", "就緒。貼上文字並選擇翻譯方向。", "準備完了。テキストを貼り付け、翻訳方向を選択してください。"));
    }

    private string SourceCode => SourceLanguage.SelectedValue as string ?? "en";
    private string TargetCode => TargetLanguage.SelectedValue as string ?? "zh";
    private bool IsBusy => _operation is not null;
    private bool IsSupportedDirection => SourceCode != TargetCode && SourceCode is "en" or "zh" && TargetCode is "en" or "zh";
    private InstalledLanguagePack? CurrentPack => _packs.FirstOrDefault(p => p.Manifest.SourceLanguage == SourceCode && p.Manifest.TargetLanguage == TargetCode);

    private void UpdateLabels()
    {
        if (!_initialized || _closed) return;
        foreach (var (key, value) in Strings.ForLanguage(ResolvedLanguage)) Resources[key] = value;
        var source = SourceCode;
        var target = TargetCode;
        _updatingLanguages = true;
        try
        {
            var languages = new[]
            {
                new LanguageOption("en", T("English", "English", "English")),
                new LanguageOption("zh", T("Chinese", "中文", "中国語")),
                new LanguageOption("ja", T("Japanese (not available)", "日本語（尚未提供）", "日本語（未対応）"))
            };
            SourceLanguage.ItemsSource = languages;
            TargetLanguage.ItemsSource = languages;
            SourceLanguage.SelectedValue = source;
            TargetLanguage.SelectedValue = target;
        }
        finally { _updatingLanguages = false; }
        UpdateCounts();
        UpdatePackList();
        UpdateAvailability();
        RenderQualityWarning();
        RenderStatus();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await PrepareBundledPacksAsync();
        if (!_closed) SourceText.Focus();
    }

    private async Task PrepareBundledPacksAsync()
    {
        var bundledDirectory = _bundledDirectory;
        if (Directory.Exists(bundledDirectory))
        {
            await RunOperationAsync(async token =>
            {
                SetStatus(() => T("Preparing bundled language packs…", "正在準備隨附語言包…", "同梱の言語パックを準備しています…"));
                var result = await _packService.InstallBundledWithResultAsync(bundledDirectory, null, token);
                if (!CanUpdate(token)) return;
                RefreshPacks();
                if (result.Failures.Count > 0)
                    SetStatus(() => T(
                        $"Prepared {result.InstalledCount} language pack(s); {result.Failures.Count} failed. Check the available direction above and reimport failed packs.",
                        $"已準備 {result.InstalledCount} 個語言包，{result.Failures.Count} 個失敗。請查看上方方向的可用狀態，並重新取得及匯入失敗的語言包。",
                        $"言語パック {result.InstalledCount} 個を準備、{result.Failures.Count} 個は失敗しました。上の翻訳方向の状態を確認し、失敗したパックを再取得してインポートしてください。"), true);
                else if (!_engine.IsAvailable)
                    SetStatus(() => T("Language packs checked, but the offline engine is missing. Use a complete TransLamp Offline Kit.", "已檢查語言包，但缺少離線引擎，請使用完整 TransLamp Offline Kit。", "言語パックを確認しましたが、オフラインエンジンがありません。完全な TransLamp Offline Kit を使用してください。"), true);
                else if (_packs.Count == 0)
                    SetStatus(() => T("No language packs installed. Import a compatible .tlpack file to translate.", "尚未安裝語言包，請匯入相容的 .tlpack 檔案後翻譯。", "言語パックがありません。互換性のある .tlpack ファイルをインポートしてください。"));
                else
                    SetStatus(() => result.InstalledCount > 0
                        ? T($"Prepared {result.InstalledCount} language pack(s). Installed directions are available for offline translation.", $"已準備 {result.InstalledCount} 個語言包，可使用已安裝的方向離線翻譯。", $"言語パックを {result.InstalledCount} 個準備しました。インストール済みの方向でオフライン翻訳できます。")
                        : T("Installed language packs are ready.", "已安裝的語言包已就緒。", "インストール済みの言語パックは準備できています。"));
            });
        }
    }

    private void DirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _updatingLanguages || _closed) return;
        InvalidateResult();
        UpdateAvailability();
        SetStatus(() => T("Direction changed. Translate again to get a new result.", "已變更方向，請重新翻譯。", "翻訳方向を変更しました。もう一度翻訳してください。"));
    }

    private void SwapClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var source = SourceCode;
        var target = TargetCode;
        _updatingLanguages = true;
        try { SourceLanguage.SelectedValue = target; TargetLanguage.SelectedValue = source; }
        finally { _updatingLanguages = false; }
        // Direction-only swap preserves the user's original text and never silently substitutes a translation.
        InvalidateResult();
        UpdateAvailability();
        SetStatus(() => T("Translation direction swapped; source text kept.", "已交換翻譯方向，保留原文。", "翻訳方向を交換しました。原文は保持されています。"));
    }

    private void SourceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _closed) return;
        InvalidateResult();
        UpdateCounts();
        UpdateAvailability();
        if (!IsBusy)
            SetStatus(() => string.IsNullOrWhiteSpace(SourceText.Text)
                ? T("Enter some text to translate.", "請先輸入要翻譯的文字。", "翻訳するテキストを入力してください。")
                : T("Source text changed. Translate again to get a new result.", "原文已變更，請重新翻譯。", "原文が変更されました。もう一度翻訳してください。"));
    }

    private void TargetTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized && !_closed) UpdateCounts();
    }

    private void InvalidateResult()
    {
        _inputRevision++;
        if (TargetText.Text.Length != 0) TargetText.Clear();
        ClearQualityWarning();
        CopyButton.IsEnabled = false;
    }

    private void UpdateCounts()
    {
        SourceCount.Text = T($"{SourceText.Text.Length:N0} / 20,000 characters", $"{SourceText.Text.Length:N0} / 20,000 字元", $"{SourceText.Text.Length:N0} / 20,000 文字");
        SourceCount.SetResourceReference(TextBlock.ForegroundProperty, SourceText.Text.Length > 20_000 ? "ErrorBrush" : "MutedBrush");
        TargetCount.Text = T($"{TargetText.Text.Length:N0} characters", $"{TargetText.Text.Length:N0} 字元", $"{TargetText.Text.Length:N0} 文字");
        SourcePlaceholder.Visibility = SourceText.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        TargetPlaceholder.Visibility = TargetText.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = !IsBusy && TargetText.Text.Length > 0;
    }

    private void RefreshPacks()
    {
        if (_closed) return;
        try { _packs = _packService.GetInstalledPacks(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TransLampException or System.Text.Json.JsonException)
        {
            _packs = [];
            SetStatus(() => FriendlyError(error), true);
        }
        UpdatePackList();
        UpdateAvailability();
    }

    private void UpdatePackList()
    {
        PacksList.ItemsSource = _packs.Select(pack => new PackRow(
            pack.Manifest.Id,
            $"{pack.Manifest.DisplayName}  ·  {pack.Manifest.SourceLanguage} → {pack.Manifest.TargetLanguage}",
            T($"Package {pack.Manifest.PackageVersion} · {FormatSize(pack.SizeBytes)} · {pack.Manifest.ModelName} {pack.Manifest.ModelVersion} · License: {pack.Manifest.LicenseIdentifier}",
              $"語言包 {pack.Manifest.PackageVersion} · {FormatSize(pack.SizeBytes)} · {pack.Manifest.ModelName} {pack.Manifest.ModelVersion} · 授權：{pack.Manifest.LicenseIdentifier}",
              $"パック {pack.Manifest.PackageVersion} · {FormatSize(pack.SizeBytes)} · {pack.Manifest.ModelName} {pack.Manifest.ModelVersion} · ライセンス：{pack.Manifest.LicenseIdentifier}"),
            T($"Source: {pack.Manifest.ModelSource}", $"來源：{pack.Manifest.ModelSource}", $"提供元：{pack.Manifest.ModelSource}"),
            !IsBusy)).ToArray();
        NoPacksText.Visibility = _packs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PackLocation.Text = T($"Language packs: {_packService.RootDirectory}", $"語言包目錄：{_packService.RootDirectory}", $"言語パック：{_packService.RootDirectory}");
        PackLocation.ToolTip = _packService.RootDirectory;
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024):N1} MB" : $"{bytes / 1024d:N1} KB";

    private void UpdateAvailability()
    {
        var pack = CurrentPack;
        AvailabilityText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        if (SourceCode == TargetCode)
            AvailabilityText.Text = T("Choose different source and target languages.", "請選擇不同的來源與目標語言。", "翻訳元と翻訳先には異なる言語を選択してください。");
        else if (!IsSupportedDirection)
            AvailabilityText.Text = T("Japanese packs are not available in this version. Choose Chinese ⇄ English.", "此版本尚未提供日文語言包，請選擇中文 ⇄ English。", "このバージョンは日本語の言語パックに未対応です。中国語 ⇄ English を選択してください。");
        else if (!_engine.IsAvailable)
            AvailabilityText.Text = T("Offline runtime not found. Use a complete TransLamp Offline Kit; importing a language pack alone is not enough.", "找不到離線翻譯引擎。請使用完整 TransLamp Offline Kit，僅匯入語言包仍無法翻譯。", "オフラインエンジンが見つかりません。言語パックだけでなく、完全な TransLamp Offline Kit が必要です。");
        else if (pack is null)
            AvailabilityText.Text = T("This direction has no installed language pack. Open Language packs and import a .tlpack file.", "此方向尚未安裝語言包。請展開「語言包管理」並匯入 .tlpack 檔案。", "この方向の言語パックは未インストールです。「言語パック」から .tlpack ファイルをインポートしてください。");
        else
        {
            AvailabilityText.Text = T($"Offline ready · {pack.Manifest.DisplayName} · v{pack.Manifest.PackageVersion} · CPU",
                $"離線就緒 · {pack.Manifest.DisplayName} · v{pack.Manifest.PackageVersion} · CPU",
                $"オフライン準備完了 · {pack.Manifest.DisplayName} · v{pack.Manifest.PackageVersion} · CPU");
            AvailabilityText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        }
        TranslateButton.IsEnabled = !IsBusy && IsSupportedDirection && _engine.IsAvailable && pack is not null
            && !string.IsNullOrWhiteSpace(SourceText.Text) && SourceText.Text.Length <= 20_000;
        PasteButton.IsEnabled = ClearButton.IsEnabled = SwapButton.IsEnabled = !IsBusy;
        SourceLanguage.IsEnabled = TargetLanguage.IsEnabled = ImportButton.IsEnabled = !IsBusy;
        SourceText.IsReadOnly = IsBusy;
        CancelButton.IsEnabled = IsBusy && _operation?.IsCancellationRequested == false;
        CopyButton.IsEnabled = !IsBusy && TargetText.Text.Length > 0;
    }

    private async void TranslateClick(object sender, RoutedEventArgs e) => await TranslateAsync();

    public async Task TranslateAsync()
    {
        if (_closed || IsBusy) return;
        UpdateAvailability();
        if (!TranslateButton.IsEnabled) return;
        var pack = CurrentPack!;
        var text = SourceText.Text;
        var revision = _inputRevision;
        ClearQualityWarning();
        TargetText.Clear();
        await RunOperationAsync(async token =>
        {
            SetStatus(() => T("Translating locally… The first translation may take longer while the model loads.", "正在本機翻譯…首次翻譯需載入模型，可能稍久。", "ローカルで翻訳中…初回はモデルの読み込みに時間がかかることがあります。"));
            var progress = new Progress<TranslationProgress>(value =>
            {
                if (!CanUpdate(token) || revision != _inputRevision) return;
                OperationProgress.IsIndeterminate = false;
                OperationProgress.Maximum = Math.Max(1, value.Total);
                OperationProgress.Value = value.Completed;
                SetStatus(() => T($"Translating locally · {value.Completed} / {value.Total} segments", $"正在本機翻譯 · {value.Completed} / {value.Total} 段", $"ローカルで翻訳中 · {value.Completed} / {value.Total} セグメント"));
            });
            var result = await _engine.TranslateAsync(text, pack, progress, token);
            if (!CanUpdate(token) || revision != _inputRevision) return;
            ApplyTranslationResult(text, result);
        });
    }

    private void ApplyTranslationResult(string source, string translation)
    {
        TargetText.Text = translation;
        _qualityDifferences = TranslationQualityCheck.FindChanges(source, translation);
        RenderQualityWarning();
        SetStatus(() => T("Translation complete. Verify names, numbers and technical terms against the source.", "翻譯完成。請對照原文確認名稱、數字與技術術語。", "翻訳が完了しました。固有名詞、数値、専門用語を原文と照合してください。"));
    }

    private void ClearQualityWarning()
    {
        _qualityDifferences = [];
        RenderQualityWarning();
    }

    private void RenderQualityWarning()
    {
        QualityWarning.Visibility = _qualityDifferences.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        QualityWarningText.Text = _qualityDifferences.Count == 0 ? "" : Resources["Lamp.LiteralWarning"] + " " +
            string.Join(" · ", _qualityDifferences.Select(difference => $"{difference.Literal} ({difference.TranslationOccurrences}/{difference.SourceOccurrences})"));
    }

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog
        {
            Title = T("Import offline language pack", "匯入離線語言包", "オフライン言語パックをインポート"),
            Filter = "TransLamp language pack (*.tlpack)|*.tlpack", CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) await ImportPackAsync(dialog.FileName);
    }

    public async Task ImportPackAsync(string path)
    {
        if (_closed || IsBusy) return;
        await RunOperationAsync(async token =>
        {
            SetStatus(() => T("Checking files and loading the model before import…", "正在檢查檔案並驗證模型載入，完成後匯入…", "インポート前にファイルとモデルの読み込みを検証しています…"));
            var pack = await _packService.ImportAsync(path, null, token);
            if (!CanUpdate(token)) return;
            RefreshPacks();
            PacksExpander.IsExpanded = true;
            SetStatus(() => T($"Imported {pack.Manifest.DisplayName} {pack.Manifest.PackageVersion}.", $"已匯入 {pack.Manifest.DisplayName} {pack.Manifest.PackageVersion}。", $"{pack.Manifest.DisplayName} {pack.Manifest.PackageVersion} をインポートしました。"));
        });
    }

    private async void RemovePackClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || sender is not Button { Tag: string id }) return;
        await RunOperationAsync(async token =>
        {
            SetStatus(() => T("Removing language pack…", "正在移除語言包…", "言語パックを削除しています…"));
            await Task.Run(() => { token.ThrowIfCancellationRequested(); _packService.Remove(id); }, token);
            if (!CanUpdate(token)) return;
            InvalidateResult();
            RefreshPacks();
            SetStatus(() => T("Language pack removed. You can import its .tlpack file again at any time.", "語言包已移除，隨時可重新匯入 .tlpack 檔案。", "言語パックを削除しました。.tlpack ファイルから再インポートできます。"));
        });
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> work)
    {
        if (_closed || IsBusy) return;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.IsIndeterminate = true;
        UpdateAvailability();
        UpdatePackList();
        try
        {
            await work(operation.Token);
            if (!_closed && operation.IsCancellationRequested) SetCancelledStatus();
        }
        catch (OperationCanceledException)
        {
            if (!_closed) SetCancelledStatus();
        }
        catch (Exception error)
        {
            if (!_closed) SetStatus(() => FriendlyError(error), true);
        }
        finally
        {
            if (ReferenceEquals(_operation, operation)) _operation = null;
            if (!_closed)
            {
                OperationProgress.Visibility = Visibility.Collapsed;
                RefreshPacks();
                UpdateCounts();
            }
        }
    }

    private bool CanUpdate(CancellationToken token) => !_closed && !token.IsCancellationRequested && _operation?.Token == token;

    private void SetCancelledStatus() => SetStatus(() => T("Operation cancelled.", "已取消目前作業。", "操作をキャンセルしました。"));

    private string FriendlyError(Exception error)
    {
        if (error is TransLampException lamp) return lamp.Code switch
        {
            "empty-input" => T("Enter some text to translate.", "請先輸入要翻譯的文字。", "翻訳するテキストを入力してください。"),
            "input-too-long" => T("Text exceeds 20,000 characters. Translate smaller sections.", "文字超過 20,000 字元，請分成較小段落翻譯。", "20,000 文字を超えています。文章を分けて翻訳してください。"),
            "runtime-missing" or "runtime-start" => T("The offline translation engine is missing or cannot start. Use a complete TransLamp Offline Kit.", "離線翻譯引擎缺漏或無法啟動，請使用完整 TransLamp Offline Kit。", "オフライン翻訳エンジンがないか、起動できません。完全な TransLamp Offline Kit を使用してください。"),
            "invalid-pack" => T("The language pack is damaged or has an unsupported format. Get a complete .tlpack file and import it again.", "語言包損壞或格式不相容，請重新取得完整 .tlpack 檔案後再匯入。", "言語パックが破損しているか、未対応の形式です。完全な .tlpack ファイルを取得し、再インポートしてください。"),
            "incompatible-runtime" => T("This language pack requires a different engine version. Use a compatible pack or update the Offline Kit.", "此語言包需要不同版本的引擎，請使用相容語言包或更新 Offline Kit。", "この言語パックには別のエンジンバージョンが必要です。互換性のあるパックを使用するか、Offline Kit を更新してください。"),
            "unsupported-direction" => T("This language direction is not supported. Choose Chinese ⇄ English and a matching language pack.", "尚未支援此翻譯方向，請選擇中文 ⇄ English 及對應語言包。", "この翻訳方向は未対応です。中国語 ⇄ English と対応する言語パックを選択してください。"),
            "invalid-model" => T("Language pack validation failed: the offline engine cannot load this model. Obtain a compatible, complete pack and import it again.", "語言包載入驗證失敗：離線引擎無法載入此模型。請重新取得相容且完整的語言包後匯入。", "言語パックの読み込み検証に失敗しました。オフラインエンジンでこのモデルを読み込めません。互換性のある完全なパックを取得し、再インポートしてください。"),
            "validation-timeout" => T("Language pack validation took too long and was stopped. Retry with a complete, compatible pack.", "語言包驗證時間過長，已停止。請使用完整且相容的語言包重試。", "言語パックの検証に時間がかかったため停止しました。完全で互換性のあるパックで再試行してください。"),
            "pack-recovery-failed" => T("The language pack operation could not finish. Existing data has been retained. Close other TransLamp windows and try again.", "語言包作業無法完成，原有資料已保留。請關閉其他 TransLamp 視窗後重試。", "言語パックの操作を完了できませんでした。既存のデータは保持されています。他の TransLamp ウィンドウを閉じて再試行してください。"),
            "checksum-mismatch" => T("Language pack integrity verification failed. Obtain the complete pack again and reimport it.", "語言包完整性驗證失敗，請重新取得完整語言包後再匯入。", "言語パックの整合性を確認できませんでした。完全なパックを再取得してインポートしてください。"),
            "pack-busy" => T("Another TransLamp window is using or changing the language pack. Try again after it finishes.", "另一個 TransLamp 視窗正在使用或管理語言包，請等候完成後重試。", "別の TransLamp ウィンドウが言語パックを使用または変更しています。完了してから再試行してください。"),
            "translation-timeout" => T("Translation took too long and was stopped. Try translating a shorter passage.", "翻譯時間過長，已停止作業。請減少文字後重試。", "翻訳に時間がかかったため停止しました。短い文章で再試行してください。"),
            "translation-failed" => T("The offline engine could not translate this text. Try a shorter passage or reinstall the complete Offline Kit.", "離線引擎無法完成翻譯。請縮短文字重試，或重新取得完整 Offline Kit。", "オフラインエンジンが翻訳を完了できませんでした。文章を短くするか、完全な Offline Kit を再インストールしてください。"),
            _ => GenericError()
        };
        return error switch
        {
            FileNotFoundException or DirectoryNotFoundException => T("Cannot complete this operation because a required file is missing. Select the language pack again or reinstall the complete Offline Kit.", "必要檔案不存在，無法完成操作。請重新選擇語言包，或重新取得完整 Offline Kit。", "必要なファイルがないため操作を完了できません。言語パックを選び直すか、完全な Offline Kit を再インストールしてください。"),
            UnauthorizedAccessException => T("Cannot access the language pack folder. Check folder permissions or use a writable data folder.", "無法存取語言包資料夾，請檢查權限或使用可寫入的資料目錄。", "言語パックのフォルダーにアクセスできません。アクセス権を確認するか、書き込み可能なデータフォルダーを使用してください。"),
            InvalidDataException or System.Text.Json.JsonException => T("The language pack cannot be read. Obtain the complete .tlpack file and import it again.", "無法讀取語言包，請重新取得完整 .tlpack 檔案後再匯入。", "言語パックを読み取れません。完全な .tlpack ファイルを取得し、再インポートしてください。"),
            IOException => T("Cannot read or save the language pack. Check available disk space and whether another application is using its files.", "無法讀取或儲存語言包，請檢查磁碟空間及檔案是否正被其他程式使用。", "言語パックを読み書きできません。ディスク空き容量と、別のアプリがファイルを使用していないか確認してください。"),
            System.Runtime.InteropServices.ExternalException => T("The clipboard is temporarily unavailable. Try copying or pasting again.", "剪貼簿暫時無法使用，請稍後重新複製或貼上。", "クリップボードを一時的に使用できません。もう一度コピーまたは貼り付けを試してください。"),
            _ => GenericError()
        };
    }

    private string GenericError() => T("Cannot complete this operation. Try again; if the problem continues, obtain a complete TransLamp Offline Kit.", "無法完成操作。請重試；若問題持續，請重新取得完整 TransLamp Offline Kit。", "操作を完了できません。再試行し、問題が続く場合は完全な TransLamp Offline Kit を再取得してください。");

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        _operation?.Cancel();
        UpdateAvailability();
        SetStatus(() => T("Cancelling…", "正在取消…", "キャンセル中…"));
    }

    private void PasteClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        try
        {
            if (!Clipboard.ContainsText())
            {
                SetStatus(() => T("The clipboard does not contain text.", "剪貼簿中沒有文字。", "クリップボードにテキストがありません。"));
                return;
            }
            SourceText.Text = Clipboard.GetText(TextDataFormat.UnicodeText);
            SourceText.Focus();
            SetStatus(() => SourceText.Text.Length > 20_000
                ? T("Text exceeds 20,000 characters. Shorten it before translating.", "文字超過 20,000 字元，請縮短後再翻譯。", "20,000 文字を超えています。短くしてから翻訳してください。")
                : T("Text pasted. Press Ctrl+Enter to translate.", "已貼上文字。按 Ctrl+Enter 翻譯。", "貼り付けました。Ctrl+Enter で翻訳できます。"));
        }
        catch (Exception error) { SetStatus(() => FriendlyError(error), true); }
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SourceText.Clear();
        InvalidateResult();
        SourceText.Focus();
        SetStatus(() => T("Source and translation cleared.", "已清除原文與譯文。", "原文と翻訳結果を消去しました。"));
    }

    private void CopyClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || TargetText.Text.Length == 0) return;
        try
        {
            Clipboard.SetText(TargetText.Text);
            SetStatus(() => T("Translation copied to clipboard.", "已複製譯文到剪貼簿。", "翻訳結果をクリップボードにコピーしました。"));
        }
        catch (Exception error) { SetStatus(() => FriendlyError(error), true); }
    }

    private async void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await TranslateAsync();
        }
        else if (e.Key == Key.Escape && IsBusy)
        {
            e.Handled = true;
            CancelClick(this, new RoutedEventArgs());
        }
    }

    private void SetStatus(Func<string> status, bool error = false)
    {
        if (_closed) return;
        _status = status;
        _statusIsError = error;
        RenderStatus();
    }

    private void RenderStatus()
    {
        StatusText.Text = _status?.Invoke() ?? "";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, _statusIsError ? "ErrorBrush" : "MutedBrush");
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _operation?.Cancel();
        base.OnClosed(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_initialized && !_closed && !IsBusy) RefreshPacks();
    }

    private void PacksExpanded(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_closed && !IsBusy) RefreshPacks();
    }

    private sealed record LanguageOption(string Code, string Label);
    private sealed record PackRow(string Id, string Title, string Details, string Source, bool CanRemove);
}
