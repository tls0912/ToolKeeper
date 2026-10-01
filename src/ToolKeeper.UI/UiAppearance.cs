using System.Windows;
using System.Windows.Media;

namespace ToolKeeper.UI;

/// <summary>Applies one owner's shared interface resources without owning settings or global state.</summary>
public static class UiAppearance
{
    public static void ApplyResources(ResourceDictionary resources, string theme, string resolvedLanguage,
        string? fontChoice, double fontSize, bool shadowEnabled = true, int shadowThickness = 1)
    {
        ArgumentNullException.ThrowIfNull(resources);
        EnsureResources(resources);
        var dark = UiTheme.IsDark(theme);
        var ink = UiTheme.IsInk(theme);
        UiTheme.ApplyResources(resources, theme, dark);
        BambooChrome.ApplyResources(resources, ink, dark);
        UiTypography.ApplyResources(resources,
            new FontFamily(UiTypography.ResolveInterfaceFont(fontChoice, resolvedLanguage, ink)), fontSize);
        ChromeTextShadow.ApplyResources(resources, dark, shadowEnabled, shadowThickness);
        resources["SuccessBrush"] = Solid(dark ? "#80C99D" : "#1C704D");
        resources["ErrorBrush"] = Solid(dark ? "#F19A97" : "#AA3434");
        resources["InkTitleStrokeVisibility"] = ink ? Visibility.Visible : Visibility.Collapsed;
        var accent = ((SolidColorBrush)resources["AccentBrush"]).Color;
        resources["InkTitleStrokeBrush"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, accent.R, accent.G, accent.B), 0),
            new(Color.FromArgb(153, accent.R, accent.G, accent.B), 0.22),
            new(Color.FromArgb(64, accent.R, accent.G, accent.B), 0.85),
            new(Color.FromArgb(0, accent.R, accent.G, accent.B), 1)
        }, new Point(0, 0), new Point(1, 0));
    }

    internal static void EnsureResources(ResourceDictionary resources)
    {
        if (resources.Contains("UiButtonStyle")) return;
        resources.MergedDictionaries.Insert(0, new ResourceDictionary
        {
            Source = new Uri("/ToolKeeper.UI;component/Resources/UiStyles.xaml", UriKind.Relative)
        });
    }

    private static SolidColorBrush Solid(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}