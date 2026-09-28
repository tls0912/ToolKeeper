using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MarkPad.Editing;
using MarkPad.Models;
using MarkPad.Rendering;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace MarkPad.Tests;

public sealed class ScrollSynchronizationTests
{
    [Fact]
    public Task EditorSynchronizationPreservesSelectionAndDoesNotEcho() => StaTest.Run(async () =>
    {
        var tab = new DocumentTab { Content = Document(), IsPreviewMode = false };
        var editor = new EditorPane();
        editor.Bind(tab);
        var window = Host(editor);
        try
        {
            window.Show();
            await LayoutAsync(window);
            editor.Editor.Select(8, 5);
            var caret = editor.Editor.CaretOffset;
            var notifications = 0;
            editor.ScrollChanged += (_, _) => notifications++;

            editor.ScrollToPosition(new ScrollPosition(81.5, 0.2));
            await LayoutAsync(window);

            Assert.InRange(editor.GetScrollPosition().Line, 81.4, 81.6);
            Assert.Equal(8, editor.Editor.SelectionStart);
            Assert.Equal(5, editor.Editor.SelectionLength);
            Assert.Equal(caret, editor.Editor.CaretOffset);
            Assert.Equal(0, notifications);

            var previousOffset = editor.Editor.VerticalOffset;
            editor.Editor.ScrollToVerticalOffset(previousOffset + 80);
            await LayoutAsync(window);
            await Task.Delay(50);

            Assert.True(notifications > 0, $"No notification for editor offset {previousOffset} -> {editor.Editor.VerticalOffset}; text view {editor.Editor.TextArea.TextView.ScrollOffset.Y}.");
            Assert.Equal(8, editor.Editor.SelectionStart);
            Assert.Equal(5, editor.Editor.SelectionLength);
            Assert.Equal(caret, editor.Editor.CaretOffset);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ShortEditorKeepsFinitePositionWhenThereIsNothingToScroll() => StaTest.Run(async () =>
    {
        var editor = new EditorPane();
        editor.Bind(new DocumentTab { Content = "Short note", IsPreviewMode = false });
        var window = Host(editor);
        try
        {
            window.Show();
            await LayoutAsync(window);
            editor.ScrollToPosition(new ScrollPosition(500, 1));
            await LayoutAsync(window);
            Assert.Equal(0, editor.Editor.VerticalOffset);
            Assert.Equal(new ScrollPosition(1, 0), editor.GetScrollPosition());
            editor.ScrollToPosition(new ScrollPosition(double.NaN, double.PositiveInfinity));
            Assert.Equal(new ScrollPosition(1, 0), editor.GetScrollPosition());
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Category", "WebView2")]
    public Task LivePreviewAndEditorFollowSourcePositionsWithoutMovingCaretOrHiddenTabs() => StaTest.Run(async () =>
    {
        var directory = new TestDirectory();
        var tab = new DocumentTab { Content = Document(), IsPreviewMode = false };
        using var view = new DocumentView(tab, directory.FilePath("browser"));
        view.SetActive(true);
        view.Editor.ApplyOptions(false, "Consolas", 14);
        var window = Host(view);
        var browserExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browserStarted = false;
        try
        {
            window.Show();
            await LayoutAsync(window);
            var options = new PreviewOptions(FontSize: 24);
            await view.Preview.ShowAsync(tab.Content, null, options);
            Assert.True(view.Preview.IsShowing(tab.Content, null), Assert.IsType<TextBlock>(Assert.IsType<Grid>(view.Preview.Content).Children[1]).Text);
            var browser = Assert.IsType<WebView2CompositionControl>(Assert.IsType<Grid>(view.Preview.Content).Children[0]);
            browser.CoreWebView2.Environment.BrowserProcessExited += (_, _) => browserExited.TrySetResult();
            browserStarted = true;
            PreviewMessage? lastScroll = null;
            view.Preview.MessageReceived += (_, message) => { if (message.Type == "scroll") lastScroll = message; };
            view.Editor.Editor.Select(8, 5);
            var caret = view.Editor.Editor.CaretOffset;

            var editorTop = view.Editor.Editor.TextArea.TextView.GetVisualTopByDocumentLine(81);
            view.Editor.Editor.ScrollToVerticalOffset(editorTop);
            await UntilAsync(async () => Math.Abs(await NumberAsync(browser,
                "document.querySelector('[data-source-line=\"81\"]').getBoundingClientRect().top")) < 80);
            var editorOffset = view.Editor.Editor.VerticalOffset;
            await Task.Delay(150);
            Assert.InRange(view.Editor.Editor.VerticalOffset, editorOffset - 1, editorOffset + 1);

            await browser.ExecuteScriptAsync("window.dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true}));window.scrollTo({top:window.scrollY+document.querySelector('[data-source-line=\"121\"]').getBoundingClientRect().top,behavior:'instant'})");
            await UntilAsync(() => Task.FromResult(Math.Abs(view.Editor.GetScrollPosition().Line - 121) < 2));
            Assert.Equal(8, view.Editor.Editor.SelectionStart);
            Assert.Equal(5, view.Editor.Editor.SelectionLength);
            Assert.Equal(caret, view.Editor.Editor.CaretOffset);

            await browser.ExecuteScriptAsync("window.dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true}));window.scrollTo({top:document.documentElement.scrollHeight,behavior:'instant'})");
            await UntilAsync(() => Task.FromResult(view.Editor.GetScrollPosition().Progress >= 0.999),
                () => $"Editor: {view.Editor.GetScrollPosition()}; offset {view.Editor.Editor.VerticalOffset}, range {view.Editor.Editor.ExtentHeight - view.Editor.Editor.ViewportHeight}; preview {lastScroll}");
            await browser.ExecuteScriptAsync("window.dispatchEvent(new WheelEvent('wheel',{deltaY:-120,bubbles:true}));window.scrollTo({top:0,behavior:'instant'})");
            await UntilAsync(() => Task.FromResult(view.Editor.Editor.VerticalOffset < 1));
            view.Editor.Editor.ScrollToEnd();
            await UntilAsync(async () => await NumberAsync(browser, "document.documentElement.scrollHeight-innerHeight-window.scrollY") <= 2);
            view.Editor.Editor.ScrollToHome();
            await UntilAsync(async () => await NumberAsync(browser, "window.scrollY") < 1);
            view.Editor.Editor.ScrollToVerticalOffset(editorTop);
            await UntilAsync(async () => Math.Abs(await NumberAsync(browser,
                "document.querySelector('[data-source-line=\"81\"]').getBoundingClientRect().top")) < 80);

            editorOffset = view.Editor.Editor.VerticalOffset;
            tab.Document.Insert(tab.Document.TextLength, "\n# Appended heading\n\nAdditional text.\n");
            await view.Preview.ShowAsync(tab.Content, null, options);
            await view.Preview.FindAsync("", false, restart: true);
            await LayoutAsync(window);
            await Task.Delay(150);
            Assert.InRange(view.Editor.Editor.VerticalOffset, editorOffset - 1, editorOffset + 1);

            view.SetActive(false);
            editorOffset = view.Editor.Editor.VerticalOffset;
            await browser.ExecuteScriptAsync("window.dispatchEvent(new WheelEvent('wheel',{deltaY:-120,bubbles:true}));window.scrollTo({top:0,behavior:'instant'})");
            await Task.Delay(150);
            Assert.InRange(view.Editor.Editor.VerticalOffset, editorOffset - 1, editorOffset + 1);
            Assert.Equal(8, view.Editor.Editor.SelectionStart);
            Assert.Equal(5, view.Editor.Editor.SelectionLength);
            Assert.Equal(caret, view.Editor.Editor.CaretOffset);
        }
        finally
        {
            window.Close();
            view.Dispose();
            if (browserStarted) await Task.WhenAny(browserExited.Task, Task.Delay(5000));
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try { directory.Dispose(); break; }
                catch (System.IO.IOException) when (attempt < 29) { await Task.Delay(100); }
            }
        }
    });

    private static string Document() => string.Join("\n\n", Enumerable.Range(1, 100).Select(index =>
        $"# Section {index}\n\n" + string.Concat(Enumerable.Repeat("A paragraph with enough text to wrap differently in each pane. ", 5)))) + "\n";

    private static Window Host(UIElement content) => new()
    {
        Content = content, Width = 1200, Height = 600, Left = -20000, Top = -20000,
        ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
    };

    private static async Task LayoutAsync(Window window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static async Task<double> NumberAsync(WebView2CompositionControl browser, string expression) =>
        double.Parse(await browser.ExecuteScriptAsync(expression), CultureInfo.InvariantCulture);

    private static async Task UntilAsync(Func<Task<bool>> condition, Func<string>? diagnostic = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }
        Assert.True(await condition(), "The panes did not reach the expected synchronized position. " + diagnostic?.Invoke());
    }
}
