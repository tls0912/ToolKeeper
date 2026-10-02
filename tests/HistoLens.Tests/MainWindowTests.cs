using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HistoLens.Core;
using ToolKeeper.UI;
using Xunit;

namespace HistoLens.Tests;

public sealed class MainWindowTests
{
    [Fact]
    public Task DemoResearchCanBeSavedReopenedAndEditedWithoutMixingResults() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.UiTests", Guid.NewGuid().ToString("N"));
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            Assert.IsType<WindowFrame>(window.Content);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal("Ink", window.SelectedTheme);
            Assert.Null(window.Snapshot); Assert.Null(window.Result);
            window.LoadDemo();
            Assert.True(window.Snapshot!.IsSynthetic);
            await window.RunResearchAsync();
            Assert.True(window.Result!.IsResearchAllowed);
            Assert.NotEmpty(window.Result.Cases);
            var original = window.Result;
            var path = await window.SaveResearchAsync();
            var output = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                File.Copy(path, Path.Combine(output, "demo.histolens.json"), overwrite: true);
                await File.WriteAllTextAsync(Path.Combine(output, "demo.snapshot.json"),
                    System.Text.Json.JsonSerializer.Serialize(window.Snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            Get<TextBox>(window, "_upper").Text = "7";
            Assert.True(window.IsResultStale); Assert.Same(original, window.Result);
            await Assert.ThrowsAsync<InvalidOperationException>(() => window.SaveResearchAsync());
            window.SelectedLanguage = "ja"; window.SelectedTheme = "InkDark";
            Assert.Same(original, window.Result); Assert.Equal("7", Get<TextBox>(window, "_upper").Text);
            await window.LoadResearchAsync(path);
            Assert.False(window.IsResultStale);
            Assert.Equal(5m, decimal.Parse(Get<TextBox>(window, "_upper").Text, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(original.DataContentHash, window.Result!.DataContentHash);
            Assert.Equal(original.Definition.Conditions, window.Result.Definition.Conditions);
            await window.RunResearchAsync();
            Assert.Equal(original.Funnel, window.Result.Funnel);

            // A failed file open must not discard the last valid result.
            var valid = window.Result;
            var broken = Path.Combine(directory, "broken.histolens.json");
            await File.WriteAllTextAsync(broken, "{}");
            await Assert.ThrowsAnyAsync<Exception>(() => window.LoadResearchAsync(broken));
            Assert.Same(valid, window.Result);
            // Edit while a worker is queued: its obsolete result may not replace the previous run.
            var pending = window.RunResearchAsync();
            Get<TextBox>(window, "_upper").Text = "9";
            await pending;
            Assert.True(window.IsResultStale); Assert.Same(valid, window.Result);
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Fact]
    public Task EveryTemplateRoundTripsItsEditableParameters() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.UiTests", Guid.NewGuid().ToString("N"));
        var window = new MainWindow(directory) { PreferencesPath = null };
        try
        {
            window.LoadDemo();
            for (var index = 0; index < ResearchTemplates.All.Count; index++)
            {
                Get<ComboBox>(window, "_template").SelectedIndex = index;
                Get<TextBox>(window, "_lookback").Text = index == 1 ? "4" : "30";
                if (index is 0 or 2) Get<TextBox>(window, "_threshold").Text = index == 0 ? "12" : "1.8";
                await window.RunResearchAsync();
                var original = window.Result!;
                Assert.True(original.IsResearchAllowed);
                var file = await window.SaveResearchAsync();
                window.LoadDemo(); await window.LoadResearchAsync(file); await window.RunResearchAsync();
                Assert.Equal(original.Definition.Conditions, window.Result!.Definition.Conditions);
                Assert.Equal(original.Funnel, window.Result.Funnel);
            }
        }
        finally { window.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Theory]
    [InlineData("zh-TW", "Ink", 900, 660)]
    [InlineData("en", "Dark", 1280, 880)]
    [InlineData("ja", "InkDark", 1280, 880)]
    public Task SharedShellRendersAndThemeChangesKeepResearch(string language, string theme, int width, int height) => OnSta(async () =>
    {
        var window = new MainWindow(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))) { PreferencesPath = null, SelectedLanguage = language, SelectedTheme = theme };
        try
        {
            window.LoadDemo(); await window.RunResearchAsync();
            var result = window.Result;
            Assert.NotNull(result);
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(width, height)); frame.Arrange(new Rect(0, 0, width, height)); frame.UpdateLayout();
            Assert.True(Get<DataGrid>(window, "_stats").ActualWidth > 200);
            Assert.True(Get<DataGrid>(window, "_stats").ActualHeight > 100);
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                Render(frame, folder, $"histolens-{language}-{theme}-{width}.png", width, height);
            }
            var tabs = Descendants(frame).OfType<TabControl>().Single();
            tabs.SelectedIndex = 1; frame.UpdateLayout();
            Assert.True(Get<DataGrid>(window, "_cases").ActualHeight >= 45);
            Assert.True(Get<DataGrid>(window, "_bars").ActualHeight >= 45);
            if (!string.IsNullOrEmpty(folder)) Render(frame, folder, $"histolens-cases-{language}-{theme}-{width}.png", width, height);
            foreach (var next in new[] { "Light", "Dark", "Ink", "InkDark", "System" })
            { window.SelectedTheme = next; Assert.Same(result, window.Result); }
        }
        finally { window.Close(); }
    });

    private static T Get<T>(MainWindow window, string field) => (T)typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    [Fact]
    public Task HistoricalCutoffAlsoLimitsTheCaseChartAndRawTable() => OnSta(async () =>
    {
        var window = new MainWindow(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))) { PreferencesPath = null };
        try
        {
            window.LoadDemo();
            var cutoff = window.Snapshot!.Calendar.TradingDates[160];
            Get<TextBox>(window, "_end").Text = cutoff.ToString("yyyy-MM-dd");
            Get<TextBox>(window, "_asOf").Text = cutoff.ToString("yyyy-MM-dd");
            Get<ComboBox>(window, "_sampling").SelectedItem = SamplingPolicy.EveryMatch;
            await window.RunResearchAsync();
            var cases = Get<DataGrid>(window, "_cases");
            var excluded = cases.Items.Cast<object>().First(item =>
                ((HorizonOutcome)item.GetType().GetProperty("Outcome")!.GetValue(item)!).PrimaryExclusion == ExclusionReason.InsufficientFutureData);
            cases.SelectedItem = excluded;
            var bars = Get<DataGrid>(window, "_bars").Items.Cast<DailyBar>().ToArray();
            Assert.NotEmpty(bars);
            Assert.All(bars, bar => Assert.True(bar.Date <= cutoff));
        }
        finally { window.Close(); }
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Render(Visual frame, string folder, string file, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, file)); encoder.Save(stream);
    }
    private static Task OnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
