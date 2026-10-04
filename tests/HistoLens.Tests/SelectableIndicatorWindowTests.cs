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

[Collection("HistoLens WPF UI")]
public sealed class SelectableIndicatorWindowTests
{
    [Theory]
    [InlineData(SimilarityIndicator.Rsi | SimilarityIndicator.VolumePath)]
    [InlineData(SimilarityIndicator.None)]
    public Task SelectedIndicatorsAreRememberedWhenTheWindowReopens(SimilarityIndicator selected) => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.IndicatorMemory-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "indicator-preferences.json");
        var uiPath = Path.Combine(directory, "ui.json");
        var window = NewWindow(directory);
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(uiPath, "existing UI preferences");
            Select(window, selected);
            var stored = File.ReadAllBytes(path);
            Get<TextBox>(window, "_recentDays").Text = "21";
            Get<TextBox>(window, "_minimumSimilarity").Text = "61";
            Get<Button>(window, "_dataNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.SelectedLanguage = "ja"; window.SelectedTheme = "InkDark";
            Get<Button>(window, "_magnifierNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(selected, CurrentSelection(window));
            Assert.Equal(stored, File.ReadAllBytes(path));
            Assert.Equal("existing UI preferences", File.ReadAllText(uiPath));
            window.Close();

            window = NewWindow(directory);
            Assert.Equal(selected, CurrentSelection(window));
            Assert.Equal(stored, File.ReadAllBytes(path));
            Assert.Null(window.SimilarityResult);
            window.LoadDemo(); await window.RunSimilarityAsync();
            if (selected == SimilarityIndicator.None)
            {
                Assert.Null(window.SimilarityResult);
                Assert.Contains("至少", Get<TextBlock>(window, "_status").Text);
            }
            else Assert.Equal(selected, window.SimilarityResult!.Definition.SelectedIndicators);
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    });

    [Fact]
    public Task SelectAllAndDefaultButtonsSaveTheirCompletedSelection() => OnSta(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.IndicatorActions-" + Guid.NewGuid().ToString("N"));
        var window = NewWindow(directory);
        try
        {
            IndicatorAction(window, 0).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SimilarityIndicator.All, new IndicatorPreferencesStore(directory).Load());
            window.Close(); window = NewWindow(directory);
            Assert.Equal(SimilarityIndicator.All, CurrentSelection(window));
            IndicatorAction(window, 1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(SimilarityIndicator.Default, new IndicatorPreferencesStore(directory).Load());
            window.Close(); window = NewWindow(directory);
            Assert.Equal(SimilarityIndicator.Default, CurrentSelection(window));
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnreadableSavedIndicatorsUseDefaultsAndReportTheLoadFailure() => OnSta(() =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.IndicatorLoadFailure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "indicator-preferences.json"));
        var window = NewWindow(directory);
        try
        {
            Assert.Equal(SimilarityIndicator.Default, CurrentSelection(window));
            Assert.Null(window.SimilarityResult);
            Assert.Contains("無法讀取", Get<TextBlock>(window, "_status").Text);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SaveFailureRetainsTheSelectionAndPreviousResultAndAllowsScanning() => OnSta(async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.IndicatorSaveFailure-" + Guid.NewGuid().ToString("N"));
        var window = NewWindow(directory);
        try
        {
            window.LoadDemo(); await window.RunSimilarityAsync(); var previous = window.SimilarityResult;
            Directory.CreateDirectory(Path.Combine(directory, "indicator-preferences.json"));
            Choices(window)[SimilarityIndicator.Rsi].IsChecked = true;
            Assert.Equal(SimilarityIndicator.Default | SimilarityIndicator.Rsi, CurrentSelection(window));
            Assert.Same(previous, window.SimilarityResult); Assert.True(window.IsSimilarityResultStale);
            Assert.Contains("無法保存", Get<TextBlock>(window, "_status").Text);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            await window.RunSimilarityAsync();
            Assert.Equal(SimilarityIndicator.Default | SimilarityIndicator.Rsi,
                window.SimilarityResult!.Definition.SelectedIndicators);
        }
        finally { window.Close(); Directory.Delete(directory, recursive: true); }
    });

    [Fact]
    public Task SelectionChangesTotalsAndColumnsOnlyAfterSuccessfulRerun() => OnSta(async () =>
    {
        var window = NewWindow();
        try
        {
            window.LoadDemo(); await window.RunSimilarityAsync();
            var baseline = window.SimilarityResult!;
            Assert.Equal(SimilarityIndicator.Default, baseline.Definition.SelectedIndicators);
            Assert.Equal(10, Choices(window).Count); Assert.Equal(7, VisibleColumns(window));
            var selection = SimilarityIndicator.Rsi | SimilarityIndicator.VolumePath;
            Select(window, selection);
            Assert.Same(baseline, window.SimilarityResult); Assert.True(window.IsSimilarityResultStale);
            Assert.Equal(7, VisibleColumns(window)); Assert.Equal(5, Get<DataGrid>(window, "_similarityIndicators").Items.Count);
            await window.RunSimilarityAsync();
            var current = window.SimilarityResult!;
            Assert.Equal(selection, current.Definition.SelectedIndicators); Assert.False(window.IsSimilarityResultStale);
            Assert.NotEmpty(current.Matches); Assert.Equal(4, VisibleColumns(window));
            Assert.Equal(2, Get<DataGrid>(window, "_similarityIndicators").Items.Count);
            Assert.All(current.Matches, match =>
            {
                Assert.Equal((match.Scores.Rsi + match.Scores.VolumePath) / 2, match.Scores.Total);
                Assert.Null(match.Scores.GetScore(SimilarityIndicator.PricePath));
            });
            Assert.Contains("等權指標：2", Get<TextBlock>(window, "_similaritySummary").Text);
            Select(window, SimilarityIndicator.None); await window.RunSimilarityAsync();
            Assert.Same(current, window.SimilarityResult); Assert.True(window.IsSimilarityResultStale);
            Assert.Equal(4, VisibleColumns(window)); Assert.Contains("至少", Get<TextBlock>(window, "_status").Text);
            Select(window, SimilarityIndicator.Default); await window.RunSimilarityAsync();
            Assert.Equal(baseline.Matches.Select(match => (match.Features.Start, match.Scores.Total)),
                window.SimilarityResult!.Matches.Select(match => (match.Features.Start, match.Scores.Total)));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditingSelectionDuringScanDoesNotReplacePreviousRun() => OnSta(async () =>
    {
        var window = NewWindow();
        try
        {
            window.LoadDemo(); await window.RunSimilarityAsync(); var previous = window.SimilarityResult;
            Select(window, SimilarityIndicator.All);
            var pending = window.RunSimilarityAsync();
            Choices(window)[SimilarityIndicator.Rsi].IsChecked = false;
            await pending;
            Assert.Same(previous, window.SimilarityResult); Assert.True(window.IsSimilarityResultStale);
            Assert.True(Get<Button>(window, "_scanButton").IsEnabled);
            Assert.False(Get<Button>(window, "_cancel").IsEnabled);
            await window.RunSimilarityAsync();
            Assert.Equal(SimilarityIndicator.All & ~SimilarityIndicator.Rsi, window.SimilarityResult!.Definition.SelectedIndicators);
            Assert.Equal(11, VisibleColumns(window));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task AllIndicatorsKeepTheirSelectionAndValuesAcrossPagesAndLanguages() => OnSta(async () =>
    {
        var window = NewWindow();
        try
        {
            window.LoadDemo(); Select(window, SimilarityIndicator.All); await window.RunSimilarityAsync();
            var run = window.SimilarityResult!; Assert.NotEmpty(run.Matches);
            Assert.Equal(10, Get<DataGrid>(window, "_similarityIndicators").Items.Count);
            Assert.Equal(12, VisibleColumns(window));
            foreach (var item in Get<DataGrid>(window, "_similarityIndicators").Items)
                foreach (var property in new[] { "Reference", "Historical", "Score" })
                    Assert.DoesNotContain("—", (string)item.GetType().GetProperty(property)!.GetValue(item)!);
            Get<Button>(window, "_dataNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.SelectedLanguage = "ja"; window.SelectedTheme = "InkDark";
            Get<Button>(window, "_magnifierNavigation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(run, window.SimilarityResult); Assert.All(Choices(window).Values, check => Assert.True(check.IsChecked));
            Assert.Contains("10/10", (string)Get<GroupBox>(window, "_indicatorSelection").Header);
            Assert.Contains("出来高", (string)Choices(window)[SimilarityIndicator.VolumePath].Content);
            Assert.Equal(10, Get<DataGrid>(window, "_similarityIndicators").Items.Count);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("zh-TW", "Ink", 900, 660)]
    [InlineData("en", "Dark", 1280, 880)]
    [InlineData("ja", "InkDark", 1280, 880)]
    public Task IndicatorChoicesAndSelectedScoresRender(string language, string theme, int width, int height) => OnSta(async () =>
    {
        var window = NewWindow(); window.SelectedLanguage = language; window.SelectedTheme = theme;
        try
        {
            window.LoadDemo(); Select(window, SimilarityIndicator.All); await window.RunSimilarityAsync();
            var section = Get<GroupBox>(window, "_indicatorSelection");
            var frame = Assert.IsType<WindowFrame>(window.Content);
            frame.Measure(new Size(width, height)); frame.Arrange(new Rect(0, 0, width, height)); frame.UpdateLayout();
            var panel = Assert.IsType<StackPanel>(section.Parent);
            var scroll = Assert.IsType<ScrollViewer>(panel.Parent);
            scroll.ScrollToVerticalOffset(section.TransformToAncestor(panel).Transform(new Point()).Y); frame.UpdateLayout();
            Assert.True(Get<Button>(window, "_scanButton").ActualHeight > 20);
            Assert.True(Get<DataGrid>(window, "_similarities").ActualHeight > 75);
            Assert.True(Get<SimilarityComparisonChart>(window, "_comparisonChart").ActualHeight > 110);
            Assert.All(Choices(window).Values, check => Assert.True(check.ActualHeight > 12));
            var folder = Environment.GetEnvironmentVariable("HISTOLENS_SCREENSHOT_DIR");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"histolens-indicators-{language}-{theme}-{width}.png")); encoder.Save(file);
            }
        }
        finally { window.Close(); }
    });

    private static void Select(MainWindow window, SimilarityIndicator selected)
    { foreach (var (indicator, check) in Choices(window)) check.IsChecked = selected.HasFlag(indicator); }
    private static SimilarityIndicator CurrentSelection(MainWindow window) => Choices(window).Aggregate(SimilarityIndicator.None,
        (selected, item) => item.Value.IsChecked == true ? selected | item.Key : selected);
    private static Button IndicatorAction(MainWindow window, int index)
    {
        var choices = Assert.IsType<StackPanel>(Get<GroupBox>(window, "_indicatorSelection").Content);
        var actions = Assert.IsType<WrapPanel>(choices.Children[^1]);
        return Assert.IsType<Button>(actions.Children[index]);
    }
    private static Dictionary<SimilarityIndicator, CheckBox> Choices(MainWindow window) => Get<Dictionary<SimilarityIndicator, CheckBox>>(window, "_indicatorChoices");
    private static int VisibleColumns(MainWindow window) => Get<DataGrid>(window, "_similarities").Columns.Count(column => column.Visibility == Visibility.Visible);
    private static MainWindow NewWindow(string? directory = null) => new(directory ?? Path.Combine(Path.GetTempPath(), "HistoLens.IndicatorUi-" + Guid.NewGuid().ToString("N")))
        { PreferencesPath = null, SelectedLanguage = "zh-TW" };
    private static T Get<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
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
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
