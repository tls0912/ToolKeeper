using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using ToolKeeper.UI;
using Xunit;

namespace ToolKeeper.UI.Tests;

public sealed class WindowFrameTests
{
    [Fact]
    public Task CaptionTracksFullOwnerTitleAndKeepsWindowControlsInsideNarrowWidths() => StaTest.Run(() =>
    {
        var owner = NewOwner("ConvAnvil - Text, Encoding & Byte Converter");
        var frame = new WindowFrame(owner) { Workspace = new TextBox { Text = "Keep this result" } };
        owner.Content = frame;
        try
        {
            frame.ApplyMetrics(16);
            var title = Named<TextBlock>(frame, "WindowTitleText");
            var minimize = Named<Button>(frame, "MinimizeButton");
            var close = Named<Button>(frame, "WindowCloseButton");
            Assert.Equal(owner.Title, title.Text);
            Assert.Equal(owner.Title, title.ToolTip);
            foreach (var width in new[] { 860d, 440d, 320d })
            {
                Arrange(frame, width, 500);
                var titleBounds = Bounds(title, frame);
                var minimizeBounds = Bounds(minimize, frame);
                Assert.True(titleBounds.Right <= minimizeBounds.Left + 0.5);
                Assert.True(Bounds(close, frame).Right <= width);
                Assert.Equal(owner.Title, title.Text);
            }

            owner.Title = "汗青 - Markdown Writer";
            Assert.Equal(owner.Title, title.Text);
            Assert.Equal(owner.Title, title.ToolTip);
            Assert.Equal(WindowStyle.None, owner.WindowStyle);
            Assert.False(WindowChrome.GetWindowChrome(owner).UseAeroCaptionButtons);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task CaptionCloseRespectsTheOwnersClosingGuard() => StaTest.Run(() =>
    {
        var owner = NewOwner("Document - unsaved");
        var frame = new WindowFrame(owner);
        owner.Content = frame;
        var cancel = true;
        var closeRequests = 0;
        var closed = false;
        owner.Closing += (_, e) => { closeRequests++; e.Cancel = cancel; };
        owner.Closed += (_, _) => closed = true;
        try
        {
            var close = Named<Button>(frame, "WindowCloseButton");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, closeRequests);
            Assert.False(closed);
            cancel = false;
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, closeRequests);
            Assert.True(closed);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, closeRequests);
        }
        finally
        {
            cancel = false;
            if (!closed) owner.Close();
        }
    });

    [Fact]
    public Task CaptionSlotsRemainInteractiveWithoutTurningTheWorkspaceIntoChrome() => StaTest.Run(() =>
    {
        var owner = NewOwner("汗青 - Markdown Writer");
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        var document = new Button { Content = "Unsaved document" };
        tabs.Children.Add(document);
        var overflow = new Button { Content = "More documents", Width = 32 };
        var editor = new TextBox { Text = "Keep this draft", SelectionStart = 4 };
        var frame = new WindowFrame(owner) { Workspace = editor, CaptionContent = tabs, CaptionActions = overflow };
        owner.Content = frame;
        try
        {
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(tabs));
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(document));
            Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(overflow));
            Assert.False(WindowChrome.GetIsHitTestVisibleInChrome(editor));
            foreach (var name in new[] { "MinimizeButton", "MaximizeButton", "WindowCloseButton" })
                Assert.True(WindowChrome.GetIsHitTestVisibleInChrome(Named<Button>(frame, name)));
            Arrange(frame, 640, 480);
            Assert.True(Bounds(tabs, frame).Width >= 99);
            Assert.True(Bounds(overflow, frame).Right <= Bounds(Named<Button>(frame, "MinimizeButton"), frame).Left + 0.5);
            Assert.Same(editor, frame.Workspace);
            Assert.Equal("Keep this draft", editor.Text);
            Assert.Equal(4, editor.SelectionStart);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task FullScreenCaptionOverlaysAndRestoresWithoutReplacingProductContent() => StaTest.Run(() =>
    {
        var owner = NewOwner("汗青 - Markdown Writer");
        var editor = new TextBox { Text = "Unchanged draft", SelectionStart = 3 };
        var frame = new WindowFrame(owner) { Workspace = editor };
        owner.Content = frame;
        try
        {
            frame.ApplyMetrics(16);
            Arrange(frame, 800, 600);
            var caption = Named<Border>(frame, "TitleBar");
            Assert.InRange(Bounds(editor, frame).Top, frame.CaptionHeight, frame.CaptionHeight + 2);
            Assert.Equal(frame.CaptionHeight, WindowChrome.GetWindowChrome(owner).CaptionHeight);

            frame.IsFullScreen = true;
            frame.CaptionLeftInset = 200;
            Assert.False(frame.IsCaptionVisible);
            Assert.Equal(0, WindowChrome.GetWindowChrome(owner).CaptionHeight);
            Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(owner).ResizeBorderThickness);
            frame.IsCaptionVisible = true;
            frame.ApplyMetrics(20);
            Arrange(frame, 800, 600);
            Assert.InRange(Bounds(editor, frame).Top, 0, 2);
            Assert.InRange(Bounds(caption, frame).Left, 200, 202);
            Assert.InRange(Bounds(caption, frame).Height, frame.CaptionHeight - 1, frame.CaptionHeight + 1);

            frame.IsFullScreen = false;
            Arrange(frame, 800, 600);
            Assert.True(frame.IsCaptionVisible);
            Assert.Equal(0, frame.CaptionLeftInset);
            Assert.Equal(new Thickness(6), WindowChrome.GetWindowChrome(owner).ResizeBorderThickness);
            Assert.Equal(frame.CaptionHeight, WindowChrome.GetWindowChrome(owner).CaptionHeight);
            Assert.InRange(Bounds(editor, frame).Top, frame.CaptionHeight, frame.CaptionHeight + 2);
            Assert.Same(editor, frame.Workspace);
            Assert.Equal("Unchanged draft", editor.Text);
            Assert.Equal(3, editor.SelectionStart);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task DialogResizeModeControlsCaptionAndContentDeterminesAutomaticHeight() => StaTest.Run(() =>
    {
        var owner = NewOwner("汗青 - Markdown Writer");
        owner.ResizeMode = ResizeMode.NoResize;
        owner.SizeToContent = SizeToContent.Height;
        var frame = new WindowFrame(owner) { Workspace = new Border { Height = 80 } };
        owner.Content = frame;
        try
        {
            var minimize = Named<Button>(frame, "MinimizeButton");
            var maximize = Named<Button>(frame, "MaximizeButton");
            Assert.Equal(Visibility.Collapsed, minimize.Visibility);
            Assert.Equal(Visibility.Collapsed, maximize.Visibility);
            Assert.Equal(Visibility.Visible, Named<Button>(frame, "WindowCloseButton").Visibility);
            Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(owner).ResizeBorderThickness);
            frame.Measure(new Size(440, double.PositiveInfinity));
            // Layout rounding can add at most one physical pixel to each one-DIP frame edge.
            var edgeRounding = 2 / VisualTreeHelper.GetDpi(frame).DpiScaleY;
            Assert.InRange(frame.DesiredSize.Height, 80 + frame.CaptionHeight, 82 + frame.CaptionHeight + edgeRounding);
            Assert.Equal(SizeToContent.Height, owner.SizeToContent);

            owner.ResizeMode = ResizeMode.CanMinimize;
            Assert.Equal(Visibility.Visible, minimize.Visibility);
            Assert.False(maximize.IsEnabled);
            Assert.Equal(new Thickness(0), WindowChrome.GetWindowChrome(owner).ResizeBorderThickness);
            owner.ResizeMode = ResizeMode.CanResize;
            Assert.True(maximize.IsEnabled);
            Assert.Equal(new Thickness(6), WindowChrome.GetWindowChrome(owner).ResizeBorderThickness);
        }
        finally { owner.Close(); }
    });

    [Fact]
    public Task AppearanceKeepsResourcesPerOwnerAndPreservesExplicitShadowPreferences() => StaTest.Run(() =>
    {
        var first = NewOwner("First");
        var second = NewOwner("Second");
        var firstFrame = new WindowFrame(first) { Workspace = new TextBox { Text = "Draft" } };
        first.Content = firstFrame;
        second.Content = new WindowFrame(second);
        try
        {
            var secondBackground = second.Resources["ChromeBackgroundBrush"];
            UiAppearance.ApplyResources(first.Resources, "InkDark", "zh-TW", "Segoe UI", 18, false, 4);
            Assert.IsType<DrawingBrush>(first.Resources["ChromeBackgroundBrush"]);
            Assert.Equal("Segoe UI", ((FontFamily)first.Resources["UiFontFamily"]).Source);
            Assert.Equal(18d, first.Resources["UiFontSize"]);
            Assert.Equal(0, first.Resources["ChromeTextShadowThickness"]);
            Assert.Same(secondBackground, second.Resources["ChromeBackgroundBrush"]);

            UiAppearance.ApplyResources(first.Resources, "Light", "ja", "", 12, true, 4);
            Assert.IsType<SolidColorBrush>(first.Resources["ChromeBackgroundBrush"]);
            Assert.Equal(4, first.Resources["ChromeTextShadowThickness"]);
            Assert.Equal("Draft", ((TextBox)firstFrame.Workspace!).Text);
        }
        finally { first.Close(); second.Close(); }
    });

    private static Window NewOwner(string title)
    {
        var owner = new Window { Title = title, ResizeMode = ResizeMode.CanResize };
        UiAppearance.ApplyResources(owner.Resources, "Ink", "en", "Segoe UI", 16);
        owner.SetResourceReference(Control.FontFamilyProperty, "UiFontFamily");
        owner.SetResourceReference(Control.FontSizeProperty, "UiFontSize");
        return owner;
    }

    private static void Arrange(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement element, Visual host) =>
        element.TransformToAncestor(host).TransformBounds(new Rect(element.RenderSize));

    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).Single(element => element.Name == name);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T element) yield return element;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}