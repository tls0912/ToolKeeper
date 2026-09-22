using System.Windows;
using System.Windows.Media;
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

    public static void Apply(Window window)
    {
        window.FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI");
        window.FontSize = 14;
        window.Foreground = Ink;
        window.Background = Surface;
        var button = new Style(typeof(Controls.Button));
        button.Setters.Add(new Setter(Controls.Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
        button.Setters.Add(new Setter(Controls.Control.BackgroundProperty, Brushes.White));
        button.Setters.Add(new Setter(Controls.Control.BorderBrushProperty, Line));
        button.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        window.Resources.Add(typeof(Controls.Button), button);
        var box = new Style(typeof(Controls.TextBox));
        box.Setters.Add(new Setter(Controls.Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
        box.Setters.Add(new Setter(Controls.Control.BorderBrushProperty, Line));
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
