using System.Windows;
using System.Windows.Media;
using ToolKeeper.UI;
using Controls = System.Windows.Controls;

namespace CabiDock.Views;

internal static class ViewTheme
{
    public static readonly SolidColorBrush Ink = Brush("#192C37");
    public static readonly SolidColorBrush Muted = Brush("#64747D");
    public static readonly SolidColorBrush Accent = Brush("#176A62");
    public static readonly SolidColorBrush Line = Brush("#D8E1E3");
    public static readonly SolidColorBrush Surface = Brush("#F5F8F8");

    public static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }

    public static void Apply(Window window, string theme = "Ink")
    {
        window.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        window.FontSize = 14;
        ApplyResources(window.Resources, theme);
        window.SetResourceReference(Controls.Control.ForegroundProperty, "TextBrush");
        window.SetResourceReference(Controls.Control.BackgroundProperty, "DesktopWindowBrush");
        ApplyControlStyles(window);
    }

    // Cards retain these resources when reparented into a native desktop or preview host.
    public static void ApplyResources(ResourceDictionary resources, string theme)
    {
        var bamboo = UiTheme.IsInk(theme);
        UiTheme.ApplyResources(resources, theme);
        if (!bamboo)
        {
            resources["WindowBackground"] = Surface;
            resources["SurfaceBrush"] = Brushes.White;
            resources["TextBrush"] = Ink;
            resources["MutedBrush"] = Muted;
            resources["AccentBrush"] = Accent;
            resources["LineBrush"] = Line;
            resources["HoverBrush"] = Brush("#E5EFED");
        }
        BambooChrome.ApplyResources(resources, bamboo, UiTheme.IsDark(theme));
        resources["DesktopTheme"] = theme;
        resources["DesktopWindowBrush"] = bamboo ? resources["ChromeBackgroundBrush"] : Surface;
        resources["DesktopCardBrush"] = bamboo ? resources["ChromeBackgroundBrush"] : Brush("#F7FAFA");
        resources["DesktopCardLineBrush"] = bamboo ? resources["LineBrush"] : Brush("#BDD3D0");
        resources["DesktopHeaderBrush"] = bamboo ? resources["ChromeTitleBrush"] : Brush("#EAF2F1");
        resources["DesktopContentBrush"] = bamboo ? resources["PaperBackgroundBrush"] : Brushes.Transparent;
        resources["DesktopPreviewWindowBrush"] = bamboo ? resources["ChromeBackgroundBrush"] : SystemColors.WindowBrush;
        resources["DesktopPreviewHeaderBrush"] = bamboo ? resources["ChromeTitleBrush"] : Brushes.Transparent;
        resources["DesktopPreviewTextBrush"] = bamboo ? resources["TextBrush"] : SystemColors.WindowTextBrush;
        resources["DesktopPreviewMutedBrush"] = bamboo ? resources["MutedBrush"] : Brushes.DimGray;
        resources["DesktopPreviewBrush"] = bamboo ? resources["PaperBackgroundBrush"] : Brush("#E8F0ED");
    }

    public static void ApplyControlStyles(Window window)
    {
        var button = new Style(typeof(Controls.Button));
        button.Setters.Add(new Setter(Controls.Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
        button.Setters.Add(new Setter(Controls.Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")));
        button.Setters.Add(new Setter(Controls.Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        button.Setters.Add(new Setter(Controls.Control.BorderBrushProperty, new DynamicResourceExtension("LineBrush")));
        button.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        window.Resources.Add(typeof(Controls.Button), button);
        var box = new Style(typeof(Controls.TextBox));
        box.Setters.Add(new Setter(Controls.Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
        box.Setters.Add(new Setter(Controls.Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")));
        box.Setters.Add(new Setter(Controls.Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
        box.Setters.Add(new Setter(Controls.TextBox.CaretBrushProperty, new DynamicResourceExtension("TextBrush")));
        box.Setters.Add(new Setter(Controls.Control.BorderBrushProperty, new DynamicResourceExtension("LineBrush")));
        box.Setters.Add(new Setter(Controls.Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        window.Resources.Add(typeof(Controls.TextBox), box);
    }

    public static Controls.TextBlock Text(string text, double size = 14, Brush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ink,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };

    public static Controls.Button Button(string label, RoutedEventHandler clicked, bool primary = false)
    {
        var button = new Controls.Button { Content = label };
        if (primary)
        {
            button.Background = Accent;
            button.Foreground = Brushes.White;
            button.BorderBrush = Accent;
        }
        button.Click += clicked;
        return button;
    }
}
