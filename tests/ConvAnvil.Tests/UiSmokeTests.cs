using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConvAnvil.Services;
using ToolKeeper.UI;
using Xunit;

namespace ConvAnvil.Tests;

public sealed class UiSmokeTests
{
    [Fact]
    public Task WholeWindowPreferencesPreserveAllWorkspacesAndRefreshExistingResults() => OnSta(async () =>
    {
        using var fixture = new Fixture("中文\r\nSecond line\n");
        var window = CreateHiddenWindow();
        try
        {
            await window.LoadFileAsync(fixture.Path);
            Get<ComboBox>(window, "TextEncoding").SelectedItem = EncodingCatalog.Find("utf-8-bom");
            Get<ComboBox>(window, "TextFormat").SelectedIndex = 1;
            var entry = Get<TextBox>(window, "TextEntry");
            entry.Text = "ABC中文\r\n";
            await window.EncodeTextAsync();
            entry.Select(1, 3);
            Get<TextBox>(window, "BytesInput").Text = "41 42 E4 B8 AD";
            await window.DecodeBytesAsync();
            var encoded = Get<TextBox>(window, "EncodedOutput").Text;
            var decoded = Get<TextBox>(window, "DecodedOutput").Text;
            var fileText = Get<TextBox>(window, "TargetPreview").Text;
            var encoding = Get<ComboBox>(window, "TextEncoding").SelectedItem;
            var format = Get<ComboBox>(window, "TextFormat").SelectedItem;
            var sourceEncoding = Get<ComboBox>(window, "SourceEncoding").SelectedItem;
            var tabs = Get<TabControl>(window, "WorkspaceTabs");
            tabs.SelectedItem = Get<TabItem>(window, "TextTab");

            window.SelectedTheme = "InkDark";
            window.SelectedLanguage = "en";
            window.ApplyUiPreferences();

            Assert.Equal("ABC中文\r\n", entry.Text);
            Assert.Equal((1, 3), (entry.SelectionStart, entry.SelectionLength));
            Assert.Equal(encoded, Get<TextBox>(window, "EncodedOutput").Text);
            Assert.Equal(decoded, Get<TextBox>(window, "DecodedOutput").Text);
            Assert.Equal(fileText, Get<TextBox>(window, "TargetPreview").Text);
            Assert.Same(encoding, Get<ComboBox>(window, "TextEncoding").SelectedItem);
            Assert.Same(format, Get<ComboBox>(window, "TextFormat").SelectedItem);
            Assert.Same(sourceEncoding, Get<ComboBox>(window, "SourceEncoding").SelectedItem);
            Assert.Same(Get<TabItem>(window, "TextTab"), tabs.SelectedItem);
            Assert.True(Get<Button>(window, "SaveFileButton").IsEnabled);
            Assert.True(Get<Button>(window, "CopyBytesButton").IsEnabled);
            Assert.True(Get<Button>(window, "CopyTextButton").IsEnabled);
            Assert.Equal("Open file…", Get<Button>(window, "OpenFileButton").Content);
            Assert.Contains("Unicode characters", Get<TextBlock>(window, "TextResult").Text);
            Assert.Contains("entire file passed", Get<TextBlock>(window, "FileResult").Text);
            Assert.Contains("Estimate:", Get<TextBlock>(window, "DetectionLabel").Text);
            Assert.Contains("Read ", Get<TextBlock>(window, "StatusLabel").Text);
            Assert.Equal(((SolidColorBrush)window.Resources["SurfaceBrush"]).Color,
                ((SolidColorBrush)Get<TextBox>(window, "SourcePreview").Background).Color);
            Assert.Equal(((SolidColorBrush)window.Resources["SuccessBrush"]).Color,
                ((SolidColorBrush)Get<TextBlock>(window, "FileResult").Foreground).Color);

            window.SelectedLanguage = "ja";
            Assert.Equal("ファイルを開く…", Get<Button>(window, "OpenFileButton").Content);
            Assert.Contains("推定：", Get<TextBlock>(window, "DetectionLabel").Text);
            Assert.Contains("コピー", Get<TextBlock>(window, "BytesResult").Text);
            Assert.Equal(encoded, Get<TextBox>(window, "EncodedOutput").Text);
            Assert.Equal(decoded, Get<TextBox>(window, "DecodedOutput").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task LanguageSwitchDuringConversionUsesCurrentLanguageAndKeepsFullResult() => OnSta(async () =>
    {
        var window = CreateHiddenWindow();
        try
        {
            var input = new string('A', MainWindow.PreviewLimit + 2);
            Get<TextBox>(window, "TextEntry").Text = input;
            var conversion = window.EncodeTextAsync();
            window.SelectedLanguage = "en";
            await conversion;
            Assert.Contains("Unicode characters", Get<TextBlock>(window, "TextResult").Text);
            Assert.Contains("Preview shows the first", Get<TextBox>(window, "EncodedOutput").Text);
            window.SelectedLanguage = "ja";
            Assert.Contains("プレビューは先頭", Get<TextBox>(window, "EncodedOutput").Text);
            Assert.DoesNotContain("Preview shows", Get<TextBox>(window, "EncodedOutput").Text);
            Assert.Equal(input, Get<TextBox>(window, "TextEntry").Text);
            Assert.True(Get<Button>(window, "CopyBytesButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ErrorAndDirtyStatesSurviveLanguageAndThemeChanges() => OnSta(async () =>
    {
        var window = CreateHiddenWindow();
        try
        {
            Get<TextBox>(window, "BytesInput").Text = "ZZ";
            await window.DecodeBytesAsync();
            window.SelectedTheme = "Dark";
            window.SelectedLanguage = "en";
            Assert.StartsWith("Decoding failed:", Get<TextBlock>(window, "BytesResult").Text);
            Assert.Equal(((SolidColorBrush)window.Resources["ErrorBrush"]).Color,
                ((SolidColorBrush)Get<TextBlock>(window, "BytesResult").Foreground).Color);
            Assert.False(Get<Button>(window, "CopyTextButton").IsEnabled);
            Assert.Equal("ZZ", Get<TextBox>(window, "BytesInput").Text);
            Get<TextBox>(window, "TextEntry").Text = "unfinished work";
            window.SelectedLanguage = "ja";
            Assert.Contains("再変換", Get<TextBlock>(window, "TextResult").Text);
            Assert.Equal("unfinished work", Get<TextBox>(window, "TextEntry").Text);
            Assert.False(Get<Button>(window, "CopyBytesButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SharedShellShowsProductTitleDescriptionAndOfflineBadge() => OnSta(() =>
    {
        var window = CreateHiddenWindow();
        try
        {
            Assert.IsAssignableFrom<AppWindow>(window);
            Assert.Equal("ConvAnvil - 文字、編碼與位元組轉換器", window.Title);
            var host = HostWindowContent(window);
            Layout(host, 844, 640);
            var labels = LogicalDescendants<TextBlock>(host).ToArray();
            var title = Assert.Single(labels, label => label.Text == "ConvAnvil");
            var description = Assert.Single(labels, label =>
                label.Text == "檢查文字編碼、預覽轉檔結果，並在文字與位元組之間轉換。");
            var badge = Assert.Single(labels, label => label.Text == "本機處理 · 離線可用");
            var workspace = Assert.IsAssignableFrom<FrameworkElement>(window.Workspace);
            var titlePosition = title.TransformToAncestor(host).Transform(new Point());
            Assert.InRange(titlePosition.Y, 0, 0.5);
            var descriptionPosition = description.TransformToAncestor(host).Transform(new Point());
            var workspacePosition = workspace.TransformToAncestor(host).Transform(new Point());
            Assert.True(title.ActualHeight > 0);
            Assert.True(description.ActualHeight > 0);
            Assert.True(badge.ActualHeight > 0);
            Assert.True(descriptionPosition.Y >= titlePosition.Y + title.ActualHeight);
            Assert.True(workspacePosition.Y >= descriptionPosition.Y + description.ActualHeight);
            Assert.True(Get<TabControl>(window, "WorkspaceTabs").ActualHeight > 0);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TextConversionClearsOldResultsAfterEditingOrChangingOptions() => OnSta(async () =>
    {
        var window = CreateHiddenWindow();
        try
        {
            var input = Get<TextBox>(window, "TextEntry");
            var output = Get<TextBox>(window, "EncodedOutput");
            input.Text = "ABC中文\r\n";
            await window.EncodeTextAsync();
            Assert.Equal("41 42 43 E4 B8 AD E6 96 87 0D 0A", output.Text);
            Assert.True(Get<Button>(window, "CopyBytesButton").IsEnabled);
            Assert.Contains("⟦CR⟧⟦LF⟧", Get<TextBox>(window, "TextControlPreview").Text);

            Get<ComboBox>(window, "TextEncoding").SelectedItem = EncodingCatalog.Find("utf-8-bom");
            Assert.Empty(output.Text);
            Assert.False(Get<Button>(window, "CopyBytesButton").IsEnabled);
            await window.EncodeTextAsync();
            Assert.StartsWith("EF BB BF 41 42 43", output.Text);

            Get<ComboBox>(window, "TextFormat").SelectedIndex = 4;
            Assert.Empty(output.Text);
            Assert.False(Get<Button>(window, "SendBytesButton").IsEnabled);
            await window.EncodeTextAsync();
            Assert.StartsWith("239 187 191 65 66 67", output.Text);

            input.Text = "Changed";
            Assert.Empty(output.Text);
            Assert.False(Get<Button>(window, "CopyBytesButton").IsEnabled);
            Assert.False(Get<Button>(window, "SendBytesButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditingDuringConversionCannotRestoreAnOutdatedResult() => OnSta(async () =>
    {
        var window = CreateHiddenWindow();
        try
        {
            var input = Get<TextBox>(window, "TextEntry");
            input.Text = new string('A', MainWindow.TextInputLimit);
            var previousConversion = window.EncodeTextAsync();
            input.Text = "B";
            await previousConversion;
            Assert.Empty(Get<TextBox>(window, "EncodedOutput").Text);
            Assert.False(Get<Button>(window, "CopyBytesButton").IsEnabled);
            await window.EncodeTextAsync();
            Assert.Equal("42", Get<TextBox>(window, "EncodedOutput").Text);

            var bytesInput = Get<TextBox>(window, "BytesInput");
            bytesInput.Text = string.Join(' ', Enumerable.Repeat("41", 10000));
            var previousDecode = window.DecodeBytesAsync();
            bytesInput.Text = "42";
            await previousDecode;
            Assert.Empty(Get<TextBox>(window, "DecodedOutput").Text);
            Assert.False(Get<Button>(window, "CopyTextButton").IsEnabled);
            await window.DecodeBytesAsync();
            Assert.Equal("B", Get<TextBox>(window, "DecodedOutput").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task InvalidBytesHighlightTheErrorAndCannotCopyPreviousText() => OnSta(async () =>
    {
        var window = CreateHiddenWindow();
        try
        {
            var input = Get<TextBox>(window, "BytesInput");
            var output = Get<TextBox>(window, "DecodedOutput");
            input.Text = "41 42 43 E4 B8 AD E6 96 87";
            await window.DecodeBytesAsync();
            Assert.Equal("ABC中文", output.Text);
            Assert.True(Get<Button>(window, "CopyTextButton").IsEnabled);

            input.Text = "41 GG";
            Assert.Empty(output.Text);
            Assert.False(Get<Button>(window, "CopyTextButton").IsEnabled);
            await window.DecodeBytesAsync();
            Assert.Equal(3, input.SelectionStart);
            Assert.Equal(1, input.SelectionLength);
            Assert.Contains("第 4 個字元", Get<TextBlock>(window, "BytesResult").Text);
            Assert.Empty(output.Text);
            Assert.Empty(Get<TextBox>(window, "BytesControlPreview").Text);
            Assert.False(Get<Button>(window, "CopyTextButton").IsEnabled);

            input.Text = "FF";
            await window.DecodeBytesAsync();
            Assert.Empty(output.Text);
            Assert.False(Get<Button>(window, "CopyTextButton").IsEnabled);
            input.Text = "41";
            await window.DecodeBytesAsync();
            Get<ComboBox>(window, "BytesFormat").SelectedIndex = 4;
            Assert.Empty(output.Text);
            Assert.False(Get<Button>(window, "CopyTextButton").IsEnabled);
            await window.DecodeBytesAsync();
            Assert.Equal(")", output.Text);

            Get<ComboBox>(window, "BytesFormat").SelectedIndex = 0;
            input.Text = "41 00 42";
            await window.DecodeBytesAsync();
            Assert.Equal("A\0B", output.Text);
            Assert.False(Get<Button>(window, "CopyTextButton").IsEnabled);
            Assert.Contains("NUL", Get<TextBlock>(window, "BytesResult").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SendingBytesPreservesARealLeadingFeffRegardlessOfPriorBomOptions() => OnSta(async () =>
    {
        var window = CreateHiddenWindow();
        try
        {
            const string text = "\uFEFFABC中文";
            foreach (var id in new[] { "utf-8", "utf-8-bom" })
            {
                var option = EncodingCatalog.Find(id)!;
                Get<ComboBox>(window, "TextEncoding").SelectedItem = option;
                Get<CheckBox>(window, "StripByteBom").IsChecked = !option.EmitBom;
                Get<TextBox>(window, "TextEntry").Text = text;
                await window.EncodeTextAsync();
                Get<Button>(window, "SendBytesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(option.EmitBom, Get<CheckBox>(window, "StripByteBom").IsChecked);
                await WaitUntilAsync(() => Get<Button>(window, "CopyTextButton").IsEnabled);
                Assert.Equal(text, Get<TextBox>(window, "DecodedOutput").Text);
                Assert.Same(Get<TabItem>(window, "BytesTab"), Get<TabControl>(window, "WorkspaceTabs").SelectedItem);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task NormalizingLineEndingsCanBeUndone() => OnSta(() =>
    {
        var window = CreateHiddenWindow();
        try
        {
            const string original = "第一行\r\n第二行\r第三行\n";
            Get<TabControl>(window, "WorkspaceTabs").SelectedItem = Get<TabItem>(window, "TextTab");
            var host = HostWindowContent(window);
            Layout(host, 1160, 820);
            var input = Get<TextBox>(window, "TextEntry");
            // WPF intentionally clears undo until the TextBox template has created its
            // TextView (ChangeBlockUndoRecord). Offscreen layout performs that setup.
            input.Text = original;
            var normalize = LogicalDescendants<Button>(host).Single(button => Equals(button.Content, "換行 → LF"));
            normalize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("第一行\n第二行\n第三行\n", input.Text);
            Assert.True(input.CanUndo);
            input.Undo();
            Assert.Equal(original, input.Text);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FilePreviewBlocksLossAndClearsSaveAfterFailedLoad() => OnSta(async () =>
    {
        using var fixture = new Fixture("中文😀\r\n第二行\n");
        var originalBytes = await File.ReadAllBytesAsync(fixture.Path);
        var window = CreateHiddenWindow();
        try
        {
            await window.LoadFileAsync(fixture.Path);
            Assert.Equal("中文😀\r\n第二行\n", Get<TextBox>(window, "SourcePreview").Text);
            Assert.Equal(Get<TextBox>(window, "SourcePreview").Text, Get<TextBox>(window, "TargetPreview").Text);
            Assert.Contains("UTF-8", Get<TextBlock>(window, "DetectionLabel").Text);
            Assert.True(Get<Button>(window, "SaveFileButton").IsEnabled);

            Get<ComboBox>(window, "TargetEncoding").SelectedItem = EncodingCatalog.Find("ascii");
            Assert.False(Get<Button>(window, "SaveFileButton").IsEnabled);
            await window.RebuildFilePreviewAsync();
            Assert.NotEmpty(Get<TextBox>(window, "SourcePreview").Text);
            Assert.Empty(Get<TextBox>(window, "TargetPreview").Text);
            Assert.False(Get<Button>(window, "SaveFileButton").IsEnabled);
            Assert.Contains("無法轉換", Get<TextBlock>(window, "FileResult").Text);

            Get<ComboBox>(window, "TargetEncoding").SelectedItem = EncodingCatalog.Default;
            Get<ComboBox>(window, "FileLineEnding").SelectedIndex = 1;
            await window.RebuildFilePreviewAsync();
            Assert.Equal("中文😀\n第二行\n", Get<TextBox>(window, "TargetPreview").Text);
            Assert.True(Get<Button>(window, "SaveFileButton").IsEnabled);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(fixture.Path));

            await window.LoadFileAsync(fixture.Path + ".missing");
            Assert.Empty(Get<TextBox>(window, "SourcePreview").Text);
            Assert.Empty(Get<TextBox>(window, "TargetPreview").Text);
            Assert.False(Get<Button>(window, "SaveFileButton").IsEnabled);
            Assert.True(Get<Button>(window, "OpenFileButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task FileConversionChecksUnrepresentableCharactersBeyondTheVisiblePreview() => OnSta(async () =>
    {
        using var fixture = new Fixture(new string('A', MainWindow.PreviewLimit + 32) + "中文");
        var window = CreateHiddenWindow();
        try
        {
            Get<ComboBox>(window, "TargetEncoding").SelectedItem = EncodingCatalog.Find("ascii");
            await window.LoadFileAsync(fixture.Path);
            var sourcePreview = Get<TextBox>(window, "SourcePreview").Text;
            Assert.Contains("預覽僅顯示前", sourcePreview);
            Assert.DoesNotContain("中文", sourcePreview);
            Assert.StartsWith(new string('A', 100), sourcePreview);
            Assert.Empty(Get<TextBox>(window, "TargetPreview").Text);
            Assert.False(Get<Button>(window, "SaveFileButton").IsEnabled);
            Assert.Contains("無法轉換", Get<TextBlock>(window, "FileResult").Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AllThreeWorkspacesRenderAtDefaultAndCompactSizes() => OnSta(async () =>
    {
        using var fixture = new Fixture("ConvAnvil 編碼診斷範例\r\n工單,設備,狀態\r\n003,控制器A,正常\n控制字元：\t結束\r\n");
        var window = CreateHiddenWindow();
        try
        {
            await window.LoadFileAsync(fixture.Path);
            var host = HostWindowContent(window);
            var tabs = Get<TabControl>(window, "WorkspaceTabs");
            RenderAtBothSizes(host, window, "file", "SaveFileButton");

            tabs.SelectedItem = Get<TabItem>(window, "TextTab");
            Get<TextBox>(window, "TextEntry").Text = "ABC中文\r\n設備編號\t003\r\n字面文字：\\r\\n";
            await window.EncodeTextAsync();
            RenderAtBothSizes(host, window, "text", "TextResult");

            tabs.SelectedItem = Get<TabItem>(window, "BytesTab");
            Get<TextBox>(window, "BytesInput").Text = "02 41 42 43 E4 B8 AD E6 96 87 09 30 30 33 0D 0A 03 00";
            await window.DecodeBytesAsync();
            RenderAtBothSizes(host, window, "bytes", "BytesResult");

            window.SelectedTheme = "InkDark";
            foreach (var language in new[] { "zh-TW", "en", "ja" })
            {
                window.SelectedLanguage = language;
                foreach (var (tab, name, bottomControl) in new[]
                {
                    ("FileTab", "file", "SaveFileButton"), ("TextTab", "text", "TextResult"), ("BytesTab", "bytes", "BytesResult")
                })
                {
                    tabs.SelectedItem = Get<TabItem>(window, tab);
                    Render(host, 844, 640, $"{name}-ink-dark-{language}-minimum.png");
                    AssertMinimumContentBounds(host, window, name, bottomControl);
                    var encodingPicker = Get<ComboBox>(window, name switch
                    {
                        "file" => "TargetEncoding", "text" => "TextEncoding", _ => "BytesEncoding"
                    });
                    var expectedBom = language switch { "en" => "no BOM", "ja" => "BOM なし", _ => "無 BOM" };
                    Assert.Contains(VisualDescendants<TextBlock>(encodingPicker), label => label.Text.Contains(expectedBom));
                }
            }
        }
        finally { window.Close(); }
    });

    private static T Get<T>(MainWindow window, string name) where T : FrameworkElement =>
        Assert.IsAssignableFrom<T>(window.FindName(name));

    private static MainWindow CreateHiddenWindow()
    {
        // These tests deliberately never Show, Activate, or create an application process.
        // Rendering is done on detached content, so the user's desktop stays untouched.
        var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false, PreferencesPath = null, SelectedLanguage = "zh-TW" };
        Assert.False(window.IsVisible);
        Assert.Null(PresentationSource.FromVisual(window));
        return window;
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root))
        {
            if (item is not DependencyObject child) continue;
            if (child is T match) yield return match;
            foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
        }
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(condition(), "The UI operation did not finish before the timeout.");
    }

    private static FrameworkElement HostWindowContent(Window window)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Border { Child = content, Resources = window.Resources };
        host.SetResourceReference(Border.BackgroundProperty, "WindowBackground");
        host.SetResourceReference(TextElement.FontFamilyProperty, "UiFontFamily");
        host.SetResourceReference(TextElement.FontSizeProperty, "UiFontSize");
        host.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        return host;
    }

    private static void RenderAtBothSizes(FrameworkElement host, MainWindow window, string tab, string bottomControl)
    {
        Render(host, 1160, 820, $"{tab}-default.png");
        Render(host, 860, 680, $"{tab}-compact.png");
        // Reserve room for window chrome when checking the minimum 860 × 680 window.
        Render(host, 844, 640, $"{tab}-minimum-client.png");
        AssertMinimumContentBounds(host, window, tab, bottomControl);
    }

    private static void AssertMinimumContentBounds(FrameworkElement host, MainWindow window, string tab, string bottomControl)
    {
        foreach (var name in new[] { bottomControl, "StatusLabel" })
        {
            var control = Get<FrameworkElement>(window, name);
            var point = control.TransformToAncestor(host).Transform(new Point());
            Assert.InRange(point.Y + control.ActualHeight, 1, 641);
            Assert.InRange(point.X + control.ActualWidth, 1, 845);
            Assert.True(control.ActualHeight > 0);
        }

        var inputName = tab switch { "file" => "SourcePreview", "text" => "TextEntry", _ => "BytesInput" };
        Assert.True(Get<TextBox>(window, inputName).ActualHeight > 40, "The primary input or preview must retain usable height.");
    }

    private static void Layout(FrameworkElement host, int width, int height)
    {
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        host.UpdateLayout();
    }

    private static void Render(FrameworkElement host, int width, int height, string name)
    {
        Layout(host, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(System.IO.Path.Combine(root.FullName, "ToolKeeper.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = System.IO.Path.Combine(root.FullName, "artifacts", "convanvil-ui");
        Directory.CreateDirectory(directory);
        using var file = File.Create(System.IO.Path.Combine(directory, name));
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
        }) { IsBackground = true, Name = "ConvAnvil UI smoke" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; }

        public Fixture(string text)
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ToolKeeper.ConvAnvil.UiTests");
            Directory.CreateDirectory(directory);
            Path = System.IO.Path.Combine(directory, $"{Guid.NewGuid():N}.txt");
            File.WriteAllText(Path, text, new UTF8Encoding(true));
        }

        public void Dispose() => File.Delete(Path);
    }
}
