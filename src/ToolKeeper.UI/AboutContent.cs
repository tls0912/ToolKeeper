using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ToolKeeper.UI;

/// <summary>Creates product About content; the application owns its popup, focus, and sizing constraints.</summary>
public static class AboutContent
{
    public static FrameworkElement Create(AboutInfo info, string language, Action close, double uiFontSize = 16)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(close);

        var body = new StackPanel
        {
            Margin = new Thickness(20),
            Width = Math.Max(260, 260 * uiFontSize / 13)
        };
        NameScope.SetNameScope(body, new NameScope());
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (info.Icon is not null)
            heading.Children.Add(new Image
            {
                Source = info.Icon,
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 10, 0)
            });
        var title = new TextBlock
        {
            Text = $"{info.ProductName} {info.Version}",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.FontSizeProperty, "UiSectionFontSize");
        heading.Children.Add(title);
        body.Children.Add(heading);

        var brand = new TextBlock { Text = info.Brand, Margin = new Thickness(0, 0, 0, 10) };
        brand.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(brand);
        body.Children.Add(new TextBlock
        {
            Text = info.Description,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        if (!string.IsNullOrWhiteSpace(info.Author))
            body.Children.Add(new TextBlock
            {
                Text = UiLanguage.Format(language, "Author: {0}", "作者：{0}", "作者：{0}", info.Author),
                TextWrapping = TextWrapping.Wrap
            });

        var closeButton = new Button
        {
            Name = "AboutCloseButton",
            Content = UiLanguage.Text(language, "Close", "關閉", "閉じる"),
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 70,
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(14, 6, 14, 6)
        };
        closeButton.Click += (_, _) => close();
        body.Children.Add(closeButton);
        body.RegisterName(closeButton.Name, closeButton);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            close();
            e.Handled = true;
        };
        return body;
    }
}
