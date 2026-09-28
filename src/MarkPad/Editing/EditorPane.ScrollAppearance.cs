using System.Windows;
using System.Windows.Media;
using ToolKeeper.UI;

namespace MarkPad.Editing;

public sealed partial class EditorPane
{
    private void ConfigureScrollAppearance() => Editor.Resources.MergedDictionaries.Add(new ResourceDictionary
    {
        Source = new Uri("/Hanqing;component/Resources/EditorScrollStyles.xaml", UriKind.Relative)
    });

    private void UpdateScrollAppearance(bool dark, bool ink)
    {
        // Match Preview.css's scrollbar-color: var(--border) var(--bg).
        var palette = UiTheme.Palette(dark, ink);
        Editor.Resources["EditorScrollTrackBrush"] = new SolidColorBrush(palette.Surface);
        Editor.Resources["EditorScrollThumbBrush"] = new SolidColorBrush(palette.Line);
        byte Blend(byte border, byte text) => (byte)Math.Round(border * 0.7 + text * 0.3);
        Editor.Resources["EditorScrollHoverBrush"] = new SolidColorBrush(Color.FromRgb(
            Blend(palette.Line.R, palette.Text.R), Blend(palette.Line.G, palette.Text.G), Blend(palette.Line.B, palette.Text.B)));
    }
}
