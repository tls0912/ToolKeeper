using System.IO;
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
        return new MainWindow(new LanguagePackService(Path.Combine(root, "packs")), new TranslationEngine(Path.Combine(root, "runtime")), Path.Combine(root, "bundled"))
        {
            PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false
        };
    }

    private static T Get<T>(MainWindow window, string name) where T : class => Assert.IsAssignableFrom<T>(window.FindName(name));

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
