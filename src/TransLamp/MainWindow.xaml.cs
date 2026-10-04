using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ToolKeeper.UI;
using TransLamp.Core;

namespace TransLamp;

public partial class MainWindow : AppWindow
{
    private readonly LanguagePackService _packService;
    private readonly TranslationEngine _engine;
    private readonly LanguagePackDownloadService _downloads;
    private readonly Func<IReadOnlyList<DownloadableLanguagePack>, bool>? _confirmMissingPacks;
    private readonly Func<DownloadableLanguagePack, IProgress<LanguagePackDownloadProgress>?, CancellationToken, Task> _downloadPack;
    private readonly DispatcherTimer _translationDelay = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly string _bundledDirectory;
    private IReadOnlyList<InstalledLanguagePack> _packs = [];
    private IReadOnlyList<TranslationLiteralDifference> _qualityDifferences = [];
    private CancellationTokenSource? _operation;
    private bool _initialized, _closed, _updatingLanguages, _loaded;
    private bool _translationRunning, _autoTranslatePending, _composing;
    private bool _updatingDownloads;
    private bool _packsReadable = true;
    private bool _promptingForPacks;
    private string? _lastMissingPackPrompt;
    private long _inputRevision;
    private Func<string>? _status;
    private bool _statusIsError;

    public MainWindow() : this(null, null) { }

    public MainWindow(LanguagePackService? packService, TranslationEngine? engine, string? bundledDirectory = null)
        : this(packService, engine, bundledDirectory, null, null) { }

    internal MainWindow(LanguagePackService? packService, TranslationEngine? engine, string? bundledDirectory,
        Func<IReadOnlyList<DownloadableLanguagePack>, bool>? confirmMissingPacks,
        Func<DownloadableLanguagePack, IProgress<LanguagePackDownloadProgress>?, CancellationToken, Task>? downloadPack)
    {
        _engine = engine ?? new TranslationEngine();
        _packService = packService ?? new LanguagePackService(validator: _engine.ValidatePackAsync);
        _downloads = new LanguagePackDownloadService(_packService);
        _confirmMissingPacks = confirmMissingPacks;
        _downloadPack = downloadPack ?? (async (pack, progress, token) => { await _downloads.DownloadAndInstallAsync(pack, progress, token); });
        _bundledDirectory = bundledDirectory ?? Path.Combine(AppContext.BaseDirectory, "LanguagePacks");
        InitializeComponent();
        _translationDelay.Tick += AutoTranslateTick;
        SourceText.AddHandler(TextCompositionManager.PreviewTextInputStartEvent, new TextCompositionEventHandler(CompositionStarted), true);
        SourceText.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent, new TextCompositionEventHandler(CompositionStarted), true);
        SourceText.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(CompositionFinished), true);
        SourceText.LostKeyboardFocus += (_, _) => { if (_composing) { _composing = false; QueueTranslation(); } };
        PreferencesPath = Path.Combine(Path.GetDirectoryName(_packService.RootDirectory)!, "ui.json");
        AboutAuthor = "不告訴你";
        UiPreferencesChanged += (_, _) => UpdateLabels();
        _initialized = true;
        UpdateLabels();
        RefreshPacks();
        ShowDataManagement(false);
        SetStatus(() => T("Ready. Type or paste text to translate automatically.", "就緒。輸入或貼上文字後自動翻譯。", "準備完了。テキストを入力・貼り付けると自動翻訳します。"));
    }

    private string SourceCode => SourceLanguage.SelectedValue as string ?? "en";
    private string TargetCode => TargetLanguage.SelectedValue as string ?? "zh";
    private bool IsBusy => _operation is not null || _promptingForPacks;
    private TranslationRoute? CurrentRoute => TranslationRoute.Resolve(SourceCode, TargetCode);
    private bool IsSupportedDirection => CurrentRoute is not null;

    private void UpdateLabels()
    {
        if (!_initialized || _closed) return;
        foreach (var (key, value) in Strings.ForLanguage(ResolvedLanguage)) Resources[key] = value;
        var source = SourceCode;
        var target = TargetCode;
        _updatingLanguages = true;
        try
        {
            var languages = LanguagePackCatalog.Packs.SelectMany(pack => new[] { pack.SourceLanguage, pack.TargetLanguage })
                .Distinct().Select(code => new LanguageOption(code, LanguageName(code))).ToArray();
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
        if (!_closed) { QueueTranslation(); SourceText.Focus(); }
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
        _lastMissingPackPrompt = null;
        InvalidateResult();
        UpdateAvailability();
        QueueTranslation();
    }

    private void SwapClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy && !_translationRunning) return;
        var source = SourceCode;
        var target = TargetCode;
        _updatingLanguages = true;
        try { SourceLanguage.SelectedValue = target; TargetLanguage.SelectedValue = source; }
        finally { _updatingLanguages = false; }
        _lastMissingPackPrompt = null;
        // Direction-only swap preserves the user's original text and never silently substitutes a translation.
        InvalidateResult();
        UpdateAvailability();
        SetStatus(() => T("Translation direction swapped; source text kept.", "已交換翻譯方向，保留原文。", "翻訳方向を交換しました。原文は保持されています。"));
        QueueTranslation(updateStatus: false);
    }

    private void SourceTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _closed) return;
        InvalidateResult();
        UpdateCounts();
        UpdateAvailability();
        QueueTranslation();
    }

    private void TargetTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized && !_closed) UpdateCounts();
    }

    private void InvalidateResult()
    {
        _inputRevision++;
        _translationDelay.Stop();
        _autoTranslatePending = false;
        if (_translationRunning) _operation?.Cancel();
        if (TargetText.Text.Length != 0) TargetText.Clear();
        ClearQualityWarning();
        CopyButton.IsEnabled = false;
    }

    private void CompositionStarted(object sender, TextCompositionEventArgs e)
    {
        _composing = true;
        _translationDelay.Stop();
        InvalidateResult();
    }

    private void CompositionFinished(object sender, TextCompositionEventArgs e)
    {
        _composing = false;
        QueueTranslation();
    }

    private void QueueTranslation(bool updateStatus = true)
    {
        _translationDelay.Stop();
        _autoTranslatePending = false;
        if (!_initialized || _closed) return;
        var empty = string.IsNullOrWhiteSpace(SourceText.Text);
        var tooLong = SourceText.Text.Length > TranslationEngine.MaximumInputCharacters;
        var route = CurrentRoute;
        var missing = MissingPacks(route);
        var promptNeeded = route is not null && missing.Count > 0 && TranslationPage.Visibility == Visibility.Visible
            && MissingPromptKey(route, missing) != _lastMissingPackPrompt;
        _autoTranslatePending = !tooLong && route is not null && _engine.IsAvailable && _packsReadable
            && (promptNeeded || (!empty && missing.Count == 0));
        if (updateStatus && (!IsBusy || _translationRunning))
            SetStatus(() => empty
                ? T("Enter some text to translate.", "請先輸入要翻譯的文字。", "翻訳するテキストを入力してください。")
                : tooLong
                    ? T("Text exceeds 20,000 characters. Shorten it before translating.", "文字超過 20,000 字元，請縮短後再翻譯。", "20,000 文字を超えています。短くしてから翻訳してください。")
                    : _autoTranslatePending
                        ? T("Waiting for typing to pause…", "等待輸入完成後自動翻譯…", "入力が止まるのを待っています…")
                        : T("Source text changed. Check the translation direction and installed language packs above.", "原文已變更，請確認上方翻譯方向與語言包狀態。", "原文が変更されました。翻訳方向と言語パックの状態を確認してください。"));
        if (_autoTranslatePending && !_composing) _translationDelay.Start();
        UpdateAvailability();
    }

    private async void AutoTranslateTick(object? sender, EventArgs e)
    {
        _translationDelay.Stop();
        if (_closed || _composing || !_autoTranslatePending || IsBusy) return;
        await TranslateAsync(automatic: true);
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
        try { _packs = _packService.GetInstalledPacks(); _packsReadable = true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TransLampException or System.Text.Json.JsonException)
        {
            _packs = [];
            _packsReadable = false;
            SetStatus(() => FriendlyError(error), true);
        }
        UpdatePackList();
        UpdateAvailability();
    }

    private void UpdatePackList()
    {
        UpdateDownloadLanguages();
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
        OpenPacksFolderButton.ToolTip = _packService.RootDirectory;
    }

    private void UpdateDownloadLanguages()
    {
        var source = DownloadSourceLanguage.SelectedValue as string ?? "en";
        var target = DownloadTargetLanguage.SelectedValue as string ?? "zh";
        _updatingDownloads = true;
        try
        {
            var sources = LanguagePackCatalog.Packs.Select(pack => pack.SourceLanguage).Distinct()
                .Select(code => new LanguageOption(code, LanguageName(code))).ToArray();
            DownloadSourceLanguage.ItemsSource = sources;
            DownloadSourceLanguage.SelectedValue = sources.Any(option => option.Code == source) ? source : sources.FirstOrDefault()?.Code;
            var targets = LanguagePackCatalog.Packs.Where(pack => pack.SourceLanguage == DownloadSourceLanguage.SelectedValue as string)
                .Select(pack => new LanguageOption(pack.TargetLanguage, LanguageName(pack.TargetLanguage))).ToArray();
            DownloadTargetLanguage.ItemsSource = targets;
            DownloadTargetLanguage.SelectedValue = targets.Any(option => option.Code == target) ? target : targets.FirstOrDefault()?.Code;
        }
        finally { _updatingDownloads = false; }
        UpdateDownloadDetails();
    }

    private void DownloadSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized && !_closed && !_updatingDownloads) UpdateDownloadLanguages();
    }

    private void DownloadTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized && !_closed && !_updatingDownloads) UpdateDownloadDetails();
    }

    private void UpdateDownloadDetails()
    {
        var languages = LanguagePackCatalog.Packs.SelectMany(pack => new[] { pack.SourceLanguage, pack.TargetLanguage }).Distinct().Count();
        var directions = LanguagePackCatalog.Packs.Count;
        DownloadCatalogSummary.Text = T($"{languages} languages · {directions} translation directions. Choose a source and target below.",
            $"{languages} 種語言 · {directions} 個翻譯方向。請從下拉選單選擇來源與目標語言。",
            $"{languages} 言語 · {directions} 翻訳方向。翻訳元と翻訳先を選択してください。");
        var pack = LanguagePackCatalog.Packs.FirstOrDefault(pack => pack.SourceLanguage == DownloadSourceLanguage.SelectedValue as string
            && pack.TargetLanguage == DownloadTargetLanguage.SelectedValue as string);
        var installed = pack is not null && _packs.Any(p => p.Manifest.Id == pack.Id && p.Manifest.ModelVersion == pack.Version);
        DownloadPackTitle.Text = pack is null ? "" : $"{LanguageName(pack.SourceLanguage)} → {LanguageName(pack.TargetLanguage)}";
        DownloadPackDetails.Text = pack is null ? "" : T($"Version {pack.Version} · {FormatSize(pack.SizeBytes)} · License: {pack.License}",
            $"版本 {pack.Version} · {FormatSize(pack.SizeBytes)} · 授權：{pack.License}",
            $"バージョン {pack.Version} · {FormatSize(pack.SizeBytes)} · ライセンス：{pack.License}");
        DownloadPackSource.Text = pack is null ? "" : T($"Source: {pack.Source}", $"來源：{pack.Source}", $"提供元：{pack.Source}");
        DownloadButton.Content = installed ? T("Installed", "已安裝", "インストール済み") : T("Download", "下載", "ダウンロード");
        DownloadButton.Tag = pack?.Id;
        DownloadButton.IsEnabled = pack is not null && !IsBusy && !installed && _engine.IsAvailable && _packsReadable;
        DownloadSourceLanguage.IsEnabled = DownloadTargetLanguage.IsEnabled = !IsBusy;
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024d * 1024):N1} MB" : $"{bytes / 1024d:N1} KB";

    private string LanguageName(string code) => code switch
    {
        "en" => "English",
        "zh" => T("Chinese (Simplified)", "中文（簡體）", "中国語（簡体字）"),
        "zt" => T("Traditional Chinese", "繁體中文", "繁体字中国語"),
        "ja" => T("Japanese", "日本語", "日本語"),
        "ko" => T("Korean", "韓文（한국어）", "韓国語"),
        "fr" => T("French", "法文", "フランス語"),
        "pt" => T("Portuguese", "葡萄牙文", "ポルトガル語"),
        "es" => T("Spanish", "西班牙文", "スペイン語"),
        "de" => T("German", "德文", "ドイツ語"),
        "it" => T("Italian", "義大利文", "イタリア語"),
        "ru" => T("Russian", "俄文", "ロシア語"),
        "ar" => T("Arabic", "阿拉伯文", "アラビア語"),
        "hi" => T("Hindi", "印地文", "ヒンディー語"),
        "th" => T("Thai", "泰文", "タイ語"),
        "vi" => T("Vietnamese", "越南文", "ベトナム語"),
        _ => code
    };

    private void UpdateAvailability()
    {
        var route = CurrentRoute;
        var missing = MissingPacks(route);
        DataRuntimeHint.Text = _engine.IsAvailable ? "" : T("The offline engine is missing. Use a complete TransLamp Offline Kit to download and validate language packs.",
            "缺少離線翻譯引擎，請使用完整 TransLamp Offline Kit，才能下載並驗證語言包。",
            "オフラインエンジンがありません。言語パックをダウンロード・検証するには完全な TransLamp Offline Kit が必要です。");
        DataRuntimeHint.Visibility = _engine.IsAvailable ? Visibility.Collapsed : Visibility.Visible;
        AvailabilityText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        if (SourceCode == TargetCode)
            AvailabilityText.Text = T("Choose different source and target languages.", "請選擇不同的來源與目標語言。", "翻訳元と翻訳先には異なる言語を選択してください。");
        else if (!IsSupportedDirection)
            AvailabilityText.Text = T("This direction is not available. Check Data management for downloadable directions.", "尚未提供此翻譯方向，請到「資料管理」查看可下載的方向。", "この翻訳方向は未対応です。「データ管理」で対応する方向を確認してください。");
        else if (!_engine.IsAvailable)
            AvailabilityText.Text = T("Offline runtime not found. Use a complete TransLamp Offline Kit; importing a language pack alone is not enough.", "找不到離線翻譯引擎。請使用完整 TransLamp Offline Kit，僅匯入語言包仍無法翻譯。", "オフラインエンジンが見つかりません。言語パックだけでなく、完全な TransLamp Offline Kit が必要です。");
        else if (!_packsReadable)
            AvailabilityText.Text = T("Language packs could not be read. Wait for other pack operations to finish and retry.",
                "目前無法讀取語言包，請等候其他語言包作業完成後重試。", "言語パックを読み取れません。他のパック操作が完了してから再試行してください。");
        else if (missing.Count > 0)
            AvailabilityText.Text = RouteDescription(route!) + " · " + T(
                $"Missing {missing.Count} language pack(s). Confirm the download to prepare this direction.",
                $"缺少 {missing.Count} 個語言包，確認下載後即可準備此方向。",
                $"言語パックが {missing.Count} 個ありません。ダウンロードを確認すると準備できます。");
        else
        {
            AvailabilityText.Text = T("Offline ready", "離線就緒", "オフライン準備完了") + " · " + RouteDescription(route!) + " · CPU";
            AvailabilityText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        }
        TranslateButton.IsEnabled = !IsBusy && IsSupportedDirection && _engine.IsAvailable && _packsReadable
            && !string.IsNullOrWhiteSpace(SourceText.Text) && SourceText.Text.Length <= 20_000;
        PasteButton.IsEnabled = ClearButton.IsEnabled = SwapButton.IsEnabled = !IsBusy || _translationRunning;
        SourceLanguage.IsEnabled = TargetLanguage.IsEnabled = !IsBusy || _translationRunning;
        ImportButton.IsEnabled = !IsBusy;
        OpenPacksFolderButton.IsEnabled = !IsBusy;
        SourceText.IsReadOnly = IsBusy && !_translationRunning;
        CancelButton.IsEnabled = _autoTranslatePending || (IsBusy && _operation?.IsCancellationRequested == false);
        CopyButton.IsEnabled = !IsBusy && TargetText.Text.Length > 0;
    }

    private async void TranslateClick(object sender, RoutedEventArgs e) => await TranslateAsync();

    public Task TranslateAsync() => TranslateAsync(automatic: false);

    private async Task TranslateAsync(bool automatic)
    {
        if (_closed || IsBusy) return;
        if (_composing) return;
        _translationDelay.Stop();
        _autoTranslatePending = false;
        UpdateAvailability();
        var route = CurrentRoute;
        if (route is null || !_engine.IsAvailable || !_packsReadable) return;
        if (MissingPacks(route).Count > 0)
        {
            await OfferMissingPacksAsync(route, automatic);
            return;
        }
        if (!TranslateButton.IsEnabled) return;
        var packs = route.Steps.Select(step => InstalledPack(step)!).ToArray();
        var text = SourceText.Text;
        var revision = _inputRevision;
        ClearQualityWarning();
        TargetText.Clear();
        _translationRunning = true;
        try { await RunOperationAsync(async token =>
        {
            SetStatus(() => T("Translating locally… The first translation may take longer while the model loads.", "正在本機翻譯…首次翻譯需載入模型，可能稍久。", "ローカルで翻訳中…初回はモデルの読み込みに時間がかかることがあります。"));
            var progress = new Progress<TranslationRouteProgress>(value =>
            {
                if (!CanUpdate(token) || revision != _inputRevision) return;
                OperationProgress.IsIndeterminate = false;
                OperationProgress.Maximum = value.Stages;
                OperationProgress.Value = value.Stage - 1 + (double)value.Segment.Completed / Math.Max(1, value.Segment.Total);
                SetStatus(() => T($"Translating locally · step {value.Stage}/{value.Stages} · {value.Segment.Completed}/{value.Segment.Total} segments",
                    $"正在本機翻譯 · 第 {value.Stage}/{value.Stages} 步 · {value.Segment.Completed}/{value.Segment.Total} 段",
                    $"ローカルで翻訳中 · ステップ {value.Stage}/{value.Stages} · {value.Segment.Completed}/{value.Segment.Total} セグメント"));
            });
            var result = await _engine.TranslateRouteAsync(text, packs, progress, token);
            if (!CanUpdate(token) || revision != _inputRevision) return;
            ApplyTranslationResult(text, result);
            if (route.ViaEnglish)
                SetStatus(() => T("Translation complete via English. Check the result against the original text.",
                    "已透過英文完成翻譯，請對照原文確認結果。", "英語を経由して翻訳しました。原文と照合してください。"));
        }); }
        finally { _translationRunning = false; }
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
            ShowDataManagement(true);
            QueueTranslation(updateStatus: false);
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
        var revision = _inputRevision;
        _operation = operation;
        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.IsIndeterminate = true;
        UpdateAvailability();
        UpdatePackList();
        try
        {
            await work(operation.Token);
            if (!_closed && operation.IsCancellationRequested && (!_translationRunning || revision == _inputRevision)) SetCancelledStatus();
        }
        catch (OperationCanceledException)
        {
            if (!_closed && (!_translationRunning || revision == _inputRevision)) SetCancelledStatus();
        }
        catch (Exception error)
        {
            if (!_closed && (!_translationRunning || revision == _inputRevision)) SetStatus(() => FriendlyError(error), true);
        }
        finally
        {
            if (ReferenceEquals(_operation, operation)) _operation = null;
            if (!_closed)
            {
                OperationProgress.Visibility = Visibility.Collapsed;
                RefreshPacks();
                UpdateCounts();
                if (_autoTranslatePending && !_composing) _translationDelay.Start();
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
            "intermediate-too-long" => T("The English intermediate translation is too long. Shorten the original text and retry.", "英文中轉結果過長，請縮短原文後重試。", "中間の英語訳が長すぎます。原文を短くして再試行してください。"),
            "runtime-missing" or "runtime-start" => T("The offline translation engine is missing or cannot start. Use a complete TransLamp Offline Kit.", "離線翻譯引擎缺漏或無法啟動，請使用完整 TransLamp Offline Kit。", "オフライン翻訳エンジンがないか、起動できません。完全な TransLamp Offline Kit を使用してください。"),
            "invalid-pack" => T("The language pack is damaged or has an unsupported format. Get a complete .tlpack file and import it again.", "語言包損壞或格式不相容，請重新取得完整 .tlpack 檔案後再匯入。", "言語パックが破損しているか、未対応の形式です。完全な .tlpack ファイルを取得し、再インポートしてください。"),
            "incompatible-runtime" => T("This language pack requires a different engine version. Use a compatible pack or update the Offline Kit.", "此語言包需要不同版本的引擎，請使用相容語言包或更新 Offline Kit。", "この言語パックには別のエンジンバージョンが必要です。互換性のあるパックを使用するか、Offline Kit を更新してください。"),
            "unsupported-direction" => T("This direction is not supported. Choose a direction listed in Data management.", "尚未支援此翻譯方向，請選擇「資料管理」中列出的方向。", "この翻訳方向は未対応です。「データ管理」にある方向を選択してください。"),
            "download-failed" => T("Download failed. Check your connection and try again. Installed packs remain available offline.", "下載失敗，請檢查網路後重試。已安裝的語言包仍可離線使用。", "ダウンロードに失敗しました。ネット接続を確認して再試行してください。インストール済みパックはオフラインで使用できます。"),
            "download-timeout" => T("Download timed out. Check your connection and retry.", "下載逾時，請檢查網路後重試。", "ダウンロードがタイムアウトしました。ネット接続を確認して再試行してください。"),
            "invalid-download" => T("The downloaded pack failed verification. Existing language packs were kept; try downloading again.", "下載的語言包未通過驗證，原有語言包已保留，請重新下載。", "ダウンロードしたパックの検証に失敗しました。既存パックは保持されています。もう一度ダウンロードしてください。"),
            "unsupported-download" => T("This pack is not in the available download list.", "此語言包不在可下載清單中。", "このパックはダウンロード一覧にありません。"),
            "license-missing" => T("Required license files are missing. Use a complete TransLamp Offline Kit.", "缺少必要授權檔案，請使用完整 TransLamp Offline Kit。", "必要なライセンスファイルがありません。完全な TransLamp Offline Kit を使用してください。"),
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
        _translationDelay.Stop();
        _autoTranslatePending = false;
        _operation?.Cancel();
        UpdateAvailability();
        if (IsBusy) SetStatus(() => T("Cancelling…", "正在取消…", "キャンセル中…"));
        else SetCancelledStatus();
    }

    private void PasteClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy && !_translationRunning) return;
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
                : T("Text pasted. Translation starts automatically when the selected direction is ready.", "已貼上文字，所選方向就緒後會自動翻譯。", "貼り付けました。選択した方向の準備ができると自動翻訳します。"));
        }
        catch (Exception error) { SetStatus(() => FriendlyError(error), true); }
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy && !_translationRunning) return;
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
        else if (e.Key == Key.Escape && (IsBusy || _autoTranslatePending))
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
        _translationDelay.Stop();
        _autoTranslatePending = false;
        _operation?.Cancel();
        base.OnClosed(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_initialized && !_closed && !IsBusy) RefreshPacks();
    }

    private void TranslationTabClick(object sender, RoutedEventArgs e) => ShowDataManagement(false);
    private void DataTabClick(object sender, RoutedEventArgs e) => ShowDataManagement(true);

    private void OpenPacksFolderClick(object sender, RoutedEventArgs e)
    {
        if (_closed || IsBusy) return;
        try
        {
            Directory.CreateDirectory(_packService.RootDirectory);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = _packService.RootDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            SetStatus(() => T("Could not open the language pack folder. Check folder permissions and try again.",
                "無法開啟語言檔資料夾，請確認資料夾存取權限後重試。",
                "言語フォルダーを開けませんでした。フォルダーへのアクセス権限を確認して、もう一度お試しください。"), true);
        }
    }

    private void ShowDataManagement(bool show)
    {
        TranslationPage.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        DataPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TranslationTab.SetResourceReference(BackgroundProperty, show ? "SurfaceBrush" : "HoverBrush");
        TranslationTab.SetResourceReference(ForegroundProperty, show ? "TextBrush" : "AccentBrush");
        DataTab.SetResourceReference(BackgroundProperty, show ? "HoverBrush" : "SurfaceBrush");
        DataTab.SetResourceReference(ForegroundProperty, show ? "AccentBrush" : "TextBrush");
        TranslationTab.FontWeight = show ? FontWeights.Normal : FontWeights.SemiBold;
        DataTab.FontWeight = show ? FontWeights.SemiBold : FontWeights.Normal;
        if (_initialized && !_closed && !IsBusy) RefreshPacks();
    }

    private async void DownloadPackClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) await DownloadPackAsync(id);
    }

    public async Task DownloadPackAsync(string id)
    {
        var pack = LanguagePackCatalog.Packs.FirstOrDefault(p => p.Id == id);
        if (pack is not null) await DownloadPacksAsync([pack], skipInstalled: false);
    }

    private sealed record LanguageOption(string Code, string Label);
    private sealed record PackRow(string Id, string Title, string Details, string Source, bool CanRemove);
}
