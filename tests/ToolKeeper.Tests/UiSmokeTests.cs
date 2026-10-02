using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ToolKeeper.Services;
using ToolKeeper.Modules;
using ToolKeeper.UI;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class UiSmokeTests
{
    private const string AbcMd5 = "900150983cd24fb0d6963f7d28e17f72";
    private const string AbcSha1 = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public Task LoadingAFileShowsAllHashesAndUpdatingExpectedHashRechecksIt() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var path = fixture.WriteText("abc.txt", "abc");
        var window = CreateHashWindow();
        try
        {
            Assert.False(Get<Button>(window, "CopyHashesButton").IsEnabled);
            await window.LoadHashFileAsync(path);
            AssertAbcHashes(window);
            Assert.True(Get<Button>(window, "CopyHashesButton").IsEnabled);
            Assert.True(Get<Button>(window, "ChooseHashButton").IsEnabled);
            AssertCancelUnavailable(window, "CancelHashButton");

            var expected = Get<TextBox>(window, "ExpectedHash");
            var comparison = Get<TextBlock>(window, "HashComparison");
            expected.Text = AbcMd5;
            AssertPositiveMatch(comparison.Text);
            expected.Text = AbcSha1.ToUpperInvariant();
            AssertPositiveMatch(comparison.Text);
            expected.Text = "  " + AbcSha256.ToUpperInvariant() + "  ";
            AssertPositiveMatch(comparison.Text);

            expected.Text = new string('0', 64);
            AssertNegativeMatch(comparison.Text);
            expected.Text = AbcSha256;
            AssertPositiveMatch(comparison.Text);
            Assert.Equal(Encoding.UTF8.GetBytes("abc"), File.ReadAllBytes(path));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AFailedHashLoadClearsPreviousHashesAndComparison() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var window = CreateHashWindow();
        try
        {
            await window.LoadHashFileAsync(fixture.WriteText("abc.txt", "abc"));
            Get<TextBox>(window, "ExpectedHash").Text = AbcSha256;
            AssertPositiveMatch(Get<TextBlock>(window, "HashComparison").Text);

            await window.LoadHashFileAsync(fixture.FilePath("does-not-exist.bin"));
            foreach (var name in new[] { "Md5Output", "Sha1Output", "Sha256Output" })
                Assert.Empty(Get<TextBox>(window, name).Text);
            Assert.False(Get<Button>(window, "CopyHashesButton").IsEnabled);
            Assert.True(Get<Button>(window, "ChooseHashButton").IsEnabled);
            AssertCancelUnavailable(window, "CancelHashButton");
            Assert.NotEmpty(Get<TextBlock>(window, "HashStatus").Text);
            Assert.False(IsPositiveMatch(Get<TextBlock>(window, "HashComparison").Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReplacingAHashRequestCannotRestoreThePreviousFileResult() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var largePath = fixture.FilePath("older-request.bin");
        using (var stream = File.Create(largePath)) stream.SetLength(32 * 1024 * 1024);
        var window = CreateHashWindow();
        try
        {
            var oldRequest = window.LoadHashFileAsync(largePath);
            var latestRequest = window.LoadHashFileAsync(fixture.WriteText("latest-request.txt", "abc"));
            await Task.WhenAll(oldRequest, latestRequest);
            // Let posted progress from the superseded operation run as well.
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            AssertAbcHashes(window);
            Assert.True(Get<Button>(window, "CopyHashesButton").IsEnabled);
            Assert.True(Get<Button>(window, "ChooseHashButton").IsEnabled);
            AssertCancelUnavailable(window, "CancelHashButton");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task CancellingAHashRequestLeavesNoPartialOrPreviouslyCopyableHash() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var largePath = fixture.FilePath("cancelled.bin");
        using (var stream = File.Create(largePath)) stream.SetLength(32 * 1024 * 1024);
        var window = CreateHashWindow();
        try
        {
            await window.LoadHashFileAsync(fixture.WriteText("previous.txt", "abc"));
            var request = window.LoadHashFileAsync(largePath);
            Get<Button>(window, "CancelHashButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await request;
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            foreach (var name in new[] { "Md5Output", "Sha1Output", "Sha256Output" })
                Assert.Empty(Get<TextBox>(window, name).Text);
            Assert.False(Get<Button>(window, "CopyHashesButton").IsEnabled);
            Assert.True(Get<Button>(window, "ChooseHashButton").IsEnabled);
            AssertCancelUnavailable(window, "CancelHashButton");
            Assert.Contains("取消", Get<TextBlock>(window, "HashStatus").Text);
            Assert.Equal(0, Get<ProgressBar>(window, "HashProgress").Value);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MixedImageBatchRetainsSuccessesAndPreservesSourcesAndExistingIcons() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var imagePath = fixture.WritePng("sample.png");
        var originalImage = File.ReadAllBytes(imagePath);
        var existingIcon = fixture.WriteText("sample.ico", "Existing icon must not be replaced.");
        var existingBytes = File.ReadAllBytes(existingIcon);
        var invalidImage = fixture.WriteText("damaged.png", "This is not an image.");
        var window = CreateIconWindow();
        try
        {
            await window.ConvertImagesAsync(new[] { imagePath, invalidImage });
            var results = Get<ListBox>(window, "IconResults").Items.Cast<IconConversionItem>().ToArray();
            Assert.Equal(2, results.Length);
            var success = Assert.Single(results, item => item.Success);
            var failure = Assert.Single(results, item => !item.Success);
            Assert.Equal("sample.png", success.FileName);
            Assert.Equal("damaged.png", failure.FileName);
            Assert.NotEmpty(failure.Message);
            var outputPath = success.OutputPath;
            Assert.True(File.Exists(outputPath));
            Assert.EndsWith(".ico", outputPath, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(existingIcon, outputPath);
            using (var stream = File.OpenRead(outputPath))
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.NotEmpty(decoder.Frames);
                Assert.All(decoder.Frames, frame => Assert.Equal(frame.PixelWidth, frame.PixelHeight));
            }
            Assert.Equal(originalImage, File.ReadAllBytes(imagePath));
            Assert.Equal(existingBytes, File.ReadAllBytes(existingIcon));
            Assert.Equal("This is not an image.", File.ReadAllText(invalidImage));
            Assert.True(Get<Button>(window, "ChooseImagesButton").IsEnabled);
            AssertCancelUnavailable(window, "CancelIconsButton");
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task RoutedFileDropsDeclareCopyBeforeAsyncWorkAndPreserveTheirSources() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var hashPath = fixture.WriteText("dropped.txt", "abc");
        var imagePath = fixture.WritePng("dropped.png");
        var imageBytes = File.ReadAllBytes(imagePath);
        var hash = CreateHashWindow();
        var icon = CreateIconWindow();
        try
        {
            var hashTarget = Get<Border>(hash, "HashDropZone");
            var hashData = new DataObject(DataFormats.FileDrop, new[] { hashPath });
            Assert.Equal(DragDropEffects.Copy,
                RaiseDrag(hashTarget, DragDrop.PreviewDragEnterEvent, hashData, DragDropEffects.Copy | DragDropEffects.Move).Effects);
            var hashDrop = RaiseDrag(hashTarget, DragDrop.PreviewDropEvent, hashData, DragDropEffects.Copy | DragDropEffects.Move);
            Assert.Equal(DragDropEffects.Copy, hashDrop.Effects);
            Assert.True(hashDrop.Handled);
            await WaitUntilAsync(() => Get<Button>(hash, "CopyHashesButton").IsEnabled);
            AssertAbcHashes(hash);

            var iconTarget = Get<Border>(icon, "IconDropZone");
            var imageData = new DataObject(DataFormats.FileDrop, new[] { imagePath });
            Assert.Equal(DragDropEffects.Copy,
                RaiseDrag(iconTarget, DragDrop.PreviewDragEnterEvent, imageData, DragDropEffects.Copy | DragDropEffects.Move).Effects);
            var iconDrop = RaiseDrag(iconTarget, DragDrop.PreviewDropEvent, imageData, DragDropEffects.Copy | DragDropEffects.Move);
            Assert.Equal(DragDropEffects.Copy, iconDrop.Effects);
            Assert.True(iconDrop.Handled);
            await WaitUntilAsync(() => Get<Button>(icon, "ChooseImagesButton").IsEnabled);
            var result = Assert.Single(Get<ListBox>(icon, "IconResults").Items.Cast<IconConversionItem>());
            Assert.True(result.Success);
            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("abc", File.ReadAllText(hashPath));
            Assert.Equal(imageBytes, File.ReadAllBytes(imagePath));
        }
        finally { hash.Close(); icon.Close(); }
    });

    [Fact]
    public Task MoveOnlyAndInvalidDropsAreRejectedWithoutStartingWork() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var hashPath = fixture.WriteText("rejected.txt", "abc");
        var imagePath = fixture.WritePng("rejected.png");
        var imageBytes = File.ReadAllBytes(imagePath);
        var hash = CreateHashWindow();
        var icon = CreateIconWindow();
        try
        {
            var hashTarget = Get<Border>(hash, "HashDropZone");
            var iconTarget = Get<Border>(icon, "IconDropZone");
            var rejected = new[]
            {
                (hashTarget, new DataObject(DataFormats.FileDrop, new[] { hashPath }), DragDropEffects.Move),
                (iconTarget, new DataObject(DataFormats.FileDrop, new[] { imagePath }), DragDropEffects.Move),
                (hashTarget, new DataObject(DataFormats.UnicodeText, "text is not a file drop"), DragDropEffects.Copy | DragDropEffects.Move),
                (iconTarget, new DataObject(DataFormats.UnicodeText, "text is not a file drop"), DragDropEffects.Copy | DragDropEffects.Move),
                (hashTarget, new DataObject(DataFormats.FileDrop, new[] { hashPath, imagePath }), DragDropEffects.Copy | DragDropEffects.Move)
            };
            foreach (var (target, data, effects) in rejected)
            {
                Assert.Equal(DragDropEffects.None,
                    RaiseDrag(target, DragDrop.PreviewDragEnterEvent, data, effects).Effects);
                var drop = RaiseDrag(target, DragDrop.PreviewDropEvent, data, effects);
                Assert.Equal(DragDropEffects.None, drop.Effects);
                Assert.True(drop.Handled);
                AssertCancelUnavailable(hash, "CancelHashButton");
                AssertCancelUnavailable(icon, "CancelIconsButton");
                Assert.True(Get<Button>(icon, "ChooseImagesButton").IsEnabled);
            }
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.Empty(Get<TextBox>(hash, "Sha256Output").Text);
            Assert.False(Get<Button>(hash, "CopyHashesButton").IsEnabled);
            Assert.Empty(Get<ListBox>(icon, "IconResults").Items.Cast<object>());
            Assert.False(File.Exists(fixture.FilePath("rejected.ico")));
            Assert.Equal("abc", File.ReadAllText(hashPath));
            Assert.Equal(imageBytes, File.ReadAllBytes(imagePath));
        }
        finally { hash.Close(); icon.Close(); }
    });

    [Fact]
    public Task BusyIconDropIsRejectedAndDoesNotConvertTheAdditionalImage() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var acceptedPath = fixture.WritePng("accepted.png");
        var rejectedPath = fixture.WritePng("busy-rejected.png");
        var rejectedBytes = File.ReadAllBytes(rejectedPath);
        var window = CreateIconWindow();
        try
        {
            var conversion = window.ConvertImagesAsync(new[] { acceptedPath });
            Assert.False(Get<Button>(window, "ChooseImagesButton").IsEnabled);
            var target = Get<Border>(window, "IconDropZone");
            var data = new DataObject(DataFormats.FileDrop, new[] { rejectedPath });
            Assert.Equal(DragDropEffects.None,
                RaiseDrag(target, DragDrop.PreviewDragEnterEvent, data, DragDropEffects.Copy | DragDropEffects.Move).Effects);
            var drop = RaiseDrag(target, DragDrop.PreviewDropEvent, data, DragDropEffects.Copy | DragDropEffects.Move);
            Assert.Equal(DragDropEffects.None, drop.Effects);
            Assert.True(drop.Handled);
            await conversion;
            var result = Assert.Single(Get<ListBox>(window, "IconResults").Items.Cast<IconConversionItem>());
            Assert.Equal(acceptedPath, result.SourcePath);
            Assert.True(result.Success);
            Assert.False(File.Exists(fixture.FilePath("busy-rejected.ico")));
            Assert.Equal(rejectedBytes, File.ReadAllBytes(rejectedPath));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SeparateModulesRenderOnlyTheirOwnToolInEmptyAndCompletedStates() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var hash = CreateHashWindow();
        var icon = CreateIconWindow();
        try
        {
            Assert.Equal("Hash Checker - File Hash Verification", hash.Title);
            Assert.Equal("Image → ICO - Icon Converter", icon.Title);
            Assert.Null(hash.FindName("IconDropZone"));
            Assert.Null(hash.FindName("IconResults"));
            Assert.Null(icon.FindName("HashDropZone"));
            Assert.Null(icon.FindName("ExpectedHash"));
            var hashHost = HostWindowContent(hash);
            var iconHost = HostWindowContent(icon);
            RenderModuleAtBothSizes(hashHost, hash, "hash-empty");
            RenderModuleAtBothSizes(iconHost, icon, "ico-empty");
            await hash.LoadHashFileAsync(fixture.WriteText("範例檔案 abc.txt", "abc"));
            Get<TextBox>(hash, "ExpectedHash").Text = AbcSha256;
            await icon.ConvertImagesAsync(new[] { fixture.WritePng("範例圖片.png"), fixture.WriteText("無法轉換.png", "Invalid image data.") });
            RenderModuleAtBothSizes(hashHost, hash, "hash-results");
            RenderModuleAtBothSizes(iconHost, icon, "ico-results");
            Assert.False(hash.IsVisible);
            Assert.False(icon.IsVisible);
            Assert.Null(PresentationSource.FromVisual(hash));
            Assert.Null(PresentationSource.FromVisual(icon));
        }
        finally { hash.Close(); icon.Close(); }
    });

    [Fact]
    public Task SharedShellKeepsIdentityAndContentInSync() => OnSta(() =>
    {
        var context = new object();
        var content = new TextBox();
        var action = new Button { Content = "Action" };
        var window = new ToolKeeper.UI.AppWindow
        {
            MainName = "Example", SubName = "Utility", Description = "First description",
            DataContext = context, Workspace = content, HeaderActions = action
        };
        try
        {
            Assert.Equal("Example - Utility", window.Title);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            Assert.Same(context, content.DataContext);
            Assert.Same(context, action.DataContext);

            window.MainName = "Renamed";
            window.SubName = "Updated utility";
            window.Description = "Updated description";
            Assert.Equal("Renamed - Updated utility", window.Title);
            var host = HostWindowContent(window);
            Assert.Contains(VisualDescendants<TextBlock>(host), text => text.Text == "Renamed - Updated utility");
            Assert.Contains(VisualDescendants<TextBlock>(host), text => text.Text == "Updated description");
            Assert.DoesNotContain(VisualDescendants<TextBlock>(host), text => text.Text == "First description");

            var replacement = new TextBox();
            window.Workspace = replacement;
            window.HeaderActions = null;
            Assert.Null(LogicalTreeHelper.GetParent(content));
            Assert.Null(LogicalTreeHelper.GetParent(action));
            Assert.NotNull(LogicalTreeHelper.GetParent(replacement));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SwitchingPreferencesUpdatesEachModuleWithoutReplacingResultsOrInput() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var hash = CreateHashWindow();
        var icon = CreateIconWindow();
        try
        {
            await hash.LoadHashFileAsync(fixture.WriteText("abc.txt", "abc"));
            var expected = Get<TextBox>(hash, "ExpectedHash");
            expected.Text = AbcSha256;
            expected.Select(3, 12);
            await icon.ConvertImagesAsync(new[] { fixture.WritePng("source.png"), fixture.WriteText("bad.png", "bad") });
            var source = Get<ListBox>(icon, "IconResults").ItemsSource;
            var items = Get<ListBox>(icon, "IconResults").Items.Cast<IconConversionItem>().ToArray();
            var hashWorkspace = hash.Workspace;
            var iconWorkspace = icon.Workspace;
            foreach (var language in new[] { "en", "ja", "zh-TW" })
            foreach (var theme in new[] { "Light", "Dark", "Ink", "InkDark", "System" })
            {
                hash.SelectedLanguage = icon.SelectedLanguage = language;
                hash.SelectedTheme = icon.SelectedTheme = theme;
                Assert.Same(hashWorkspace, hash.Workspace);
                Assert.Same(iconWorkspace, icon.Workspace);
                Assert.Same(source, Get<ListBox>(icon, "IconResults").ItemsSource);
                Assert.Same(items[0], Get<ListBox>(icon, "IconResults").Items[0]);
                Assert.Same(items[1], Get<ListBox>(icon, "IconResults").Items[1]);
                AssertAbcHashes(hash);
                Assert.Equal(AbcSha256, expected.Text);
                Assert.Equal(3, expected.SelectionStart);
                Assert.Equal(12, expected.SelectionLength);
                Assert.True(Get<Button>(hash, "CopyHashesButton").IsEnabled);
                Assert.Equal("abc.txt", Get<TextBlock>(hash, "HashFileName").Text);
                Assert.Equal(hash.FindResource("PaperBackgroundBrush"), Get<Border>(hash, "HashDropZone").Background);
                Assert.Equal(icon.FindResource("PaperBackgroundBrush"), Get<Border>(icon, "IconDropZone").Background);
                Assert.Equal(hash.FindResource("TextBrush"), expected.Foreground);
                Assert.Equal(hash.FindResource("SuccessBrush"), Get<TextBlock>(hash, "HashComparison").Foreground);
                Assert.Equal(UiLanguage.Text(language, "Choose images", "選擇圖片", "画像を選択"), Get<Button>(icon, "ChooseImagesButton").Content);
                Assert.Equal(UiLanguage.Text(language, "✓ Converted", "✓ 轉換完成", "✓ 変換完了"), items[0].Message);
                Assert.StartsWith(UiLanguage.Text(language, "Unable to convert: ", "無法轉換：", "変換できません："), items[1].Message);
                Assert.StartsWith(UiLanguage.Text(language, "Done", "完成", "完了"), Get<TextBlock>(hash, "HashStatus").Text);
                Assert.StartsWith(UiLanguage.Text(language, "Done", "完成", "完了"), Get<TextBlock>(icon, "IconStatus").Text);
            }
            Assert.True(File.Exists(items[0].OutputPath));
        }
        finally { hash.Close(); icon.Close(); }
    });

    [Fact]
    public Task SwitchingPreferencesDuringWorkKeepsCancellationAndCompletesTheSameRequests() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var path = fixture.FilePath("large.bin");
        using (var stream = File.Create(path)) stream.SetLength(32 * 1024 * 1024);
        var hash = CreateHashWindow();
        var icon = CreateIconWindow();
        try
        {
            var hashWork = hash.LoadHashFileAsync(path);
            var iconWork = icon.ConvertImagesAsync(new[] { fixture.WritePng("source.png") });
            hash.SelectedTheme = icon.SelectedTheme = "InkDark";
            hash.SelectedLanguage = icon.SelectedLanguage = "en";
            Assert.True(Get<Button>(hash, "CancelHashButton").IsEnabled);
            Assert.True(Get<Button>(icon, "CancelIconsButton").IsEnabled);
            Assert.False(Get<Button>(icon, "ChooseImagesButton").IsEnabled);
            Assert.StartsWith("Calculating", Get<TextBlock>(hash, "HashStatus").Text);
            Assert.StartsWith("Converting", Get<TextBlock>(icon, "IconStatus").Text);
            await Task.WhenAll(hashWork, iconWork);
            Assert.NotEmpty(Get<TextBox>(hash, "Sha256Output").Text);
            Assert.True(Assert.Single(Get<ListBox>(icon, "IconResults").Items.Cast<IconConversionItem>()).Success);
            Assert.StartsWith("Done", Get<TextBlock>(hash, "HashStatus").Text);
            Assert.StartsWith("Done", Get<TextBlock>(icon, "IconStatus").Text);
            AssertCancelUnavailable(hash, "CancelHashButton");
            AssertCancelUnavailable(icon, "CancelIconsButton");
        }
        finally { hash.Close(); icon.Close(); }
    });

    [Theory]
    [InlineData("en", "Dark")]
    [InlineData("ja", "InkDark")]
    [InlineData("zh-TW", "Ink")]
    public Task LocalizedModuleWindowsRetainTheirMinimumLayout(string language, string theme) => OnSta(() =>
    {
        foreach (var window in new AppWindow[] { CreateHashWindow(), CreateIconWindow() })
        {
            try
            {
                window.SelectedTheme = theme;
                window.SelectedLanguage = language;
                RenderModuleAtBothSizes(HostWindowContent(window), window, window is HashCheckerWindow ? "hash-" + language + "-" + theme : "ico-" + language + "-" + theme);
            }
            finally { window.Close(); }
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("en", "Dark")]
    [InlineData("ja", "InkDark")]
    [InlineData("zh-TW", "Ink")]
    public Task LauncherShowsAllModuleEntriesAtMinimumSize(string language, string theme) => OnSta(() =>
    {
        var window = new MainWindow { PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false, SelectedTheme = theme, SelectedLanguage = language };
        try
        {
            Assert.Null(window.FindName("HashDropZone"));
            Assert.Null(window.FindName("IconDropZone"));
            window.SetProducts(ProductCatalogService.Definitions.Select(product =>
                new ProductStatus(product, product.Id == "001" ? ProductAvailability.Get : ProductAvailability.Available,
                    product.ActivationUri)).ToArray());
            window.SetPlatformStatus("Example status: an application could not be opened. Try again after checking its installation.");
            var host = HostWindowContent(window);
            foreach (var (width, height, suffix) in new[] { (820, 620, "default"), (720, 500, "minimum-client") })
            {
                Render(host, width, height, $"launcher-{language}-{theme}-{suffix}.png");
                AssertCommonShell(host, window);
                foreach (var name in new[] { "PlatformStatus", "ToolList" })
                    AssertVisibleBounds(Get<FrameworkElement>(window, name), host, width, height);
                var products = Get<ItemsControl>(window, "Products");
                Assert.Equal(new[] { "001", "002", "003", "004", "005", "006" }, products.Items.Cast<object>().Select(item => ReadProperty<string>(item, "Id")));
                var scroller = VisualDescendants<ScrollViewer>(Get<Border>(window, "ToolList")).First();
                scroller.ScrollToBottom();
                host.UpdateLayout();
                var last = Assert.IsAssignableFrom<FrameworkElement>(products.ItemContainerGenerator.ContainerFromIndex(products.Items.Count - 1));
                Assert.True(Bounds(last, host).Bottom <= Bounds(scroller, host).Bottom + 1);
                Assert.All(VisualDescendants<Button>(products), action => Assert.True(action.IsEnabled));
                scroller.ScrollToTop();
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HostCancellingWindowCloseKeepsTheRunningUtilityAvailable() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var path = fixture.FilePath("continue-after-hide.bin");
        using (var stream = File.Create(path)) stream.SetLength(32 * 1024 * 1024);
        var window = CreateHashWindow();
        System.ComponentModel.CancelEventHandler hideToTray = (_, args) => args.Cancel = true;
        window.Closing += hideToTray;
        try
        {
            var work = window.LoadHashFileAsync(path);
            window.Close();
            await work;
            Assert.NotEmpty(Get<TextBox>(window, "Sha256Output").Text);
            await window.LoadHashFileAsync(fixture.WriteText("after-hide.txt", "abc"));
            AssertAbcHashes(window);
        }
        finally
        {
            window.Closing -= hideToTray;
            window.Close();
        }
    });

    private static void AssertAbcHashes(HashCheckerWindow window)
    {
        Assert.Equal(AbcMd5, Get<TextBox>(window, "Md5Output").Text.ToLowerInvariant());
        Assert.Equal(AbcSha1, Get<TextBox>(window, "Sha1Output").Text.ToLowerInvariant());
        Assert.Equal(AbcSha256, Get<TextBox>(window, "Sha256Output").Text.ToLowerInvariant());
    }

    private static void AssertCancelUnavailable(Window window, string name)
    {
        var button = Get<Button>(window, name);
        Assert.True(!button.IsEnabled || button.Visibility != Visibility.Visible,
            "Cancellation must not be offered after the operation finishes.");
    }

    private static bool IsPositiveMatch(string text) =>
        (text.Contains("相符", StringComparison.Ordinal) || text.Contains("符合", StringComparison.Ordinal)
         || text.Contains("一致", StringComparison.Ordinal))
        && !text.Contains("不", StringComparison.Ordinal);

    private static void AssertPositiveMatch(string text) =>
        Assert.True(IsPositiveMatch(text), $"Expected a matching-hash message, received: {text}");

    private static void AssertNegativeMatch(string text) =>
        Assert.True(text.Contains("不", StringComparison.Ordinal) &&
                    (text.Contains("相符", StringComparison.Ordinal) || text.Contains("符合", StringComparison.Ordinal)
                     || text.Contains("一致", StringComparison.Ordinal)),
            $"Expected a mismatching-hash message, received: {text}");

    private static T ReadProperty<T>(object item, string name) =>
        Assert.IsAssignableFrom<T>(item.GetType().GetProperty(name)?.GetValue(item));

    private static T Get<T>(Window window, string name) where T : FrameworkElement =>
        Assert.IsAssignableFrom<T>(window.FindName(name));

    private static DragEventArgs RaiseDrag(FrameworkElement target, RoutedEvent routedEvent,
        IDataObject data, DragDropEffects allowedEffects)
    {
        // WPF exposes routed drag events but keeps their argument constructor internal.
        // Constructing only the event arguments avoids native drag sessions and desktop use.
        var constructor = typeof(DragEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)],
            modifiers: null);
        Assert.NotNull(constructor);
        var args = Assert.IsType<DragEventArgs>(constructor.Invoke(
            [data, DragDropKeyStates.None, allowedEffects, target, new Point(4, 4)]));
        args.RoutedEvent = routedEvent;
        args.Effects = allowedEffects;
        target.RaiseEvent(args);
        return args;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "The routed drop operation did not finish before the timeout.");
    }

    private static HashCheckerWindow CreateHashWindow() => new()
    {
        ShowActivated = false, ShowInTaskbar = false, PreferencesPath = null, SelectedLanguage = "zh-TW"
    };

    private static ImageToIcoWindow CreateIconWindow() => new()
    {
        ShowActivated = false, ShowInTaskbar = false, PreferencesPath = null, SelectedLanguage = "zh-TW"
    };

    private static FrameworkElement HostWindowContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Background = window.Background, Child = content, Resources = window.Resources };
        TextElement.SetFontFamily(host, window.FontFamily);
        TextElement.SetFontSize(host, window.FontSize);
        TextElement.SetForeground(host, window.Foreground);
        return host;
    }

    private static void RenderModuleAtBothSizes(FrameworkElement host, AppWindow window, string state)
    {
        foreach (var (width, height, suffix) in new[] { (760, 660, "default"), (660, 560, "minimum-client") })
        {
            Render(host, width, height, $"{state}-{suffix}.png");
            AssertCommonShell(host, window);
            var controls = window is HashCheckerWindow
                ? new[] { "HashDropZone", "ChooseHashButton", "Md5Output", "Sha1Output", "Sha256Output", "CopyHashesButton", "PrivacyNotice" }
                : new[] { "IconDropZone", "ChooseImagesButton", "IconStatus", "PrivacyNotice", Get<Border>(window, "IconEmptyState").Visibility == Visibility.Visible ? "IconEmptyState" : "IconResults" };
            foreach (var name in controls) AssertVisibleBounds(Get<FrameworkElement>(window, name), host, width, height);
            if (window is HashCheckerWindow)
            {
                var scroller = VisualDescendants<ScrollViewer>(Get<Border>(window, "HashDropZone")).First();
                scroller.ScrollToBottom();
                host.UpdateLayout();
                var viewport = Bounds(scroller, host);
                foreach (var name in new[] { "ExpectedHash", "HashComparison", "HashStatus" })
                {
                    var bounds = Bounds(Get<FrameworkElement>(window, name), host);
                    Assert.True(bounds.Height > 0);
                    Assert.InRange(bounds.Top, viewport.Top - 0.5, viewport.Bottom);
                    Assert.InRange(bounds.Bottom, viewport.Top, viewport.Bottom + 0.5);
                }
                scroller.ScrollToTop();
                host.UpdateLayout();
            }
            else if (Get<Border>(window, "IconEmptyState").Visibility == Visibility.Visible)
            {
                var empty = Get<Border>(window, "IconEmptyState");
                foreach (var label in VisualDescendants<TextBlock>(empty))
                    Assert.True(Bounds(label, host).Bottom <= Bounds(empty, host).Bottom - empty.Padding.Bottom + 0.5,
                        "ICO drop instructions must fit without clipping.");
            }
        }
    }

    private static void AssertCommonShell(FrameworkElement host, AppWindow window)
    {
        var heading = VisualDescendants<TextBlock>(host).Single(text => text.Name == "WindowTitleText");
        var description = VisualDescendants<TextBlock>(host).Single(text => text.Name == "ProductDescription");
        Assert.Equal(window.Title, heading.Text);
        Assert.Equal(window.Description, description.Text);
        var headingBounds = Bounds(heading, host);
        var descriptionBounds = Bounds(description, host);
        Assert.InRange(headingBounds.Top, 1, 40);
        Assert.True(headingBounds.Bottom <= descriptionBounds.Top);
        var workspaceBounds = Bounds(Assert.IsAssignableFrom<FrameworkElement>(window.Workspace), host);
        Assert.True(workspaceBounds.Top >= descriptionBounds.Bottom);
        if (window.HeaderActions is FrameworkElement actions)
        {
            var actionBounds = Bounds(actions, host);
            Assert.False(headingBounds.IntersectsWith(actionBounds));
            Assert.False(descriptionBounds.IntersectsWith(actionBounds));
            Assert.True(workspaceBounds.Top >= actionBounds.Bottom);
        }
    }

    private static void AssertVisibleBounds(FrameworkElement element, FrameworkElement host, int width, int height)
    {
        var bounds = Bounds(element, host);
        Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{element.Name} must remain rendered.");
        Assert.InRange(bounds.Left, -0.5, width);
        Assert.InRange(bounds.Top, -0.5, height);
        Assert.InRange(bounds.Right, 0, width + 0.5);
        Assert.InRange(bounds.Bottom, 0, height + 0.5);
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement host) =>
        element.TransformToAncestor(host).TransformBounds(new Rect(new Point(), element.RenderSize));

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void Render(FrameworkElement host, int width, int height, string name)
    {
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ToolKeeper.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, "artifacts", "toolkeeper-ui");
        Directory.CreateDirectory(directory);
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
                catch (Exception exception) { completion.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "ToolKeeper hidden UI smoke" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "ToolKeeper.UiTests", Guid.NewGuid().ToString("N"));

        public Fixture() => Directory.CreateDirectory(directory);
        public string FilePath(string name) => Path.Combine(directory, name);

        public string WriteText(string name, string text)
        {
            var path = FilePath(name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        public string WritePng(string name)
        {
            const int width = 48;
            const int height = 32;
            var pixels = new byte[width * height * 4];
            for (var index = 0; index < pixels.Length; index += 4)
            {
                pixels[index] = 160;
                pixels[index + 1] = 125;
                pixels[index + 2] = 30;
                pixels[index + 3] = 255;
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = FilePath(name);
            using var stream = File.Create(path);
            encoder.Save(stream);
            return path;
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
