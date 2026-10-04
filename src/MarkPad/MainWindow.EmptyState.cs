using System.IO;
using System.Windows;
using MarkPad.Models;
using MarkPad.Rendering;
using MarkPad.Services;

namespace MarkPad;

public partial class MainWindow
{
    // Reading surfaces only: these articles never enter document, recent-file or recovery state.
    private readonly PreviewPane _emptyPreview;
    private readonly Dictionary<(int Article, string Language), string> _emptyArticles = [];
    private bool _deferEmptyState;
    private bool _hasSelectedEmptyArticle;
    private int _emptyArticleIndex = 1;

    internal void CompleteStartup()
    {
        _deferEmptyState = false;
        _ = GuardAsync(RenderEmptyStateAsync);
    }

    private string EmptyArticle()
    {
        var language = UiLanguage;
        var key = (_emptyArticleIndex, language);
        if (_emptyArticles.TryGetValue(key, out var article)) return article;
        var resource = _emptyArticleIndex switch { 1 => "Introduction", 2 => "Origin", _ => "Welcome" };
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream($"MarkPad.Resources.{resource}.{language}.md")
            ?? throw new InvalidOperationException("The welcome article is unavailable.");
        using var reader = new StreamReader(stream);
        return _emptyArticles[key] = reader.ReadToEnd();
    }

    private PreviewOptions EmptyPreviewOptions() => new(_dark, PreviewFontName, Settings.PreviewFontSize,
        Settings.CodeLineNumbers, Settings.EmojiShortcodes, UiLanguage, ReadOnly: true,
        DocumentTitle: _emptyArticleIndex switch
        {
            1 => T("Welcome to 汗青", "歡迎使用汗青", "汗青へようこそ"),
            2 => T("The story of 汗青", "汗青的由來", "「汗青」の由来"),
            _ => T("Quiet pages", "竹間札記", "竹のそばのメモ")
        }, Ink: IsInkTheme, RightToLeft: ToolKeeper.UI.UiLanguage.IsRightToLeft(UiLanguage));

    private void SelectEmptyArticle()
    {
        if (_hasSelectedEmptyArticle || _disposed || _deferEmptyState || _current is not null) return;
        _emptyArticleIndex = Settings.NextEmptyArticleIndex ?? 1;
        _hasSelectedEmptyArticle = true;
        Settings.NextEmptyArticleIndex = (_emptyArticleIndex + 1) % AppSettings.EmptyArticleCount;
        // Persist only on entry, not on theme/font changes or startup that opens a file.
        try { App.Preferences.Save(); } catch (Exception ex) { LocalLog.Write(ex); }
    }

    private Task RenderEmptyStateAsync()
    {
        if (_disposed || _deferEmptyState || _current is not null || !IsLoaded) return Task.CompletedTask;
        SelectEmptyArticle();
        return _emptyPreview.ShowAsync(EmptyArticle(), null, EmptyPreviewOptions());
    }

    private async void OnEmptyPreviewMessageReceived(object? sender, PreviewMessage message)
    {
        if (_disposed || _current is not null || !ReferenceEquals(sender, _emptyPreview)) return;
        await GuardAsync(async () =>
        {
            switch (message.Type)
            {
                case "ready":
                    await _emptyPreview.SetThemeAsync(_dark, IsInkTheme);
                    await _emptyPreview.SetFontSizeAsync(Settings.PreviewFontSize);
                    break;
                case "copy": case "copy-markdown": case "copy-link":
                    if (message.Text is not null) Clipboard.SetText(message.Text);
                    break;
                case "link":
                    if (message.Text is { } link)
                    {
                        if (link.StartsWith('#')) await _emptyPreview.GoToAnchorAsync(link);
                        else await OpenLinkAsync(link);
                    }
                    break;
                case "shortcut":
                    if (message.Text is not null) await ShortcutAsync(message.Text);
                    break;
                case "error":
                    if (message.Text is not null) LocalLog.Write(new InvalidOperationException(message.Text));
                    break;
            }
        });
    }
}
