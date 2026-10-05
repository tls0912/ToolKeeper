using System.Windows;
using TransLamp.Core;

namespace TransLamp;

public partial class MainWindow
{
    private InstalledLanguagePack? InstalledPack(DownloadableLanguagePack step) => _packs.FirstOrDefault(pack =>
        pack.Manifest.SourceLanguage == step.SourceLanguage && pack.Manifest.TargetLanguage == step.TargetLanguage);

    private IReadOnlyList<DownloadableLanguagePack> MissingPacks(TranslationRoute? route) =>
        route?.Steps.Where(step => InstalledPack(step) is null).ToArray() ?? [];

    private static string MissingPromptKey(TranslationRoute route, IReadOnlyList<DownloadableLanguagePack> missing) =>
        string.Join(",", route.Steps.Select(step => step.Id)) + ":" + string.Join(",", missing.Select(step => step.Id));

    private string RouteDescription(TranslationRoute route)
    {
        var path = string.Join(" → ", new[] { route.Steps[0].SourceLanguage }.Concat(route.Steps.Select(step => step.TargetLanguage)).Select(LanguageName));
        return route.ViaEnglish ? T($"Via English: {path}", $"透過英文中轉：{path}", $"英語経由：{path}") : path;
    }

    private async Task OfferMissingPacksAsync(TranslationRoute route, bool automatic)
    {
        var missing = MissingPacks(route);
        if (missing.Count == 0 || _closed || IsBusy) return;
        var key = MissingPromptKey(route, missing);
        if (automatic && (TranslationPage.Visibility != Visibility.Visible || key == _lastMissingPackPrompt)) return;
        var source = SourceCode;
        var target = TargetCode;
        _lastMissingPackPrompt = key;
        bool accepted;
        _promptingForPacks = true;
        UpdateAvailability();
        try
        {
            accepted = _confirmMissingPacks?.Invoke(missing) ?? ShowMissingPacksDialog(route, missing);
        }
        finally
        {
            _promptingForPacks = false;
            if (!_closed) UpdateAvailability();
        }
        if (_closed || SourceCode != source || TargetCode != target) return;
        if (!accepted)
        {
            SetStatus(() => T("Download cancelled. Choose Translate to ask again, or install the missing packs in Data management.",
                "暫不下載。可按「立即翻譯」再次確認，或到「資料管理」安裝缺少的語言包。",
                "ダウンロードを見送りました。「今すぐ翻訳」で再確認するか、「データ管理」から不足するパックをインストールできます。"));
            return;
        }
        // The user's confirmation covers the displayed directed packs, even if
        // another window installed one while the confirmation dialog was open.
        await DownloadPacksAsync(missing);
    }

    private bool ShowMissingPacksDialog(TranslationRoute route, IReadOnlyList<DownloadableLanguagePack> missing)
    {
        var names = string.Join("\n", missing.Select(pack => $"• {LanguageName(pack.SourceLanguage)} → {LanguageName(pack.TargetLanguage)} ({FormatSize(pack.SizeBytes)})"));
        var size = FormatSize(missing.Sum(pack => pack.SizeBytes));
        var message = RouteDescription(route) + "\n\n" + T(
            $"The following language packs are missing:\n{names}\n\nDownload size: {size}. Download now? Choosing Yes opens Data management and starts the downloads.",
            $"缺少以下語言包：\n{names}\n\n下載大小合計：{size}。是否立即下載？選擇「是」將切換到資料管理並開始下載。",
            $"次の言語パックがありません：\n{names}\n\n合計ダウンロードサイズ：{size}。ダウンロードしますか？「はい」でデータ管理を開き、ダウンロードを開始します。");
        return MessageBox.Show(this, message, T("Download missing language packs", "下載缺少的語言包", "不足する言語パックをダウンロード"),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private async Task DownloadPacksAsync(IReadOnlyList<DownloadableLanguagePack> requested, bool skipInstalled = true)
    {
        if (_closed || IsBusy || !_engine.IsAvailable || !_packsReadable) return;
        _translationDelay.Stop();
        _autoTranslatePending = false;
        ShowDataManagement(true);
        await RunOperationAsync(async token =>
        {
            string? activePackId = null;
            try
            {
                for (var index = 0; index < requested.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var pack = requested[index];
                    if (skipInstalled && InstalledPack(pack) is not null) continue;
                    var number = index + 1;
                    activePackId = pack.Id;
                    SelectDownloadPack(pack);
                    SetStatus(() => T($"Downloading {number}/{requested.Count}: {pack.DisplayName}…",
                        $"正在下載 {number}/{requested.Count}：{pack.DisplayName}…",
                        $"ダウンロード {number}/{requested.Count}：{pack.DisplayName}…"));
                    var progress = new Progress<LanguagePackDownloadProgress>(value =>
                    {
                        if (!CanUpdate(token) || activePackId != pack.Id) return;
                        var installing = value.Stage == "Installing";
                        OperationProgress.IsIndeterminate = installing;
                        OperationProgress.Maximum = 1;
                        OperationProgress.Value = value.Fraction;
                        SetStatus(() => installing
                            ? T($"Checking and installing {number}/{requested.Count}: {pack.DisplayName}…",
                                $"正在驗證並安裝 {number}/{requested.Count}：{pack.DisplayName}…",
                                $"検証・インストール {number}/{requested.Count}：{pack.DisplayName}…")
                            : T($"Downloading {number}/{requested.Count}: {pack.DisplayName} · {FormatSize(value.BytesDownloaded)} / {FormatSize(value.TotalBytes)}",
                                $"正在下載 {number}/{requested.Count}：{pack.DisplayName} · {FormatSize(value.BytesDownloaded)} / {FormatSize(value.TotalBytes)}",
                                $"ダウンロード {number}/{requested.Count}：{pack.DisplayName} · {FormatSize(value.BytesDownloaded)} / {FormatSize(value.TotalBytes)}"));
                    });
                    await _downloadPack(pack, progress, token);
                    token.ThrowIfCancellationRequested();
                    if (!CanUpdate(token)) return;
                    RefreshPacks();
                    if (InstalledPack(pack) is null)
                        throw new TransLampException("invalid-download", "The downloaded language pack was not installed.");
                }
            }
            finally { activePackId = null; }
            if (!CanUpdate(token)) return;
            RefreshPacks();
            QueueTranslation(updateStatus: false);
            SetStatus(() => T("Language packs installed. The original text will translate automatically when its direction is ready.",
                "語言包已安裝。所選方向就緒後會自動翻譯原文。",
                "言語パックをインストールしました。選択した方向の準備ができると原文を自動翻訳します。"));
        });
    }

    private void SelectDownloadPack(DownloadableLanguagePack pack)
    {
        _updatingDownloads = true;
        try { DownloadSourceLanguage.SelectedValue = pack.SourceLanguage; }
        finally { _updatingDownloads = false; }
        UpdateDownloadLanguages();
        DownloadTargetLanguage.SelectedValue = pack.TargetLanguage;
        UpdateDownloadDetails();
    }
}
