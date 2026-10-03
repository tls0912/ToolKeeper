using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    public Task MissingRuntimeUnsupportedJapaneseAndOversizeInputStayExplicit() => OnSta(async () =>
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
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "ja";
            Assert.Contains("Japanese packs are not available", Get<TextBlock>(window, "AvailabilityText").Text);
            await window.TranslateAsync();
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
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
            Assert.False(Get<Button>(window, "TranslateButton").IsEnabled);
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
            Assert.False(Get<Button>(window, "TranslateButton").IsEnabled);
            Assert.Equal("Check device", source.Text);
            await externalService.ImportAsync(fixture.MakePack("external-reimport.tlpack"));
            Get<Expander>(window, "PacksExpander").IsExpanded = true;
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
                Get<Expander>(window, "PacksExpander").IsExpanded = true;
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
    public Task LiteralWarningAndExpandedPacksFitTheMinimumSizeInThreeLanguages() => OnSta(async () =>
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
                Get<Expander>(window, "PacksExpander").IsExpanded = true;
                Layout(host, 900, 720);
                foreach (var name in new[] { "PasteButton", "CopyButton", "CancelButton", "PacksExpander", "StatusText", "QualityWarning" })
                    AssertWithinViewport(Get<FrameworkElement>(window, name), host, 900, 720);
                var qualityNotice = Assert.IsType<Grid>(window.Workspace).Children.OfType<TextBlock>().Single(text => Grid.GetRow(text) == 5);
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
                SaveImage(host, $"literal-warning-{language}-{theme}-packs.png");
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
        var window = CreateWindow();
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
            foreach (var expanded in new[] { false, true })
            {
                window.SelectedLanguage = language;
                window.SelectedTheme = theme;
                Get<Expander>(window, "PacksExpander").IsExpanded = expanded;
                Layout(host, 900, 720);
                foreach (var name in new[] { "PasteButton", "CopyButton", "CancelButton", "PacksExpander", "StatusText" })
                {
                    var element = Get<FrameworkElement>(window, name);
                    var origin = element.TransformToAncestor(host).Transform(new Point());
                    Assert.InRange(origin.X, 0, 900);
                    Assert.InRange(origin.Y, 0, 720);
                    Assert.InRange(origin.X + element.ActualWidth, 1, 901);
                    Assert.InRange(origin.Y + element.ActualHeight, 1, 721);
                }
                Assert.True(Get<TextBox>(window, "SourceText").ActualHeight >= 70);
                SaveImage(host, $"{language}-{theme}-{(expanded ? "packs" : "translation")}.png");
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
        return new MainWindow(new LanguagePackService(Path.Combine(root, "packs"), NoModelValidation), new TranslationEngine(Path.Combine(root, "runtime")), Path.Combine(root, "bundled"))
        {
            PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false
        };
    }

    private static T Get<T>(MainWindow window, string name) where T : class => Assert.IsAssignableFrom<T>(window.FindName(name));

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

        public MainWindow CreateWindow() => new(Service, Engine, BundledDirectory)
        {
            PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false, SelectedLanguage = "en"
        };

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
                DisplayName = "UI fixture " + source + " → " + target, PackageVersion = "1.0.0", ModelName = "UI fixture", ModelVersion = "1",
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

    private static void Layout(FrameworkElement host, int width, int height)
    {
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        host.UpdateLayout();
    }

    private static void SaveImage(FrameworkElement host, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ToolKeeper.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Path.Combine(root.FullName, "artifacts", "translamp-ui");
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(900, 720, 96, 96, PixelFormats.Pbgra32);
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
