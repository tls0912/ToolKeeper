using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace MarkPad.Theming;

/// <summary>A single offset copy behind a short, plain-text interface label.</summary>
[ContentProperty(nameof(Label))]
public sealed class ChromeTextShadow : Grid
{
    private static readonly DependencyProperty[] MirroredProperties =
    [
        TextBlock.TextProperty, TextBlock.FontFamilyProperty, TextBlock.FontSizeProperty,
        TextBlock.FontWeightProperty, TextBlock.FontStyleProperty, TextBlock.FontStretchProperty,
        TextBlock.TextAlignmentProperty, TextBlock.TextTrimmingProperty, TextBlock.TextWrappingProperty,
        TextBlock.TextDecorationsProperty, TextBlock.LineHeightProperty, TextBlock.LineStackingStrategyProperty,
        TextBlock.PaddingProperty, FlowDirectionProperty, LanguageProperty,
        HorizontalAlignmentProperty, VerticalAlignmentProperty, MarginProperty,
        WidthProperty, HeightProperty, MinWidthProperty, MinHeightProperty, MaxWidthProperty, MaxHeightProperty,
        VisibilityProperty, OpacityProperty
    ];
    private static readonly Brush LightShadow = ShadowBrush(40);
    private static readonly Brush DarkShadow = ShadowBrush(96);
    private TextBlock? _label;

    public ChromeTextShadow() { }
    public ChromeTextShadow(TextBlock label) => Label = label;

    public TextBlock? Label
    {
        get => _label;
        set
        {
            Children.Clear();
            _label = value;
            if (value is null) return;
            // Keep the foreground on WPF's normal text path; only the copy is offset.
            var shadow = new DecorativeTextBlock
            {
                IsHitTestVisible = false, Focusable = false,
                RenderTransform = new TranslateTransform(1, 1)
            };
            shadow.SetResourceReference(TextBlock.ForegroundProperty, "ChromeTextShadowBrush");
            foreach (var property in MirroredProperties)
                shadow.SetBinding(property, new Binding { Source = value, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
            Children.Add(shadow);
            Children.Add(value);
        }
    }

    internal static void ApplyResources(ResourceDictionary resources, bool dark)
        => resources["ChromeTextShadowBrush"] = dark ? DarkShadow : LightShadow;

    private static Brush ShadowBrush(byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private sealed class DecorativeTextBlock : TextBlock
    {
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
    }
}

public sealed class ChromeButtonTextSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    // Compound button content already wraps its text leaves; never duplicate UIElements.
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is string ? TextTemplate : null;
}
