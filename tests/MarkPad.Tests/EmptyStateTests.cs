using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AngleSharp.Html.Parser;
using MarkPad.Models;
using MarkPad.Rendering;
using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class EmptyStateTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("ko")]
    public Task AdditionalLanguagesHaveThreeReadonlyArticlesAndPersistAcrossSwitches(string language) => WithWindow(window =>
    {
        var settings = App.Preferences.Settings;
        settings.Language = language;
        window.ApplyPreferences();
        Assert.Equal(FlowDirection.LeftToRight, window.FlowDirection);
        var menu = new ContextMenu();
        Invoke<object?>(window, "AddLanguageChoices", menu.Items);
        Assert.Equal(9, menu.Items.Count);
        Assert.Single(menu.Items.OfType<MenuItem>(), item => item.IsChecked);
        var expectedTitles = new[]
        {
            window.T("Quiet pages", "竹間札記", "竹のそばのメモ"),
            window.T("Welcome to 汗青", "歡迎使用汗青", "汗青へようこそ"),
            window.T("The story of 汗青", "汗青的由來", "「汗青」の由来")
        };
        foreach (var index in new[] { 1, 2, 0 })
        {
            settings.NextEmptyArticleIndex = index;
            SetField(window, "_hasSelectedEmptyArticle", false);
            Invoke<object?>(window, "SelectEmptyArticle");
            var article = Invoke<string>(window, "EmptyArticle");
            var options = Invoke<PreviewOptions>(window, "EmptyPreviewOptions");
            Assert.StartsWith("# " + expectedTitles[index], article.TrimStart('\uFEFF'));
            Assert.Equal(expectedTitles[index], options.DocumentTitle);
            Assert.Equal(language, options.Language);
            Assert.True(options.ReadOnly);
            Assert.Equal(language == "ar", options.RightToLeft);
            var page = new HtmlParser().ParseDocument(new MarkdownRenderer().Render(article, options));
            Assert.Equal(language == "ar" ? "rtl" : "ltr", page.QuerySelector("#document")!.GetAttribute("dir"));
            Assert.Equal(expectedTitles[index], page.QuerySelector("h1")!.TextContent);
            if (index == 0)
            {
                Assert.Equal(3, page.QuerySelectorAll("input[type=checkbox]").Length);
                Assert.All(page.QuerySelectorAll("input[type=checkbox]"), input => Assert.True(input.HasAttribute("disabled")));
                for (var level = 1; level <= 6; level++) Assert.NotNull(page.QuerySelector("h" + level));
            }
            if (index == 2) Assert.Contains("汗青", page.QuerySelector("blockquote")!.TextContent);
            settings.Language = "en";
            window.ApplyPreferences();
            settings.Language = language;
            window.ApplyPreferences();
            Assert.Equal(article, Invoke<string>(window, "EmptyArticle"));
            Assert.Equal((index + 1) % 3, settings.NextEmptyArticleIndex);
        }
        Assert.Equal(language, new SettingsService(App.Preferences.DataDirectory).Settings.Language);
        Assert.Empty(window.Documents);
        Assert.Empty(settings.RecentFiles);
        Assert.Empty(settings.SessionFiles);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmptyArticlesAlternateOnlyWhenEnteringAnEmptyWindow() => WithWindow(async window =>
    {
        Invoke<object?>(window, "SelectEmptyArticle");
        Assert.Contains("# 歡迎使用汗青", Invoke<string>(window, "EmptyArticle"));
        Assert.Equal(2, new SettingsService(App.Preferences.DataDirectory).Settings.NextEmptyArticleIndex);

        App.Preferences.Settings.Theme = "InkDark";
        window.ApplyPreferences();
        Invoke<object?>(window, "SelectEmptyArticle");
        Assert.Contains("# 歡迎使用汗青", Invoke<string>(window, "EmptyArticle"));
        Assert.Equal(2, App.Preferences.Settings.NextEmptyArticleIndex);

        foreach (var expected in new[] { "# 汗青的由來", "# 竹間札記", "# 歡迎使用汗青" })
        {
            var document = new DocumentTab { Content = "# User document", IsDirty = false };
            var view = Invoke<DocumentView>(window, "GetDocumentView", document);
            SetField(view.Preview, "_initializeTask", Task.FromResult(false));
            window.AddDocument(document);
            var next = App.Preferences.Settings.NextEmptyArticleIndex;
            Invoke<object?>(window, "SelectEmptyArticle");
            Assert.Equal(next, App.Preferences.Settings.NextEmptyArticleIndex);
            await Invoke<Task>(window, "CloseDocumentAsync", document);
            Invoke<object?>(window, "SelectEmptyArticle");
            Assert.Contains(expected, Invoke<string>(window, "EmptyArticle"));
            Assert.Equal(App.Preferences.Settings.NextEmptyArticleIndex,
                new SettingsService(App.Preferences.DataDirectory).Settings.NextEmptyArticleIndex);
        }
        Assert.Empty(window.Documents);
        Assert.Empty(App.Preferences.Settings.RecentFiles);
        Assert.Empty(App.Preferences.Settings.SessionFiles);
        await App.Recovery.SaveAsync(window.Documents);
        Assert.Empty(await App.Recovery.RecoverAsync());
    });

    [Fact]
    public Task NextWindowContinuesWithThePersistedOriginArticleInEveryLanguage() => WithWindow(window =>
    {
        Invoke<object?>(window, "SelectEmptyArticle");
        var preferences = new SettingsService(App.Preferences.DataDirectory);
        typeof(App).GetProperty(nameof(App.Preferences))!.SetValue(null, preferences);
        var nextWindow = new MainWindow();
        try
        {
            Invoke<object?>(nextWindow, "SelectEmptyArticle");
            foreach (var (language, title) in new[] { ("zh-TW", "汗青的由來"), ("en", "The story of 汗青"), ("ja", "「汗青」の由来") })
            {
                preferences.Settings.Language = language;
                nextWindow.ApplyPreferences();
                Invoke<object?>(nextWindow, "SelectEmptyArticle");
                var article = Invoke<string>(nextWindow, "EmptyArticle");
                var options = Invoke<PreviewOptions>(nextWindow, "EmptyPreviewOptions");
                Assert.StartsWith("# " + title, article.TrimStart('\uFEFF'));
                Assert.Equal(title, options.DocumentTitle);
                Assert.Equal(language, options.Language);
                Assert.True(options.ReadOnly);
                var rendered = new HtmlParser().ParseDocument(new MarkdownRenderer().Render(article, options));
                Assert.Equal(title, rendered.QuerySelector("#document h1")!.TextContent);
                Assert.All(rendered.QuerySelectorAll("input[type='checkbox']"), checkbox => Assert.True(checkbox.HasAttribute("disabled")));
            }
            Assert.Equal(0, preferences.Settings.NextEmptyArticleIndex);
            Assert.Empty(nextWindow.Documents);
            Assert.Empty(preferences.Settings.RecentFiles);
        }
        finally
        {
            SetField(nextWindow, "_allowClose", true);
            nextWindow.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DeferredStartupDoesNotConsumeTheNextEmptyArticle() => WithWindow(window =>
    {
        App.Preferences.Settings.NextEmptyArticleIndex = 2;
        SetField(window, "_deferEmptyState", true);
        Invoke<object?>(window, "SelectEmptyArticle");
        Assert.Equal(2, App.Preferences.Settings.NextEmptyArticleIndex);
        Assert.False(GetField<bool>(window, "_hasSelectedEmptyArticle"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task IntroductionAndOriginKeepLocalizedReadonlyContentOnPreferenceChanges(int articleIndex) => WithWindow(window =>
    {
        var settings = App.Preferences.Settings;
        settings.NextEmptyArticleIndex = articleIndex;
        Invoke<object?>(window, "SelectEmptyArticle");
        var titles = articleIndex == 1
            ? new[] { ("zh-TW", "歡迎使用汗青"), ("en", "Welcome to 汗青"), ("ja", "汗青へようこそ") }
            : new[] { ("zh-TW", "汗青的由來"), ("en", "The story of 汗青"), ("ja", "「汗青」の由来") };
        foreach (var (language, title) in titles)
        {
            settings.Language = language;
            settings.Theme = "InkDark";
            settings.PreviewFontSize = 22;
            window.ApplyPreferences();
            Invoke<object?>(window, "SelectEmptyArticle");
            var article = Invoke<string>(window, "EmptyArticle");
            var options = Invoke<PreviewOptions>(window, "EmptyPreviewOptions");
            Assert.StartsWith("# " + title, article.TrimStart('\uFEFF'));
            Assert.Equal(title, options.DocumentTitle);
            Assert.Equal(language, options.Language);
            Assert.True(options.ReadOnly);
            Assert.True(options.Dark);
            Assert.True(options.Ink);
            Assert.Equal(22, options.FontSize);
            var rendered = new HtmlParser().ParseDocument(new MarkdownRenderer().Render(article, options));
            Assert.Equal(title, rendered.QuerySelector("#document h1")!.TextContent);
            if (articleIndex == 2)
                Assert.Contains("人生自古誰無死", rendered.QuerySelector("#document blockquote")!.TextContent);
            Assert.Equal((articleIndex + 1) % 3, settings.NextEmptyArticleIndex);
        }
        Assert.Empty(window.Documents);
        Assert.Empty(settings.RecentFiles);
        Assert.Empty(settings.SessionFiles);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OutlinePreferencesReachNewAndExistingDocumentViews() => WithWindow(window =>
    {
        var headings = new[] { new PreviewHeading("h1", "First", 1, 1), new PreviewHeading("h2", "Second", 2, 2) };
        static ListBox Outline(DocumentView view) => view.Children.OfType<Grid>()
            .SelectMany(panel => panel.Children.OfType<ListBox>()).Single();
        static string[] HeadingIds(DocumentView view) => Outline(view).Items.Cast<ListBoxItem>()
            .Select(item => ((PreviewHeading)item.Tag).Id).ToArray();
        App.Preferences.Settings.PdfOutlineLevels = [2];
        var first = Invoke<DocumentView>(window, "GetDocumentView", new DocumentTab());
        Invoke<object?>(first.Preview, "PublishHeadings", (object)headings);
        Assert.Equal(new[] { "h2" }, HeadingIds(first));

        App.Preferences.Settings.PdfOutlineLevels = [1];
        window.ApplyPreferences();
        Assert.Equal(new[] { "h1" }, HeadingIds(first));
        var second = Invoke<DocumentView>(window, "GetDocumentView", new DocumentTab());
        Invoke<object?>(second.Preview, "PublishHeadings", (object)headings);
        Assert.Equal(new[] { "h1" }, HeadingIds(second));
        return Task.CompletedTask;
    });

    [Fact]
    public Task WelcomeArticleReturnsAfterClosingTheLastDocumentWithoutCreatingFileState() => WithWindow(async window =>
    {
        var emptyState = Assert.IsType<Grid>(window.FindName("EmptyState"));
        var preview = Assert.IsType<PreviewPane>(Assert.IsType<Border>(window.FindName("EmptyPreviewHost")).Child);
        var open = Assert.IsType<Button>(window.FindName("EmptyOpenButton"));
        Assert.Empty(window.Documents);
        Assert.Equal(Visibility.Visible, emptyState.Visibility);
        Assert.True(open.IsEnabled);
        Assert.Contains("# 歡迎使用汗青", Invoke<string>(window, "EmptyArticle"));

        var first = new DocumentTab { Content = "# First document", IsDirty = false };
        var second = new DocumentTab { Content = "# Second document", IsDirty = false };
        foreach (var document in new[] { first, second })
        {
            var view = Invoke<DocumentView>(window, "GetDocumentView", document);
            // Keep this lifecycle test independent of an installed WebView2 runtime.
            SetField(view.Preview, "_initializeTask", Task.FromResult(false));
            window.AddDocument(document);
            Assert.Equal(Visibility.Collapsed, emptyState.Visibility);
            Assert.Equal("汗青 - " + document.DisplayName, window.Title);
        }
        Assert.Equal(new[] { first, second }, window.Documents);

        await Invoke<Task>(window, "CloseDocumentAsync", second);
        Assert.Same(first, Assert.Single(window.Documents));
        Assert.Equal(Visibility.Collapsed, emptyState.Visibility);
        await Invoke<Task>(window, "CloseDocumentAsync", first);
        Assert.Empty(window.Documents);
        Assert.Equal(Visibility.Visible, emptyState.Visibility);
        Assert.Equal("汗青 - Markdown Writer", window.Title);
        Assert.Same(preview, Assert.IsType<Border>(window.FindName("EmptyPreviewHost")).Child);
        Assert.True(open.IsEnabled);
        Assert.Empty(App.Preferences.Settings.RecentFiles);
        await App.Recovery.SaveAsync(window.Documents);
        Assert.Empty(await App.Recovery.RecoverAsync());

        // Even unexpected browser messages cannot turn the sample into an editable document.
        Invoke<object?>(window, "OnEmptyPreviewMessageReceived", preview, new PreviewMessage("edit", Line: 1));
        Invoke<object?>(window, "OnEmptyPreviewMessageReceived", preview, new PreviewMessage("task", Line: 10, Flag: true));
        Assert.Empty(window.Documents);
        Assert.Empty(App.Preferences.Settings.RecentFiles);
    });

    [Fact]
    public Task WelcomeArticlesUseLocalizedBundledContentAndCurrentReadingPreferences() => WithWindow(window =>
    {
        App.Preferences.Settings.NextEmptyArticleIndex = 0;
        Invoke<object?>(window, "SelectEmptyArticle");
        foreach (var (language, title, ending) in new[]
        {
            ("zh-TW", "竹間札記", "把檔案存好，讓今天的想法有一個安穩的位置。"),
            ("en", "Quiet pages", "Save the file and give today's ideas a place to stay."),
            ("ja", "竹のそばのメモ", "ファイルを保存し、今日の考えに居場所を作ろう。")
        })
        {
            var settings = App.Preferences.Settings;
            settings.Language = language;
            settings.Theme = "InkDark";
            settings.PreviewFontFamily = "Georgia";
            settings.PreviewFontSize = 22;
            window.ApplyPreferences();
            var article = Invoke<string>(window, "EmptyArticle");
            var options = Invoke<PreviewOptions>(window, "EmptyPreviewOptions");
            Assert.StartsWith("# " + title, article.TrimStart('\uFEFF'));
            Assert.EndsWith(ending, article.TrimEnd());
            Assert.True(options.ReadOnly);
            Assert.True(options.Dark);
            Assert.True(options.Ink);
            Assert.Equal(language, options.Language);
            Assert.Equal(title, options.DocumentTitle);
            Assert.Equal("Georgia", options.FontFamily);
            Assert.Equal(22, options.FontSize);

            var rendered = new HtmlParser().ParseDocument(new MarkdownRenderer().Render(article, options));
            Assert.Equal(title, rendered.QuerySelector("#document h1")!.TextContent);
            Assert.Equal("ink-dark", rendered.DocumentElement.GetAttribute("data-theme"));
            var checkboxes = rendered.QuerySelectorAll("#document input[type='checkbox']");
            Assert.Equal(3, checkboxes.Length);
            Assert.All(checkboxes, checkbox => Assert.True(checkbox.HasAttribute("disabled")));
            Assert.Equal(6, rendered.QuerySelectorAll("#document h1,h2,h3,h4,h5,h6").Select(heading => heading.TagName).Distinct().Count());
        }
        Assert.Empty(window.Documents);
        Assert.Empty(App.Preferences.Settings.RecentFiles);
        return Task.CompletedTask;
    });

    [Fact]
    public Task NativeLinkEntryRejectsEncodedAbsolutePathsBeforeOpeningFiles() => WithWindow(async window =>
    {
        var path = Path.Combine(App.Preferences.DataDirectory, "private.md");
        await File.WriteAllTextAsync(path, "# Must not open through an encoded absolute link");
        var encodedFileUrl = new Uri(path).AbsoluteUri.Replace("file:", "file%3A", StringComparison.Ordinal);
        await Invoke<Task>(window, "OpenLinkAsync", encodedFileUrl);
        await Invoke<Task>(window, "OpenLinkAsync", path.Replace(":", "%3A", StringComparison.Ordinal));
        Assert.Empty(window.Documents);
        Assert.Empty(App.Preferences.Settings.RecentFiles);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SavingAChangedPathRefreshesOnlyTheCurrentDocument(bool switchTabs) => WithWindow(async window =>
    {
        App.Preferences.Settings.AutoSave = false;
        // Save normalizes this equivalent path. It exercises the actual post-save path
        // transition without opening a native Save As dialog or starting WebView2.
        var path = Path.Combine(App.Preferences.DataDirectory, ".", "saved.md");
        var tab = new DocumentTab { FilePath = path, Content = "# Saved document" };
        var view = Invoke<DocumentView>(window, "GetDocumentView", tab);
        SetField(view.Preview, "_initializeTask", Task.FromResult(false));
        window.AddDocument(tab);
        await Invoke<Task>(window, "RenderAsync", new object?[] { null });
        var previousRequest = GetField<long>(view.Preview, "_request");

        if (switchTabs)
        {
            var other = new DocumentTab { Content = "# Another document", IsDirty = false };
            var otherView = Invoke<DocumentView>(window, "GetDocumentView", other);
            SetField(otherView.Preview, "_initializeTask", Task.FromResult(false));
            window.AddDocument(other);
            await Invoke<Task>(window, "RenderAsync", new object?[] { null });
        }

        Assert.True(await Invoke<Task<bool>>(window, "SaveAsync", tab, false, false));
        Assert.Equal(Path.GetFullPath(path), tab.FilePath);
        Assert.Equal("汗青 - " + window.Documents.Last().DisplayName, window.Title);
        var currentRequest = GetField<long>(view.Preview, "_request");
        if (switchTabs) Assert.Equal(previousRequest, currentRequest);
        else
        {
            Assert.True(currentRequest > previousRequest);
            var input = GetField<(string Markdown, string? Path, PreviewOptions Options)?>(view.Preview, "_displayInput");
            Assert.Equal(tab.FilePath, input!.Value.Path);
            Assert.False(input.Value.Options.ReadOnly);

            // A later save with unchanged path and access mode keeps the retained page.
            Assert.True(await Invoke<Task<bool>>(window, "SaveAsync", tab, false, false));
            Assert.Equal(currentRequest, GetField<long>(view.Preview, "_request"));
        }
    });

    private static Task WithWindow(Func<MainWindow, Task> action) => StaTest.Run(async () =>
    {
        using var directory = new TestDirectory();
        var preferencesProperty = typeof(App).GetProperty(nameof(App.Preferences))!;
        var recoveryProperty = typeof(App).GetProperty(nameof(App.Recovery))!;
        var previousPreferences = preferencesProperty.GetValue(null);
        var previousRecovery = recoveryProperty.GetValue(null);
        var preferences = new SettingsService(directory.PathName);
        preferences.Settings.Language = "zh-TW";
        preferencesProperty.SetValue(null, preferences);
        var recoveryDirectory = directory.FilePath("recovery");
        Directory.CreateDirectory(recoveryDirectory);
        recoveryProperty.SetValue(null, new RecoveryService(recoveryDirectory));
        MainWindow? window = null;
        try
        {
            // An unshown window exercises document transitions without launching a browser.
            window = new MainWindow();
            await action(window);
        }
        finally
        {
            if (window is not null)
            {
                SetField(window, "_allowClose", true);
                window.Close();
            }
            preferencesProperty.SetValue(null, previousPreferences);
            recoveryProperty.SetValue(null, previousRecovery);
        }
    });

    private static T Invoke<T>(object target, string name, params object?[] arguments) =>
        (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static T GetField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
}
