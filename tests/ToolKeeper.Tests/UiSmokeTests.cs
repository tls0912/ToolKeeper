using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        var window = CreateHiddenWindow();
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
        var window = CreateHiddenWindow();
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
        var window = CreateHiddenWindow();
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
        var window = CreateHiddenWindow();
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
        var window = CreateHiddenWindow();
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
        var window = CreateHiddenWindow();
        try
        {
            var hashTarget = Get<Border>(window, "HashDropZone");
            var hashData = new DataObject(DataFormats.FileDrop, new[] { hashPath });
            Assert.Equal(DragDropEffects.Copy,
                RaiseDrag(hashTarget, DragDrop.PreviewDragEnterEvent, hashData, DragDropEffects.Copy | DragDropEffects.Move).Effects);
            Assert.False(Get<Button>(window, "CopyHashesButton").IsEnabled);
            var hashDrop = RaiseDrag(hashTarget, DragDrop.PreviewDropEvent, hashData, DragDropEffects.Copy | DragDropEffects.Move);
            // The source receives the final effect as soon as the routed event returns,
            // before the async handler finishes; Move could make it delete the source.
            Assert.Equal(DragDropEffects.Copy, hashDrop.Effects);
            Assert.True(hashDrop.Handled);
            await WaitUntilAsync(() => Get<Button>(window, "CopyHashesButton").IsEnabled);
            AssertAbcHashes(window);

            var iconTarget = Get<Border>(window, "IconDropZone");
            var imageData = new DataObject(DataFormats.FileDrop, new[] { imagePath });
            Assert.Equal(DragDropEffects.Copy,
                RaiseDrag(iconTarget, DragDrop.PreviewDragEnterEvent, imageData, DragDropEffects.Copy | DragDropEffects.Move).Effects);
            Assert.Empty(Get<ListBox>(window, "IconResults").Items.Cast<object>());
            var iconDrop = RaiseDrag(iconTarget, DragDrop.PreviewDropEvent, imageData, DragDropEffects.Copy | DragDropEffects.Move);
            Assert.Equal(DragDropEffects.Copy, iconDrop.Effects);
            Assert.True(iconDrop.Handled);
            await WaitUntilAsync(() => Get<Button>(window, "ChooseImagesButton").IsEnabled);
            var result = Assert.Single(Get<ListBox>(window, "IconResults").Items.Cast<IconConversionItem>());
            Assert.True(result.Success);
            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal("abc", File.ReadAllText(hashPath));
            Assert.Equal(imageBytes, File.ReadAllBytes(imagePath));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task MoveOnlyAndInvalidDropsAreRejectedWithoutStartingWork() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var hashPath = fixture.WriteText("rejected.txt", "abc");
        var imagePath = fixture.WritePng("rejected.png");
        var imageBytes = File.ReadAllBytes(imagePath);
        var window = CreateHiddenWindow();
        try
        {
            var hashTarget = Get<Border>(window, "HashDropZone");
            var iconTarget = Get<Border>(window, "IconDropZone");
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
                AssertCancelUnavailable(window, "CancelHashButton");
                AssertCancelUnavailable(window, "CancelIconsButton");
                Assert.True(Get<Button>(window, "ChooseImagesButton").IsEnabled);
            }
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Assert.Empty(Get<TextBox>(window, "Sha256Output").Text);
            Assert.False(Get<Button>(window, "CopyHashesButton").IsEnabled);
            Assert.Empty(Get<ListBox>(window, "IconResults").Items.Cast<object>());
            Assert.False(File.Exists(fixture.FilePath("rejected.ico")));
            Assert.Equal("abc", File.ReadAllText(hashPath));
            Assert.Equal(imageBytes, File.ReadAllBytes(imagePath));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task BusyIconDropIsRejectedAndDoesNotConvertTheAdditionalImage() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var acceptedPath = fixture.WritePng("accepted.png");
        var rejectedPath = fixture.WritePng("busy-rejected.png");
        var rejectedBytes = File.ReadAllBytes(rejectedPath);
        var window = CreateHiddenWindow();
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
    public Task EmptyAndCompletedStatesRenderWithTwoTopPanelsAndOneFullWidthToolList() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var window = CreateHiddenWindow();
        try
        {
            Assert.Equal(1100, window.Width);
            Assert.Equal(850, window.Height);
            Assert.Equal(860, window.MinWidth);
            Assert.Equal(740, window.MinHeight);
            Assert.IsAssignableFrom<ToolKeeper.UI.AppWindow>(window);
            Assert.Equal("ToolKeeper - 工具番", window.Title);
            var host = HostWindowContent(window);
            RenderAtBothSizes(host, window, "empty");
            var products = Get<ItemsControl>(window, "Products");
            Assert.NotEmpty(products.Items.Cast<object>());
            for (var index = 0; index < products.Items.Count; index++)
            {
                var row = Assert.IsAssignableFrom<FrameworkElement>(products.ItemContainerGenerator.ContainerFromIndex(index));
                var action = Assert.Single(VisualDescendants<Button>(row));
                var name = Assert.Single(VisualDescendants<TextBlock>(row),
                    text => text.Text == ReadProperty<string>(products.Items[index], "Name"));
                Assert.False(action.IsEnabled);
                Assert.True(Bounds(name, host).Right <= Bounds(action, host).Left + 1,
                    "Each catalog row must show the product name on the left and its deferred action on the right.");
            }

            await window.LoadHashFileAsync(fixture.WriteText("範例檔案 abc.txt", "abc"));
            Get<TextBox>(window, "ExpectedHash").Text = AbcSha256;
            await window.ConvertImagesAsync(new[]
            {
                fixture.WritePng("範例圖片.png"),
                fixture.WriteText("無法轉換.png", "Invalid image data.")
            });
            RenderAtBothSizes(host, window, "results");
            Assert.False(window.IsVisible);
            Assert.Null(PresentationSource.FromVisual(window));
        }
        finally { window.Close(); }
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
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
            Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
            Assert.Same(context, content.DataContext);
            Assert.Same(context, action.DataContext);

            window.MainName = "Renamed";
            window.SubName = "Updated utility";
            window.Description = "Updated description";
            Assert.Equal("Renamed - Updated utility", window.Title);
            var host = HostWindowContent(window);
            Assert.Contains(VisualDescendants<TextBlock>(host), text => text.Text == "Renamed");
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
    public Task SwitchingPreferencesUpdatesWorkspaceWithoutReplacingResultsOrInput() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var window = CreateHiddenWindow();
        try
        {
            await window.LoadHashFileAsync(fixture.WriteText("abc.txt", "abc"));
            var expected = Get<TextBox>(window, "ExpectedHash");
            expected.Text = AbcSha256;
            expected.Select(3, 12);
            await window.ConvertImagesAsync(new[] { fixture.WritePng("source.png"), fixture.WriteText("bad.png", "bad") });
            var source = Get<ListBox>(window, "IconResults").ItemsSource;
            var items = Get<ListBox>(window, "IconResults").Items.Cast<IconConversionItem>().ToArray();
            var workspace = window.Workspace;
            var products = Get<ItemsControl>(window, "Products").ItemsSource;

            foreach (var language in new[] { "en", "ja", "zh-TW" })
            foreach (var theme in new[] { "Light", "Dark", "Ink", "InkDark", "System" })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                Assert.Same(workspace, window.Workspace);
                Assert.Same(source, Get<ListBox>(window, "IconResults").ItemsSource);
                Assert.Same(products, Get<ItemsControl>(window, "Products").ItemsSource);
                Assert.Same(items[0], Get<ListBox>(window, "IconResults").Items[0]);
                Assert.Same(items[1], Get<ListBox>(window, "IconResults").Items[1]);
                AssertAbcHashes(window);
                Assert.Equal(AbcSha256, expected.Text);
                Assert.Equal(3, expected.SelectionStart);
                Assert.Equal(12, expected.SelectionLength);
                Assert.True(Get<Button>(window, "CopyHashesButton").IsEnabled);
                Assert.Equal("abc.txt", Get<TextBlock>(window, "HashFileName").Text);
                Assert.Equal(window.FindResource("SurfaceBrush"), Get<Border>(window, "HashDropZone").Background);
                Assert.Equal(window.FindResource("SurfaceBrush"), Get<Border>(window, "ToolList").Background);
                Assert.Equal(window.FindResource("TextBrush"), expected.Foreground);
                Assert.Equal(window.FindResource("SuccessBrush"), Get<TextBlock>(window, "HashComparison").Foreground);
                Assert.Equal(ToolKeeper.UI.UiLanguage.Text(language, "Choose images", "選擇圖片", "画像を選択"), Get<Button>(window, "ChooseImagesButton").Content);
                Assert.Equal(ToolKeeper.UI.UiLanguage.Text(language, "✓ Converted", "✓ 轉換完成", "✓ 変換完了"), items[0].Message);
                Assert.StartsWith(ToolKeeper.UI.UiLanguage.Text(language, "Unable to convert: ", "無法轉換：", "変換できません："), items[1].Message);
                Assert.StartsWith(ToolKeeper.UI.UiLanguage.Text(language, "Done", "完成", "完了"), Get<TextBlock>(window, "HashStatus").Text);
                Assert.StartsWith(ToolKeeper.UI.UiLanguage.Text(language, "Done", "完成", "完了"), Get<TextBlock>(window, "IconStatus").Text);
            }
            Assert.True(File.Exists(items[0].OutputPath));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SwitchingPreferencesDuringWorkKeepsCancellationAndCompletesTheSameRequests() => OnSta(async () =>
    {
        using var fixture = new Fixture();
        var path = fixture.FilePath("large.bin");
        using (var stream = File.Create(path)) stream.SetLength(32 * 1024 * 1024);
        var window = CreateHiddenWindow();
        try
        {
            var hashWork = window.LoadHashFileAsync(path);
            var iconWork = window.ConvertImagesAsync(new[] { fixture.WritePng("source.png") });
            window.SelectedTheme = "InkDark";
            window.SelectedLanguage = "en";
            Assert.True(Get<Button>(window, "CancelHashButton").IsEnabled);
            Assert.True(Get<Button>(window, "CancelIconsButton").IsEnabled);
            Assert.False(Get<Button>(window, "ChooseImagesButton").IsEnabled);
            Assert.StartsWith("Calculating", Get<TextBlock>(window, "HashStatus").Text);
            Assert.StartsWith("Converting", Get<TextBlock>(window, "IconStatus").Text);
            await Task.WhenAll(hashWork, iconWork);
            Assert.NotEmpty(Get<TextBox>(window, "Sha256Output").Text);
            Assert.True(Assert.Single(Get<ListBox>(window, "IconResults").Items.Cast<IconConversionItem>()).Success);
            Assert.StartsWith("Done", Get<TextBlock>(window, "HashStatus").Text);
            Assert.StartsWith("Done", Get<TextBlock>(window, "IconStatus").Text);
            AssertCancelUnavailable(window, "CancelHashButton");
            AssertCancelUnavailable(window, "CancelIconsButton");
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("en", "Dark")]
    [InlineData("ja", "InkDark")]
    [InlineData("zh-TW", "Ink")]
    public Task LocalizedWholeWindowRetainsItsMinimumLayout(string language, string theme) => OnSta(() =>
    {
        var window = CreateHiddenWindow();
        try
        {
            window.SelectedTheme = theme;
            window.SelectedLanguage = language;
            var host = HostWindowContent(window);
            RenderAtBothSizes(host, window, language + "-" + theme);
            Assert.All(VisualDescendants<Button>(Get<ItemsControl>(window, "Products")), action => Assert.False(action.IsEnabled));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static void AssertAbcHashes(MainWindow window)
    {
        Assert.Equal(AbcMd5, Get<TextBox>(window, "Md5Output").Text.ToLowerInvariant());
        Assert.Equal(AbcSha1, Get<TextBox>(window, "Sha1Output").Text.ToLowerInvariant());
        Assert.Equal(AbcSha256, Get<TextBox>(window, "Sha256Output").Text.ToLowerInvariant());
    }

    private static void AssertCancelUnavailable(MainWindow window, string name)
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

    private static T Get<T>(MainWindow window, string name) where T : FrameworkElement =>
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

    private static MainWindow CreateHiddenWindow()
    {
        // Never show/activate a window or create an Application. All render checks use
        // detached WPF content and leave the user's desktop and clipboard untouched.
        var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false, PreferencesPath = null, SelectedLanguage = "zh-TW" };
        Assert.False(window.IsVisible);
        Assert.Null(PresentationSource.FromVisual(window));
        return window;
    }

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

    private static void RenderAtBothSizes(FrameworkElement host, MainWindow window, string state)
    {
        foreach (var (width, height, suffix) in new[] { (1100, 850, "default"), (844, 700, "minimum-client") })
        {
            Render(host, width, height, $"{state}-{suffix}.png");
            var heading = VisualDescendants<TextBlock>(host).Single(text => text.Name == "ProductHeading");
            var description = VisualDescendants<TextBlock>(host).Single(text => text.Name == "ProductDescription");
            Assert.Equal("ToolKeeper", heading.Text);
            Assert.Equal(window.Description, description.Text);
            var headingBounds = Bounds(heading, host);
            Assert.InRange(headingBounds.Top, 0, 0.5);
            var descriptionBounds = Bounds(description, host);
            var actionBounds = Bounds(Assert.IsAssignableFrom<FrameworkElement>(window.HeaderActions), host);
            var workspaceBounds = Bounds(Assert.IsAssignableFrom<FrameworkElement>(window.Workspace), host);
            Assert.True(headingBounds.Bottom <= descriptionBounds.Top);
            Assert.False(headingBounds.IntersectsWith(actionBounds));
            Assert.False(descriptionBounds.IntersectsWith(actionBounds));
            Assert.True(workspaceBounds.Top >= Math.Max(descriptionBounds.Bottom, actionBounds.Bottom));
            var hash = Bounds(Get<FrameworkElement>(window, "HashDropZone"), host);
            var icon = Bounds(Get<FrameworkElement>(window, "IconDropZone"), host);
            var tools = Bounds(Get<Border>(window, "ToolList"), host);
            Assert.True(hash.Width > 300 && icon.Width > 300, "Both drop zones must remain wide enough to use.");
            Assert.InRange(Math.Abs(hash.Top - icon.Top), 0, 1);
            Assert.True(hash.Right <= icon.Left, "Hash belongs at the upper left and ICO at the upper right.");
            Assert.True(tools.Top >= Math.Max(hash.Bottom, icon.Bottom), "The tool list must be below both drop zones.");
            Assert.True(tools.Width >= hash.Width + icon.Width, "The tool list must span both upper panels.");
            Assert.True(tools.Height >= 100, "The lower tool list must retain usable height.");
            if (Get<Border>(window, "IconEmptyState").Visibility == Visibility.Visible)
            {
                var emptyCard = Get<Border>(window, "IconEmptyState");
                var cardBounds = Bounds(emptyCard, host);
                foreach (var label in VisualDescendants<TextBlock>(emptyCard))
                {
                    Assert.True(Bounds(label, host).Bottom <= cardBounds.Bottom - emptyCard.Padding.Bottom + 0.5,
                        "The empty ICO drop instruction must fit without clipping at the minimum window size.");
                }
            }

            foreach (var name in new[]
                     {
                         "HashDropZone", "IconDropZone", "Md5Output", "Sha1Output", "Sha256Output",
                         "ChooseHashButton",
                         "CopyHashesButton", "IconStatus", Get<Border>(window, "IconEmptyState").Visibility == Visibility.Visible ? "IconEmptyState" : "IconResults", "ChooseImagesButton", "ToolList"
                     })
            {
                var control = Get<FrameworkElement>(window, name);
                var bounds = Bounds(control, host);
                Assert.True(bounds.Width > 0 && bounds.Height > 0, $"{name} must retain a rendered size at {width} × {height}.");
                Assert.InRange(bounds.Left, -0.5, width);
                Assert.InRange(bounds.Top, -0.5, height);
                Assert.InRange(bounds.Right, 0, width + 0.5);
                Assert.InRange(bounds.Bottom, 0, height + 0.5);
            }

            var hashScroller = VisualDescendants<ScrollViewer>(Get<Border>(window, "HashDropZone")).First();
            hashScroller.ScrollToBottom();
            host.UpdateLayout();
            var viewport = Bounds(hashScroller, host);
            foreach (var name in new[] { "ExpectedHash", "HashComparison", "HashStatus" })
            {
                var control = Get<FrameworkElement>(window, name);
                var bounds = Bounds(control, host);
                Assert.True(bounds.Height > 0, $"{name} must have visible content.");
                Assert.InRange(bounds.Top, viewport.Top - 0.5, viewport.Bottom);
                Assert.InRange(bounds.Bottom, viewport.Top, viewport.Bottom + 0.5);
            }
            hashScroller.ScrollToTop();
            host.UpdateLayout();
        }
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
