using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolKeeper.UI;
using TransLamp.Core;
using Xunit;

namespace TransLamp.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public Task UsesSharedShellAndKeepsInputAcrossAllPreferences() => OnSta(() =>
    {
        var window = CreateWindow();
        try
        {
            Assert.IsType<WindowFrame>(window.Content);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal("Ink", window.SelectedTheme);
            Assert.Equal("TransLamp - Offline Language Translator", window.Title);
            var source = Get<TextBox>(window, "SourceText");
            var target = Get<TextBox>(window, "TargetText");
            source.Text = "Error 42\n請檢查設備";
            target.Text = "A previous result used to check state retention.";
            source.Select(2, 4);
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            foreach (var theme in new[] { "Light", "Dark", "Ink", "InkDark", "System" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                Assert.Equal("Error 42\n請檢查設備", source.Text);
                Assert.Equal("A previous result used to check state retention.", target.Text);
                Assert.Equal((2, 4), (source.SelectionStart, source.SelectionLength));
                Assert.Equal("en", Get<ComboBox>(window, "SourceLanguage").SelectedValue);
                Assert.Equal("zh", Get<ComboBox>(window, "TargetLanguage").SelectedValue);
                Assert.Same(window.Resources["SurfaceBrush"], source.Background);
                Assert.Same(window.Resources["TextBrush"], target.Foreground);
                Assert.True(Get<Button>(window, "CopyButton").IsEnabled);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditingOrSwappingDirectionInvalidatesOutputWithoutReplacingSource() => OnSta(() =>
    {
        var window = CreateWindow();
        try
        {
            window.SelectedLanguage = "en";
            var source = Get<TextBox>(window, "SourceText");
            var target = Get<TextBox>(window, "TargetText");
            source.Text = "Check device";
            target.Text = "Old output";
            source.AppendText(" again");
            Assert.Empty(target.Text);
            Assert.False(Get<Button>(window, "CopyButton").IsEnabled);
            target.Text = "Old output";
            Get<Button>(window, "SwapButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(target.Text);
            Assert.Equal("Check device again", source.Text);
            Assert.Equal("zh", Get<ComboBox>(window, "SourceLanguage").SelectedValue);
            Assert.Equal("en", Get<ComboBox>(window, "TargetLanguage").SelectedValue);
            Assert.Contains("source text kept", Get<TextBlock>(window, "StatusText").Text);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DataManagementFiltersCatalogDirectionsAndTabSwitchingPreservesTranslation() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        await fixture.Service.ImportAsync(fixture.MakePack("installed.tlpack"));
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "Check D100 within 3000 ms.";
            source.Select(6, 4);
            ApplyResult(window, source.Text, "Check D100 within 3000 ms.");
            Get<Button>(window, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var frame = Assert.IsType<WindowFrame>(window.Content);
            window.Content = null;
            var host = new Border { Child = frame, Resources = window.Resources };
            Assert.Equal(Visibility.Visible, Get<Grid>(window, "TranslationPage").Visibility);
            Assert.Equal(Visibility.Collapsed, Get<ScrollViewer>(window, "DataPage").Visibility);
            ShowPage(window, data: true);
            Layout(host, 900, 720);
            Assert.Equal(Visibility.Collapsed, Get<Grid>(window, "TranslationPage").Visibility);
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(window, "DataPage").Visibility);
            var downloadSource = Get<ComboBox>(window, "DownloadSourceLanguage");
            var downloadTarget = Get<ComboBox>(window, "DownloadTargetLanguage");
            var download = Get<Button>(window, "DownloadButton");
            Assert.Equal(LanguagePackCatalog.Packs.Select(pack => pack.SourceLanguage).Distinct().Order(), OptionCodes(downloadSource).Order());
            Assert.Contains($"{LanguagePackCatalog.Packs.SelectMany(pack => new[] { pack.SourceLanguage, pack.TargetLanguage }).Distinct().Count()} languages", Get<TextBlock>(window, "DownloadCatalogSummary").Text);
            Assert.Contains($"{LanguagePackCatalog.Packs.Count} translation directions", Get<TextBlock>(window, "DownloadCatalogSummary").Text);
            foreach (var sourceCode in OptionCodes(downloadSource))
            {
                downloadSource.SelectedValue = sourceCode;
                var packs = LanguagePackCatalog.Packs.Where(pack => pack.SourceLanguage == sourceCode).ToArray();
                Assert.Equal(packs.Select(pack => pack.TargetLanguage).Order(), OptionCodes(downloadTarget).Order());
                Assert.DoesNotContain(sourceCode, OptionCodes(downloadTarget));
                foreach (var pack in packs)
                {
                    downloadTarget.SelectedValue = pack.TargetLanguage;
                    AssertDownloadDetailsMatchSelection(window, pack);
                    Assert.Equal(pack.Id == "en-zh" ? "Installed" : "Download", download.Content);
                    Assert.Equal(pack.Id != "en-zh", download.IsEnabled);
                }
            }
            ShowPage(window, data: false);
            Layout(host, 900, 720);
            Assert.Equal("Check D100 within 3000 ms.", source.Text);
            Assert.Equal((6, 4), (source.SelectionStart, source.SelectionLength));
            Assert.Equal("Check D100 within 3000 ms.", Get<TextBox>(window, "TargetText").Text);
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            Assert.Contains("Missing 1 language pack", Get<TextBlock>(window, "AvailabilityText").Text);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Assert.Contains("Via English", Get<TextBlock>(window, "AvailabilityText").Text);
            Assert.Contains("Missing 2 language pack", Get<TextBlock>(window, "AvailabilityText").Text);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DownloadSelectionPreferencesAndPagesKeepTranslationWithoutNetworkRequests() => OnSta(() =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        using var transport = new CountingDownloadHandler();
        using var client = new HttpClient(transport);
        var window = fixture.CreateWindow(client);
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "Keep D100 and 3000 ms.";
            source.Select(5, 4);
            ApplyResult(window, source.Text, "Retained translation D100 and 3000 ms.");
            var downloadSource = Get<ComboBox>(window, "DownloadSourceLanguage");
            var downloadTarget = Get<ComboBox>(window, "DownloadTargetLanguage");
            downloadTarget.SelectedValue = "fr";
            downloadSource.SelectedValue = "fr";
            Assert.Equal("en", downloadTarget.SelectedValue);
            downloadSource.SelectedValue = "en";
            downloadTarget.SelectedValue = "fr";
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            foreach (var theme in new[] { "Ink", "InkDark" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                ShowPage(window, data: true);
                Activate(window);
                Assert.Equal("en", downloadSource.SelectedValue);
                Assert.Equal("fr", downloadTarget.SelectedValue);
                AssertDownloadDetailsMatchSelection(window, LanguagePackCatalog.Packs.Single(pack => pack.Id == "en-fr"));
                ShowPage(window, data: false);
                Assert.Equal("en", Get<ComboBox>(window, "SourceLanguage").SelectedValue);
                Assert.Equal("zh", Get<ComboBox>(window, "TargetLanguage").SelectedValue);
                Assert.Equal("Keep D100 and 3000 ms.", source.Text);
                Assert.Equal((5, 4), (source.SelectionStart, source.SelectionLength));
                Assert.Equal("Retained translation D100 and 3000 ms.", Get<TextBox>(window, "TargetText").Text);
                Assert.True(Get<Button>(window, "CopyButton").IsEnabled);
                Assert.Empty(fixture.Service.GetInstalledPacks());
                Assert.Equal(0, transport.RequestCount);
                Assert.Equal(Visibility.Collapsed, Get<ProgressBar>(window, "OperationProgress").Visibility);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DownloadSelectionSurvivesImportRemovalAndExternalPackRefresh() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var window = fixture.CreateWindow();
        try
        {
            Get<ComboBox>(window, "DownloadTargetLanguage").SelectedValue = "fr";
            var download = Get<Button>(window, "DownloadButton");
            Assert.True(download.IsEnabled);
            await window.ImportPackAsync(fixture.MakePack("unrelated.tlpack"));
            Assert.Equal("en-fr", download.Tag);
            Assert.Equal("Download", download.Content);
            Assert.True(download.IsEnabled);

            var external = new LanguagePackService(fixture.PackDirectory, NoModelValidation);
            await external.ImportAsync(fixture.MakePack("external-fr.tlpack", "en", "fr"));
            Activate(window);
            Assert.Equal("en-fr", download.Tag);
            Assert.Equal("Installed", download.Content);
            Assert.False(download.IsEnabled);

            var frame = Assert.IsType<WindowFrame>(window.Content);
            window.Content = null;
            var host = new Border { Child = frame, Resources = window.Resources };
            Layout(host, 900, 720);
            var remove = Assert.Single(Descendants(Get<ItemsControl>(window, "PacksList")).OfType<Button>(), button => Equals(button.Tag, "en-fr"));
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Get<ProgressBar>(window, "OperationProgress").Visibility == Visibility.Collapsed);
            Assert.Equal("en-fr", download.Tag);
            Assert.Equal("Download", download.Content);
            Assert.True(download.IsEnabled);
            Assert.DoesNotContain(fixture.Service.GetInstalledPacks(), pack => pack.Manifest.Id == "en-fr");

            await window.ImportPackAsync(fixture.MakePack("ui-fr.tlpack", "en", "fr"));
            Assert.Equal("en-fr", download.Tag);
            Assert.Equal("Installed", download.Content);
            Assert.False(download.IsEnabled);
            external.Remove("en-fr");
            ShowPage(window, data: true);
            Assert.Equal("en", Get<ComboBox>(window, "DownloadSourceLanguage").SelectedValue);
            Assert.Equal("fr", Get<ComboBox>(window, "DownloadTargetLanguage").SelectedValue);
            Assert.Equal("en-fr", download.Tag);
            Assert.Equal("Download", download.Content);
            Assert.True(download.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BusyOperationsDisableDownloadSelectorsAndRestoreTheirSelection() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        using var transport = new CountingDownloadHandler();
        using var client = new HttpClient(transport);
        var window = fixture.CreateWindow(client);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var source = Get<ComboBox>(window, "DownloadSourceLanguage");
            var target = Get<ComboBox>(window, "DownloadTargetLanguage");
            target.SelectedValue = "fr";
            var operation = InvokeTask(window, "RunOperationAsync", new Func<CancellationToken, Task>(_ => release.Task));
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            {
                window.SelectedLanguage = language;
                ShowPage(window, data: true);
                Activate(window);
                Assert.False(source.IsEnabled);
                Assert.False(target.IsEnabled);
                Assert.False(Get<Button>(window, "DownloadButton").IsEnabled);
                Assert.False(Get<Button>(window, "ImportButton").IsEnabled);
                Assert.Equal("en", source.SelectedValue);
                Assert.Equal("fr", target.SelectedValue);
                await window.DownloadPackAsync("en-fr");
                Assert.Equal(0, transport.RequestCount);
            }
            release.SetResult();
            await operation;
            Assert.True(source.IsEnabled);
            Assert.True(target.IsEnabled);
            Assert.True(Get<Button>(window, "DownloadButton").IsEnabled);
            Assert.True(Get<Button>(window, "ImportButton").IsEnabled);
            Assert.Equal("en", source.SelectedValue);
            Assert.Equal("fr", target.SelectedValue);
            Assert.Equal("en-fr", Get<Button>(window, "DownloadButton").Tag);
        }
        finally { release.TrySetResult(); window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AcceptingMissingPivotPacksDownloadsOnlyMissingDirectionsOnTheDataPage(bool firstPackInstalled) => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        if (firstPackInstalled)
            await fixture.Service.ImportAsync(fixture.MakePack("installed-zh-en.tlpack", "zh", "en"));
        var prompts = new List<string[]>();
        var downloads = new List<(string Id, object Source, object Target, Visibility Page)>();
        MainWindow? window = null;
        window = fixture.CreateWindow(confirmMissingPacks: packs =>
        {
            prompts.Add(packs.Select(pack => pack.Id).ToArray());
            return true;
        }, downloadPack: async (pack, progress, token) =>
        {
            downloads.Add((pack.Id, Get<ComboBox>(window!, "DownloadSourceLanguage").SelectedValue,
                Get<ComboBox>(window!, "DownloadTargetLanguage").SelectedValue, Get<ScrollViewer>(window!, "DataPage").Visibility));
            await fixture.Service.ImportAsync(fixture.MakePack("download-" + pack.Id + ".tlpack", pack.SourceLanguage, pack.TargetLanguage), null, token);
        });
        try
        {
            // The direction change must offer missing packs even before the user enters text.
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            await WaitUntilAsync(() => Get<ProgressBar>(window, "OperationProgress").Visibility == Visibility.Collapsed && fixture.Service.Find("en", "fr") is not null);
            var expected = firstPackInstalled ? new[] { "en-fr" } : new[] { "zh-en", "en-fr" };
            Assert.Equal(expected, Assert.Single(prompts));
            Assert.Equal(expected, downloads.Select(download => download.Id).ToArray());
            foreach (var download in downloads)
            {
                var pack = LanguagePackCatalog.Packs.Single(pack => pack.Id == download.Id);
                Assert.Equal(pack.SourceLanguage, download.Source);
                Assert.Equal(pack.TargetLanguage, download.Target);
                Assert.Equal(Visibility.Visible, download.Page);
            }
            Assert.Equal(new[] { "en-fr", "zh-en" }, fixture.Service.GetInstalledPacks().Select(pack => pack.Manifest.Id).Order().ToArray());
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(window, "DataPage").Visibility);
            Assert.Equal(Visibility.Collapsed, Get<Grid>(window, "TranslationPage").Visibility);
            Assert.Equal("en", Get<ComboBox>(window, "DownloadSourceLanguage").SelectedValue);
            Assert.Equal("fr", Get<ComboBox>(window, "DownloadTargetLanguage").SelectedValue);
            Assert.Empty(Get<TextBox>(window, "SourceText").Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Contains("Offline ready", Get<TextBlock>(window, "AvailabilityText").Text);
            Assert.Contains("Via English", Get<TextBlock>(window, "AvailabilityText").Text);
        }
        finally
        {
            window.Close();
            // Wait for a cancelled import to release its archive before fixture cleanup.
            await WaitUntilAsync(() => typeof(MainWindow).GetField("_operation", PrivateInstance)!.GetValue(window) is null);
        }
    });

    [Fact]
    public Task DecliningMissingPivotPacksPreservesInputAndSuppressesTypingPromptsUntilRetryOrDirectionChange() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var prompts = new List<string[]>();
        var downloads = new List<string>();
        var window = fixture.CreateWindow(confirmMissingPacks: packs =>
        {
            prompts.Add(packs.Select(pack => pack.Id).ToArray());
            return false;
        }, downloadPack: (pack, _, _) =>
        {
            downloads.Add(pack.Id);
            return Task.CompletedTask;
        });
        try
        {
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            await WaitUntilAsync(() => prompts.Count == 1);
            Assert.Equal(new[] { "zh-en", "en-fr" }, prompts[0]);
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "原文 D100";
            await Task.Delay(700);
            source.AppendText(" 3000 ms");
            await Task.Delay(700);
            Assert.Single(prompts);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
            await window.TranslateAsync();
            Assert.Equal(2, prompts.Count);
            Assert.Equal(prompts[0], prompts[1]);
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "ja";
            await WaitUntilAsync(() => prompts.Count == 3);
            Assert.Equal(new[] { "zh-en", "en-ja" }, prompts[2]);
            Assert.Empty(downloads);
            Assert.Empty(fixture.Service.GetInstalledPacks());
            Assert.Equal("原文 D100 3000 ms", source.Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(Visibility.Visible, Get<Grid>(window, "TranslationPage").Visibility);
            Assert.False(Get<Button>(window, "CopyButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MissingDirectPackConfirmationWaitsForTypingToPause() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var prompts = new List<string[]>();
        var window = fixture.CreateWindow(confirmMissingPacks: packs =>
        {
            prompts.Add(packs.Select(pack => pack.Id).ToArray());
            return false;
        });
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "first input";
            await Task.Delay(250);
            Assert.Empty(prompts);
            source.AppendText(" still typing");
            await Task.Delay(250);
            Assert.Empty(prompts);
            source.Text = "Final pasted text";
            await WaitUntilAsync(() => prompts.Count == 1);
            Assert.Equal(new[] { "en-zh" }, Assert.Single(prompts));
            Assert.Equal("Final pasted text", source.Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Empty(fixture.Service.GetInstalledPacks());
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CancellingAutomaticPackDownloadStopsRemainingDirectionsAndCannotReenterConfirmation() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var downloads = new List<string>();
        var prompts = 0;
        var window = fixture.CreateWindow(confirmMissingPacks: _ => { prompts++; return true; }, downloadPack: async (pack, _, token) =>
        {
            downloads.Add(pack.Id);
            await Task.Delay(Timeout.Infinite, token);
        });
        try
        {
            Get<TextBox>(window, "SourceText").Text = "Keep original D100";
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            await WaitUntilAsync(() => downloads.Count == 1);
            Assert.True(Get<Button>(window, "CancelButton").IsEnabled);
            Assert.False(Get<ComboBox>(window, "DownloadSourceLanguage").IsEnabled);
            Assert.False(Get<ComboBox>(window, "DownloadTargetLanguage").IsEnabled);
            await window.TranslateAsync();
            Assert.Equal(1, prompts);
            Get<Button>(window, "CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => Get<ProgressBar>(window, "OperationProgress").Visibility == Visibility.Collapsed);
            await Task.Delay(700);
            Assert.Equal(new[] { "zh-en" }, downloads);
            Assert.Equal(1, prompts);
            Assert.Empty(fixture.Service.GetInstalledPacks());
            Assert.Equal("Keep original D100", Get<TextBox>(window, "SourceText").Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Contains("cancelled", Get<TextBlock>(window, "StatusText").Text);
            Assert.False(Get<Button>(window, "CancelButton").IsEnabled);
            Assert.True(Get<ComboBox>(window, "DownloadSourceLanguage").IsEnabled);
            Assert.True(Get<ComboBox>(window, "DownloadTargetLanguage").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FailedAutomaticPackDownloadStopsRemainingDirectionsAndPreservesOriginalText() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var downloads = new List<string>();
        var window = fixture.CreateWindow(confirmMissingPacks: _ => true, downloadPack: (pack, _, _) =>
        {
            downloads.Add(pack.Id);
            return Task.FromException(new IOException("PRIVATE download fixture diagnostic"));
        });
        try
        {
            Get<TextBox>(window, "SourceText").Text = "Keep original D100";
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            await WaitUntilAsync(() => downloads.Count > 0 && Get<ProgressBar>(window, "OperationProgress").Visibility == Visibility.Collapsed);
            await Task.Delay(700);
            Assert.Equal(new[] { "zh-en" }, downloads);
            Assert.Empty(fixture.Service.GetInstalledPacks());
            Assert.Equal("Keep original D100", Get<TextBox>(window, "SourceText").Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.False(Get<Button>(window, "CopyButton").IsEnabled);
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(window, "DataPage").Visibility);
            Assert.Contains("Cannot read or save the language pack", Get<TextBlock>(window, "StatusText").Text);
            Assert.DoesNotContain("PRIVATE", Get<TextBlock>(window, "StatusText").Text);
            Assert.True(Get<Button>(window, "ImportButton").IsEnabled);
            Assert.False(Get<TextBox>(window, "SourceText").IsReadOnly);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DownloadDropdownsShowReadableOptionsAndSupportKeyboardSelection() => OnSta(() =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var window = fixture.CreateWindow();
        try
        {
            var frame = Assert.IsType<WindowFrame>(window.Content);
            window.Content = null;
            var host = new Border { Child = frame, Resources = window.Resources };
            host.SetResourceReference(TextElement.FontFamilyProperty, "UiFontFamily");
            host.SetResourceReference(TextElement.FontSizeProperty, "UiFontSize");
            using var presentation = new HwndSource(new HwndSourceParameters("TransLamp dropdown verification")
            {
                Width = 900, Height = 720, PositionX = -10000, PositionY = -10000,
                WindowStyle = unchecked((int)0x80000000)
            }) { RootVisual = host };
            ShowPage(window, data: true);
            var source = Get<ComboBox>(window, "DownloadSourceLanguage");
            var target = Get<ComboBox>(window, "DownloadTargetLanguage");
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            foreach (var theme in new[] { "Ink", "InkDark" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                source.SelectedValue = "en";
                target.SelectedValue = "zh";
                Layout(host, 900, 720);
                RaiseKey(source, presentation, Key.F4);
                AssertReadablePopup(window, source, $"dropdown-{language}-{theme}-source.png");
                RaiseKey(source, presentation, Key.F4);
                Assert.False(source.IsDropDownOpen);
                RaiseKey(source, presentation, Key.End);
                Assert.Equal(OptionCodes(source).Last(), source.SelectedValue);
                Assert.Equal("en", target.SelectedValue);
                AssertDownloadDetailsMatchSelection(window, LanguagePackCatalog.Packs.Single(pack => pack.Id == Get<Button>(window, "DownloadButton").Tag as string));
                RaiseKey(source, presentation, Key.Home);
                Assert.Equal("en", source.SelectedValue);
                Layout(host, 900, 720);
                RaiseKey(target, presentation, Key.F4);
                AssertReadablePopup(window, target, $"dropdown-{language}-{theme}-target.png");
                RaiseKey(target, presentation, Key.F4);
                Assert.False(target.IsDropDownOpen);
                RaiseKey(target, presentation, Key.End);
                Assert.Equal(OptionCodes(target).Last(), target.SelectedValue);
                AssertDownloadDetailsMatchSelection(window, LanguagePackCatalog.Packs.Single(pack => pack.Id == Get<Button>(window, "DownloadButton").Tag as string));
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MissingRuntimeUnsupportedDirectionAndOversizeInputStayExplicit() => OnSta(async () =>
    {
        var window = CreateWindow();
        try
        {
            window.SelectedLanguage = "en";
            var source = Get<TextBox>(window, "SourceText");
            source.Text = new string('x', 20_001);
            Assert.Equal(20_001, source.Text.Length);
            Assert.Contains("20,001", Get<TextBlock>(window, "SourceCount").Text);
            Assert.Same(window.Resources["ErrorBrush"], Get<TextBlock>(window, "SourceCount").Foreground);
            Assert.False(Get<Button>(window, "TranslateButton").IsEnabled);
            Assert.Contains("runtime not found", Get<TextBlock>(window, "AvailabilityText").Text);
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "es";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "en";
            Assert.Contains("direction is not available", Get<TextBlock>(window, "AvailabilityText").Text);
            await window.TranslateAsync();
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "en";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "en";
            Assert.Contains("different source and target", Get<TextBlock>(window, "AvailabilityText").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InvalidImportReturnsFriendlyErrorAndRestoresEditableState() => OnSta(async () =>
    {
        var window = CreateWindow();
        try
        {
            window.SelectedLanguage = "en";
            await window.ImportPackAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tlpack"));
            Assert.Contains("Cannot complete this operation", Get<TextBlock>(window, "StatusText").Text);
            Assert.DoesNotContain(Path.GetTempPath(), Get<TextBlock>(window, "StatusText").Text);
            window.SelectedLanguage = "ja";
            Assert.Contains("必要なファイル", Get<TextBlock>(window, "StatusText").Text);
            Assert.True(Get<Button>(window, "ImportButton").IsEnabled);
            Assert.False(Get<TextBox>(window, "SourceText").IsReadOnly);
            Assert.False(Get<Button>(window, "CancelButton").IsEnabled);
            Assert.Equal(Visibility.Collapsed, Get<ProgressBar>(window, "OperationProgress").Visibility);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SourceChangesInvalidateCompletionAndErrorStatusButKeepBusyProgress() => OnSta(async () =>
    {
        var window = CreateWindow();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window.SelectedLanguage = "en";
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "Device D100 needs 3000 ms.";
            ApplyResult(window, source.Text, "設備 D100 需要 30000 ms。");
            Assert.Contains("Translation complete", Get<TextBlock>(window, "StatusText").Text);
            source.AppendText(" Check again.");
            Assert.Contains("Source text changed", Get<TextBlock>(window, "StatusText").Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(Visibility.Collapsed, Get<Border>(window, "QualityWarning").Visibility);
            SetStatus(window, "SYNTHETIC previous error", true);
            source.AppendText(" Retry.");
            Assert.DoesNotContain("SYNTHETIC", Get<TextBlock>(window, "StatusText").Text);
            Assert.Same(window.Resources["MutedBrush"], Get<TextBlock>(window, "StatusText").Foreground);
            source.Clear();
            Assert.Contains("Enter some text", Get<TextBlock>(window, "StatusText").Text);

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = InvokeTask(window, "RunOperationAsync", new Func<CancellationToken, Task>(_ =>
            {
                SetStatus(window, "SYNTHETIC translation progress 1 / 3");
                started.SetResult();
                return release.Task;
            }));
            await started.Task;
            Assert.True(source.IsReadOnly);
            source.Text = "A programmatic change while busy";
            Assert.Equal("SYNTHETIC translation progress 1 / 3", Get<TextBlock>(window, "StatusText").Text);
            release.SetResult();
            await operation;
            Assert.False(source.IsReadOnly);
        }
        finally { release.TrySetResult(); window.Close(); }
    });

    [Fact]
    public Task ActivationRefreshesExternallyImportedAndRemovedPacks() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "Check device";
            Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
            Assert.Contains("Missing 1 language pack", Get<TextBlock>(window, "AvailabilityText").Text);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
            var externalService = new LanguagePackService(fixture.PackDirectory, NoModelValidation);
            await externalService.ImportAsync(fixture.MakePack("external.tlpack"));
            Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
            Activate(window);
            Assert.Single(Get<ItemsControl>(window, "PacksList").Items);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
            Assert.Equal("Check device", source.Text);
            externalService.Remove("en-zh");
            Activate(window);
            Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
            Assert.Contains("Missing 1 language pack", Get<TextBlock>(window, "AvailabilityText").Text);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
            Assert.Equal("Check device", source.Text);
            await externalService.ImportAsync(fixture.MakePack("external-reimport.tlpack"));
            Get<Button>(window, "DataTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(Get<ItemsControl>(window, "PacksList").Items);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BusyActivationDefersPackRefreshUntilOperationFinishes() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var window = fixture.CreateWindow();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Get<TextBox>(window, "SourceText").Text = "Check device";
            var operation = InvokeTask(window, "RunOperationAsync", new Func<CancellationToken, Task>(_ => release.Task));
            Assert.True(Get<TextBox>(window, "SourceText").IsReadOnly);
            await new LanguagePackService(fixture.PackDirectory, NoModelValidation).ImportAsync(fixture.MakePack("external-busy.tlpack"));
            Activate(window);
            Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
            release.SetResult();
            await operation;
            Assert.Single(Get<ItemsControl>(window, "PacksList").Items);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
        }
        finally { release.TrySetResult(); window.Close(); }
    });

    [Fact]
    public Task ActivationWhilePackManagerIsLockedShowsFriendlyErrorAndCanRefreshAfterRelease() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        await fixture.Service.ImportAsync(fixture.MakePack("locked-pack.tlpack"));
        var window = fixture.CreateWindow();
        try
        {
            Get<TextBox>(window, "SourceText").Text = "Check device";
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
            using (var writeLock = new FileStream(Path.Combine(fixture.PackDirectory, ".write.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Activate(window);
                Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
                Assert.False(Get<Button>(window, "TranslateButton").IsEnabled);
                Assert.Contains("Another TransLamp window", Get<TextBlock>(window, "StatusText").Text);
                Assert.DoesNotContain(fixture.RootDirectory, Get<TextBlock>(window, "StatusText").Text);
                Get<Button>(window, "DataTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
            }
            Activate(window);
            Assert.Single(Get<ItemsControl>(window, "PacksList").Items);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LiteralWarningKeepsTheCompleteResultAcrossLanguagesAndClearsWithInputOrDirection() => OnSta(() =>
    {
        var window = CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            var translation = "Device D100 did not respond within 30000 ms.";
            source.Text = "設備 D100 在 3000 ms 內沒有回應。";
            ApplyResult(window, source.Text, translation);
            foreach (var (language, warning) in new[] { ("en", "Check possible changes"), ("zh-TW", "原文字面值可能缺漏或改變"), ("ja", "欠落や変更") })
            {
                window.SelectedLanguage = language;
                Assert.Equal(Visibility.Visible, Get<Border>(window, "QualityWarning").Visibility);
                Assert.Contains(warning, Get<TextBlock>(window, "QualityWarningText").Text);
                Assert.Contains("3000 (0/1)", Get<TextBlock>(window, "QualityWarningText").Text);
                Assert.DoesNotContain("D100", Get<TextBlock>(window, "QualityWarningText").Text);
                Assert.Equal(translation, Get<TextBox>(window, "TargetText").Text);
                Assert.True(Get<Button>(window, "CopyButton").IsEnabled);
            }
            source.AppendText(" 再試一次。");
            Assert.Equal(Visibility.Collapsed, Get<Border>(window, "QualityWarning").Visibility);
            Assert.Empty(Get<TextBlock>(window, "QualityWarningText").Text);
            ApplyResult(window, source.Text, translation);
            Get<Button>(window, "SwapButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, Get<Border>(window, "QualityWarning").Visibility);
            ApplyResult(window, source.Text, translation);
            source.Clear();
            Assert.Equal(Visibility.Collapsed, Get<Border>(window, "QualityWarning").Visibility);
            Assert.Empty(Get<TextBlock>(window, "QualityWarningText").Text);
            ApplyResult(window, "Wait ten seconds.", "等待 10 秒。");
            Assert.Equal(Visibility.Collapsed, Get<Border>(window, "QualityWarning").Visibility);
            Assert.Equal("等待 10 秒。", Get<TextBox>(window, "TargetText").Text);
            Assert.True(Get<Button>(window, "CopyButton").IsEnabled);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LiteralWarningAndBothPagesFitTheMinimumSizeInThreeLanguages() => OnSta(async () =>
    {
        using var fixture = new UiFixture();
        await fixture.Service.ImportAsync(fixture.MakePack("en-zh.tlpack"));
        await fixture.Service.ImportAsync(fixture.MakePack("zh-en.tlpack", "zh", "en"));
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            var longPath = "C:\\Logs\\" + new string('a', 500) + ".txt";
            source.Text = string.Join(" ", Enumerable.Range(1000, 30).Select(number => "D" + number)) +
                " 192.168.1.10:502 uses 3000 ms and retry_count. Logs: " + longPath + " Read https://example.test/api/v1?retry=3.";
            ApplyResult(window, source.Text, "設備在其他位置使用 30000 ms；請查看日誌。");
            var frame = Assert.IsType<WindowFrame>(window.Content);
            window.Content = null;
            var host = new Border { Child = frame, Resources = window.Resources };
            host.SetResourceReference(TextElement.FontFamilyProperty, "UiFontFamily");
            host.SetResourceReference(TextElement.FontSizeProperty, "UiFontSize");
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            foreach (var theme in new[] { "Ink", "InkDark" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                ShowPage(window, data: false);
                Layout(host, 900, 720);
                foreach (var name in new[] { "PasteButton", "CopyButton", "CancelButton", "TranslationTab", "DataTab", "StatusText", "QualityWarning" })
                    AssertWithinViewport(Get<FrameworkElement>(window, name), host, 900, 720);
                var qualityNotice = Get<TextBlock>(window, "QualityNotice");
                AssertWithinViewport(qualityNotice, host, 900, 720);
                AssertWithinVisibleAncestors(qualityNotice, host);
                AssertWithinVisibleAncestors(Get<Button>(window, "CopyButton"), host);
                AssertWithinVisibleAncestors(Get<Border>(window, "QualityWarning"), host);
                AssertWithinVisibleAncestors(Get<TextBox>(window, "SourceText"), host);
                AssertWithinVisibleAncestors(Get<TextBox>(window, "TargetText"), host);
                Assert.True(Get<TextBox>(window, "SourceText").ActualHeight >= 70);
                Assert.True(Get<TextBox>(window, "TargetText").ActualHeight >= 70);
                Assert.Equal(Visibility.Visible, Get<Border>(window, "QualityWarning").Visibility);
                Assert.True(Get<Button>(window, "CopyButton").IsEnabled);
                Assert.Contains(longPath, Get<TextBlock>(window, "QualityWarningText").Text);
                Assert.Contains("D1029 (0/1)", Get<TextBlock>(window, "QualityWarningText").Text);
                var warningScroll = Assert.IsType<ScrollViewer>(Get<Border>(window, "QualityWarning").Child);
                Assert.True(warningScroll.ExtentHeight > warningScroll.ViewportHeight);
                warningScroll.ScrollToEnd();
                Layout(host, 900, 720);
                Assert.True(warningScroll.VerticalOffset > 0);
                Assert.True(warningScroll.VerticalOffset + warningScroll.ViewportHeight >= warningScroll.ExtentHeight - 1);
                Assert.True(Get<TextBlock>(window, "QualityWarningText").ActualWidth <= warningScroll.ViewportWidth + 1);
                SaveImage(host, $"literal-warning-{language}-{theme}-translation.png");
                ShowPage(window, data: true);
                Get<ScrollViewer>(window, "DataPage").ScrollToTop();
                Layout(host, 900, 720);
                Assert.Equal(Visibility.Collapsed, Get<Grid>(window, "TranslationPage").Visibility);
                AssertWithinViewport(Get<ScrollViewer>(window, "DataPage"), host, 900, 720);
                AssertWithinVisibleAncestors(Get<Border>(window, "QualityWarning"), host);
                AssertDownloadTitleUsesThemeForeground(window);
                foreach (var name in new[] { "DownloadSourceLanguage", "DownloadTargetLanguage", "DownloadDetails", "DownloadButton", "ImportButton" })
                    AssertWithinViewport(Get<FrameworkElement>(window, name), host, 900, 720);
                SaveImage(host, $"literal-warning-{language}-{theme}-data.png");
                Get<ScrollViewer>(window, "DataPage").ScrollToEnd();
                Layout(host, 900, 720);
                AssertWithinViewport(Get<Button>(window, "ImportButton"), host, 900, 720);
                AssertPackTitlesUseThemeForeground(window, "PacksList", expectedCount: 2);
                SaveImage(host, $"literal-warning-{language}-{theme}-data-installed.png");
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BundledPreparationReportsPartialSuccessAndKeepsHealthyDirectionUsable() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        fixture.MakePack("100-healthy.tlpack");
        File.WriteAllText(Path.Combine(fixture.BundledDirectory, "000-damaged.tlpack"), "not a zip package");
        var window = fixture.CreateWindow();
        try
        {
            Get<TextBox>(window, "SourceText").Text = "Check device";
            await InvokeTask(window, "PrepareBundledPacksAsync");
            foreach (var (language, count) in new[] { ("en", "1 failed"), ("zh-TW", "1 個失敗"), ("ja", "1 個は失敗") })
            {
                window.SelectedLanguage = language;
                Assert.Contains(count, Get<TextBlock>(window, "StatusText").Text);
                Assert.DoesNotContain(fixture.RootDirectory, Get<TextBlock>(window, "StatusText").Text);
                Assert.DoesNotContain("000-damaged", Get<TextBlock>(window, "StatusText").Text);
                Assert.Single(Get<ItemsControl>(window, "PacksList").Items);
                Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
            }
            Assert.True(Get<Button>(window, "ImportButton").IsEnabled);
            Assert.False(Get<TextBox>(window, "SourceText").IsReadOnly);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EmptyBundledDirectoryDoesNotClaimInstalledPacksAreReady() => OnSta(async () =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var window = fixture.CreateWindow();
        try
        {
            await InvokeTask(window, "PrepareBundledPacksAsync");
            Assert.Contains("No language packs installed", Get<TextBlock>(window, "StatusText").Text);
            Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
            Assert.False(Get<Button>(window, "TranslateButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("invalid-model", "validation failed", "載入驗證失敗", "読み込み検証に失敗")]
    [InlineData("validation-timeout", "validation took too long", "驗證時間過長", "検証に時間")]
    [InlineData("pack-recovery-failed", "data has been retained", "原有資料已保留", "データは保持")]
    public Task ModelLoadValidationFailuresAreExplicitAndDoNotInstall(string code, string english, string chinese, string japanese) => OnSta(async () =>
    {
        using var fixture = new UiFixture(validator: (_, _) => Task.FromException(new TransLampException(code, "PRIVATE fixture diagnostic")));
        var window = fixture.CreateWindow();
        try
        {
            await window.ImportPackAsync(fixture.MakePack("bad-model.tlpack"));
            foreach (var (language, expected) in new[] { ("en", english), ("zh-TW", chinese), ("ja", japanese) })
            {
                window.SelectedLanguage = language;
                Assert.Contains(expected, Get<TextBlock>(window, "StatusText").Text);
                Assert.DoesNotContain("PRIVATE", Get<TextBlock>(window, "StatusText").Text);
                Assert.DoesNotContain(fixture.RootDirectory, Get<TextBlock>(window, "StatusText").Text);
            }
            Assert.Empty(fixture.Service.GetInstalledPacks());
            Assert.Empty(Get<ItemsControl>(window, "PacksList").Items);
            Assert.True(Get<Button>(window, "ImportButton").IsEnabled);
            Assert.False(Get<TextBox>(window, "SourceText").IsReadOnly);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ThreeLanguagesAndBambooThemesRenderAtMinimumSize() => OnSta(() =>
    {
        using var fixture = new UiFixture(runtimeAvailable: true);
        var window = fixture.CreateWindow();
        try
        {
            Get<TextBox>(window, "SourceText").Text = "Error E104: The connection timed out.\nCheck the cable and try again.";
            var frame = Assert.IsType<WindowFrame>(window.Content);
            window.Content = null;
            var host = new Border { Child = frame, Resources = window.Resources };
            host.SetResourceReference(TextElement.FontFamilyProperty, "UiFontFamily");
            host.SetResourceReference(TextElement.FontSizeProperty, "UiFontSize");
            foreach (var language in new[] { "en", "zh-TW", "ja" })
            foreach (var theme in new[] { "Ink", "InkDark" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                Get<ComboBox>(window, "SourceLanguage").SelectedValue = "en";
                Get<ComboBox>(window, "TargetLanguage").SelectedValue = "zh";
                ShowPage(window, data: false);
                Layout(host, 900, 720);
                foreach (var name in new[] { "PasteButton", "CopyButton", "CancelButton", "TranslationTab", "DataTab", "StatusText" })
                {
                    AssertWithinViewport(Get<FrameworkElement>(window, name), host, 900, 720);
                    AssertWithinVisibleAncestors(Get<FrameworkElement>(window, name), host);
                }
                Assert.True(Get<TextBox>(window, "SourceText").ActualHeight >= 70);
                AssertWithinVisibleAncestors(Get<TextBox>(window, "SourceText"), host);
                AssertWithinVisibleAncestors(Get<TextBox>(window, "TargetText"), host);
                SaveImage(host, $"{language}-{theme}-translation.png");

                Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
                Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
                Layout(host, 900, 720);
                var availability = Get<TextBlock>(window, "AvailabilityText");
                Assert.Equal(TextWrapping.Wrap, availability.TextWrapping);
                AssertWithinViewport(availability, host, 900, 720);
                AssertWithinVisibleAncestors(availability, host);
                AssertWithinVisibleAncestors(Get<TextBox>(window, "SourceText"), host);
                AssertWithinVisibleAncestors(Get<TextBox>(window, "TargetText"), host);
                Assert.True(Get<TextBox>(window, "SourceText").ActualHeight >= 70);
                SaveImage(host, $"pivot-{language}-{theme}.png");

                ShowPage(window, data: true);
                var data = Get<ScrollViewer>(window, "DataPage");
                data.ScrollToTop();
                Layout(host, 900, 720);
                Assert.Equal(Visibility.Collapsed, Get<Grid>(window, "TranslationPage").Visibility);
                Assert.Equal(Visibility.Visible, data.Visibility);
                foreach (var name in new[] { "TranslationTab", "DataTab", "DataPage", "CancelButton", "StatusText" })
                {
                    AssertWithinViewport(Get<FrameworkElement>(window, name), host, 900, 720);
                    AssertWithinVisibleAncestors(Get<FrameworkElement>(window, name), host);
                }
                AssertDownloadTitleUsesThemeForeground(window);
                foreach (var name in new[] { "DownloadSourceLanguage", "DownloadTargetLanguage", "DownloadDetails", "DownloadButton", "ImportButton" })
                {
                    AssertWithinViewport(Get<FrameworkElement>(window, name), host, 900, 720);
                    AssertWithinVisibleAncestors(Get<FrameworkElement>(window, name), host);
                }
                SaveImage(host, $"{language}-{theme}-data.png");
                data.ScrollToEnd();
                Layout(host, 900, 720);
                AssertWithinViewport(Get<Button>(window, "ImportButton"), host, 900, 720);
                AssertWithinViewport(Get<TextBlock>(window, "PackLocation"), host, 900, 720);
                SaveImage(host, $"{language}-{theme}-data-installed.png");
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("translamp://open")]
    [InlineData("translamp://open/")]
    [InlineData("TransLamp://OPEN")]
    public void ActivationOnlyOpensTheApplication(string uri)
    {
        var options = LaunchOptions.Parse([uri]);
        Assert.Null(options.DataDirectory);
        Assert.Null(options.RuntimeDirectory);
    }

    [Theory]
    [InlineData("translamp://open?runtime-dir=malicious")]
    [InlineData("translamp://open#file")]
    [InlineData("translamp://open/execute")]
    [InlineData("translamp://other")]
    [InlineData("https://open")]
    [InlineData("--unknown")]
    [InlineData("--data-dir")]
    public void ActivationRejectsUnrecognizedParameters(string argument) =>
        Assert.Throws<ArgumentException>(() => LaunchOptions.Parse([argument]));

    [Fact]
    public void ExplicitLocalPathsAreAccepted()
    {
        var options = LaunchOptions.Parse(["--data-dir", ".", "--runtime-dir", "runtime-test", "translamp://open"]);
        Assert.Equal(Path.GetFullPath("."), options.DataDirectory);
        Assert.Equal(Path.GetFullPath("runtime-test"), options.RuntimeDirectory);
    }

    private static MainWindow CreateWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), "TransLamp.UiTests", Guid.NewGuid().ToString("N"));
        return new MainWindow(new LanguagePackService(Path.Combine(root, "packs"), NoModelValidation), new TranslationEngine(Path.Combine(root, "runtime")), Path.Combine(root, "bundled"), _ => false, null)
        {
            PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false
        };
    }

    private static T Get<T>(MainWindow window, string name) where T : class => Assert.IsAssignableFrom<T>(window.FindName(name));

    private static void ShowPage(MainWindow window, bool data) =>
        Get<Button>(window, data ? "DataTab" : "TranslationTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void AssertPackTitlesUseThemeForeground(MainWindow window, string listName, int expectedCount)
    {
        var titles = Descendants(Get<ItemsControl>(window, listName)).OfType<TextBlock>()
            .Where(text => BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == "Title").ToArray();
        Assert.Equal(expectedCount, titles.Length);
        var expected = Assert.IsType<SolidColorBrush>(window.Resources["TextBrush"]);
        Assert.All(titles, title => Assert.Equal(expected.Color, Assert.IsType<SolidColorBrush>(title.Foreground).Color));
    }

    private static string[] OptionCodes(ComboBox combo) => combo.Items.Cast<object>()
        .Select(item => (string)item.GetType().GetProperty("Code")!.GetValue(item)!).ToArray();

    private static string OptionLabel(object item) => (string)item.GetType().GetProperty("Label")!.GetValue(item)!;

    private static void AssertDownloadDetailsMatchSelection(MainWindow window, DownloadableLanguagePack pack)
    {
        Assert.Equal(pack.Id, Get<Button>(window, "DownloadButton").Tag);
        Assert.Equal($"{OptionLabel(Get<ComboBox>(window, "DownloadSourceLanguage").SelectedItem)} → {OptionLabel(Get<ComboBox>(window, "DownloadTargetLanguage").SelectedItem)}", Get<TextBlock>(window, "DownloadPackTitle").Text);
        Assert.Contains(pack.Version, Get<TextBlock>(window, "DownloadPackDetails").Text);
        Assert.Contains(pack.License, Get<TextBlock>(window, "DownloadPackDetails").Text);
        Assert.Contains(pack.Source, Get<TextBlock>(window, "DownloadPackSource").Text);
    }

    private static void AssertDownloadTitleUsesThemeForeground(MainWindow window) =>
        Assert.Equal(Assert.IsType<SolidColorBrush>(window.Resources["TextBrush"]).Color,
            Assert.IsType<SolidColorBrush>(Get<TextBlock>(window, "DownloadPackTitle").Foreground).Color);

    private static void RaiseKey(ComboBox combo, PresentationSource presentation, Key key) =>
        combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, presentation, Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });

    private static void AssertReadablePopup(MainWindow window, ComboBox combo, string imageName)
    {
        Assert.True(combo.IsDropDownOpen);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var popup = Assert.IsType<Popup>(combo.Template.FindName("PART_Popup", combo));
        var surface = Assert.IsType<Border>(popup.Child);
        surface.UpdateLayout();
        Assert.True(popup.IsOpen);
        Assert.InRange(surface.ActualHeight, 1, combo.MaxDropDownHeight);
        Assert.True(surface.ActualWidth >= combo.ActualWidth);
        var background = Assert.IsType<SolidColorBrush>(surface.Background).Color;
        var foreground = Assert.IsType<SolidColorBrush>(window.Resources["TextBrush"]).Color;
        Assert.Equal(Assert.IsType<SolidColorBrush>(window.Resources["SurfaceBrush"]).Color, background);
        Assert.True(ContrastRatio(foreground, background) >= 4.5, "Dropdown option text must remain readable against its popup surface.");
        foreach (var item in combo.Items)
        {
            var container = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromItem(item));
            Assert.Equal(foreground, Assert.IsType<SolidColorBrush>(container.Foreground).Color);
            var label = Assert.Single(Descendants(container).OfType<TextBlock>(), text => text.Text == OptionLabel(item));
            Assert.Equal(foreground, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
            Assert.True(label.ActualWidth > 0 && label.ActualHeight > 0);
            Assert.True(label.ActualWidth <= container.ActualWidth);
        }
        SaveImage(surface, imageName, (int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight));
    }

    private static double ContrastRatio(Color first, Color second)
    {
        static double Channel(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Activate(MainWindow window) => typeof(MainWindow).GetMethod("OnActivated", PrivateInstance)!.Invoke(window, [EventArgs.Empty]);
    private static void ApplyResult(MainWindow window, string source, string translation) => typeof(MainWindow).GetMethod("ApplyTranslationResult", PrivateInstance)!.Invoke(window, [source, translation]);
    private static void SetStatus(MainWindow window, string status, bool error = false) => typeof(MainWindow).GetMethod("SetStatus", PrivateInstance)!.Invoke(window, [new Func<string>(() => status), error]);
    private static Task InvokeTask(MainWindow window, string method, params object[] arguments) => (Task)typeof(MainWindow).GetMethod(method, PrivateInstance)!.Invoke(window, arguments)!;

    // These UI fixtures exercise package/state handling, not native model loading. Production uses engine.ValidatePackAsync.
    private static Task NoModelValidation(InstalledLanguagePack pack, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static void AssertWithinViewport(FrameworkElement element, FrameworkElement host, int width, int height)
    {
        Assert.Equal(Visibility.Visible, element.Visibility);
        Assert.True(element.ActualWidth > 0 && element.ActualHeight > 0, $"{element.Name} has no rendered size.");
        var origin = element.TransformToAncestor(host).Transform(new Point());
        Assert.InRange(origin.X, 0, width);
        Assert.InRange(origin.Y, 0, height);
        Assert.InRange(origin.X + element.ActualWidth, 1, width + 1);
        Assert.InRange(origin.Y + element.ActualHeight, 1, height + 1);
    }

    private static void AssertWithinVisibleAncestors(FrameworkElement element, FrameworkElement host)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement ancestor)
            {
                var origin = element.TransformToAncestor(ancestor).Transform(new Point());
                Assert.True(origin.X >= -0.5 && origin.Y >= -0.5 &&
                    origin.X + element.ActualWidth <= ancestor.ActualWidth + 0.5 &&
                    origin.Y + element.ActualHeight <= ancestor.ActualHeight + 0.5,
                    $"{element.Name} bounds ({origin.X}, {origin.Y}, {element.ActualWidth}, {element.ActualHeight}) extend past {ancestor.GetType().Name}/{ancestor.Name} ({ancestor.ActualWidth}, {ancestor.ActualHeight}).");
            }
            if (ReferenceEquals(parent, host)) return;
        }
        throw new InvalidOperationException("The visible ancestor check did not reach the offscreen host.");
    }

    private sealed class UiFixture : IDisposable
    {
        private readonly string _temporaryBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TransLamp.UiTests"));
        public string RootDirectory { get; }
        public string PackDirectory => Path.Combine(RootDirectory, "packs");
        public string BundledDirectory => Path.Combine(RootDirectory, "bundled");
        public LanguagePackService Service { get; }
        public TranslationEngine Engine { get; }

        public UiFixture(bool runtimeAvailable = false, Func<InstalledLanguagePack, CancellationToken, Task>? validator = null)
        {
            RootDirectory = Path.GetFullPath(Path.Combine(_temporaryBase, Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(BundledDirectory);
            var runtime = Path.Combine(RootDirectory, "runtime");
            if (runtimeAvailable)
            {
                Directory.CreateDirectory(runtime);
                // IsAvailable-only fixtures: these files are never launched by these tests.
                File.WriteAllBytes(Path.Combine(runtime, "python.exe"), []);
                File.WriteAllText(Path.Combine(runtime, "translate.py"), "");
            }
            Engine = new TranslationEngine(runtime);
            Service = new LanguagePackService(PackDirectory, validator ?? NoModelValidation);
        }

        public MainWindow CreateWindow(HttpClient? downloadClient = null,
            Func<IReadOnlyList<DownloadableLanguagePack>, bool>? confirmMissingPacks = null,
            Func<DownloadableLanguagePack, IProgress<LanguagePackDownloadProgress>?, CancellationToken, Task>? downloadPack = null)
        {
            var window = new MainWindow(Service, Engine, BundledDirectory, confirmMissingPacks ?? (_ => false), downloadPack)
            {
                PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false, SelectedLanguage = "en"
            };
            if (downloadClient is not null)
                typeof(MainWindow).GetField("_downloads", PrivateInstance)!.SetValue(window, new LanguagePackDownloadService(Service, downloadClient));
            return window;
        }

        public string MakePack(string fileName, string source = "en", string target = "zh")
        {
            var files = new Dictionary<string, byte[]>
            {
                ["LICENSE"] = Encoding.UTF8.GetBytes("MIT fixture license"),
                ["NOTICE"] = Encoding.UTF8.GetBytes("UI fixture; not a native translation model"),
                ["sentencepiece.model"] = Encoding.UTF8.GetBytes("fixture tokenizer"),
                ["model/model.bin"] = Encoding.UTF8.GetBytes("fixture model"),
                ["model/config.json"] = Encoding.UTF8.GetBytes("{}"),
                ["model/shared_vocabulary.json"] = Encoding.UTF8.GetBytes("[]")
            };
            var manifest = new LanguagePackManifest
            {
                SchemaVersion = 1, Id = source + "-" + target, SourceLanguage = source, TargetLanguage = target,
                DisplayName = "UI fixture " + source + " → " + target, PackageVersion = "1.0.0", ModelName = "UI fixture",
                ModelVersion = LanguagePackCatalog.Packs.Single(pack => pack.Id == source + "-" + target).Version,
                Runtime = LanguagePackService.SupportedRuntime, ModelSource = "https://example.test/fixture", LicenseIdentifier = "MIT",
                Files = files.Select(pair => new LanguagePackFile { Path = pair.Key, Size = pair.Value.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant() }).ToList()
            };
            var path = Path.Combine(BundledDirectory, fileName);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, bytes) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
            using var manifestStream = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(manifestStream, manifest, LanguagePackManifest.JsonOptions);
            return path;
        }

        public void Dispose()
        {
            var resolvedRoot = Path.GetFullPath(RootDirectory);
            if (!resolvedRoot.StartsWith(_temporaryBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("UI fixture cleanup must stay within its temporary base.");
            if (Directory.Exists(resolvedRoot)) Directory.Delete(resolvedRoot, true);
        }
    }

    private sealed class CountingDownloadHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new HttpRequestException("An ordinary UI selection must not send a download request.");
        }
    }

    private static void Layout(FrameworkElement host, int width, int height)
    {
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        host.UpdateLayout();
    }

    private static void SaveImage(FrameworkElement host, string name, int width = 900, int height = 720)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ToolKeeper.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, "artifacts", "translamp-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name));
        encoder.Save(file);
    }

    private static Task OnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "TransLamp UI verification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }
}
