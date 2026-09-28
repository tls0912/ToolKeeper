using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using MarkPad.Services;
using ToolKeeper.UI;

namespace MarkPad;

public partial class MainWindow
{
    private double UiSize(double size) => size * Settings.UiFontSize / 13;
    private double UiTitleHeight => Math.Max(40, UiSize(40));
    private string UiFontName => UiTypography.ResolveInterfaceFont(Settings.UiFontFamily, UiLanguage, IsInkTheme);
    private string PreviewFontName => UiTypography.ResolveReadingFont(Settings.PreviewFontFamily, UiLanguage, IsInkTheme);
    private string EditorFontName => Settings.EditorFontFamily == InkTypography.FontChoice
        ? InkTypography.Resolve(UiLanguage) : Settings.EditorFontFamily;

    private sealed record FontOption(string Value, string Label);

    private void ApplyUiTypography()
    {
        FontFamily = new FontFamily(UiFontName);
        FontSize = Settings.UiFontSize;
        UiTypography.ApplyResources(Resources, FontFamily, FontSize);
        RailToggleButton.Height = Math.Max(32, UiSize(32));
        StatusToast.Margin = new Thickness(20, UiTitleHeight + 24, 20, 0);
        TitleRow.Height = new GridLength(_fullScreen ? 0 : UiTitleHeight);
        TitleBar.Height = _fullScreen ? UiTitleHeight : double.NaN;
        if (WindowChrome.GetWindowChrome(this) is { } chrome) chrome.CaptionHeight = _fullScreen ? 0 : UiTitleHeight;
    }

    private async void OnContentMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_disposed || _current is null || Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        // Intercept before AvalonEdit/WebView2 scrolls or applies its own zoom.
        e.Handled = true;
        var previewMode = _current.IsPreviewMode || _preview.IsMouseOver;
        var previous = previewMode ? Settings.PreviewFontSize : Settings.EditorFontSize;
        await GuardAsync(() => ChangeContentFontSizeAsync(previewMode, previous + Math.Sign(e.Delta)));
    }

    private async Task ChangeContentFontSizeAsync(bool previewMode, double requestedSize)
    {
        if (_disposed || !double.IsFinite(requestedSize)) return;
        var previous = previewMode ? Settings.PreviewFontSize : Settings.EditorFontSize;
        var size = Math.Clamp(requestedSize, 8, 72);
        if (size == previous) return;
        if (previewMode) Settings.PreviewFontSize = size;
        else Settings.EditorFontSize = size;
        var updates = new List<Task>();
        foreach (var window in Application.Current.Windows.OfType<MainWindow>())
        {
            if (window._disposed) continue;
            foreach (var view in window._documentViews.Values)
            {
                if (previewMode) updates.Add(view.Preview.SetFontSizeAsync(size));
                else view.Editor.Editor.FontSize = size;
            }
            window.RefreshEditorToolbar();
        }
        App.Preferences.Save();
        await Task.WhenAll(updates);
    }

    private void ShowFontPicker()
    {
        var body = new StackPanel { Margin = new Thickness(14) };
        var heading = new TextBlock { Text = T("Font & size", "字型與字級", "フォントとサイズ"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "UiSectionFontSize");
        body.Children.Add(heading);
        var traditionalNote = new TextBlock
        {
            Text = T("Traditional uses an installed calligraphic or serif font. Automatic pairs it with the bamboo interface and paper background in both light and dark themes.",
                "傳統文字使用本機楷體或襯線字型；介面與內文預覽選「自動」時，會搭配亮色或深色的竹子介面與宣紙底色。",
                "伝統書体はインストール済みの楷書体・明朝体を使用します。UI と本文プレビューの「自動」は、明暗両方の竹のインターフェースと紙の背景に合わせます。"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
        };
        traditionalNote.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        body.Children.Add(traditionalNote);
        var allFonts = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToArray();
        void AddFontRow(string label, Func<string> getFont, Action<string> setFont, Func<string> resolvedFont,
            Func<double> getSize, Action<double> setSize, double min, double max, string defaultValue = "")
        {
            body.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 5) });
            var common = new[] { "Segoe UI", "Microsoft JhengHei UI", "Microsoft JhengHei", "Yu Gothic UI", "Yu Gothic", "Cascadia Mono", "Consolas", "Arial" }
                .Where(f => allFonts.Contains(f, StringComparer.OrdinalIgnoreCase));
            List<FontOption> Options(IEnumerable<string> fonts)
            {
                var options = new List<FontOption>
                {
                    new(defaultValue, T("Automatic", "自動", "自動")),
                    new(InkTypography.FontChoice, T("Traditional", "傳統文字", "伝統書体"))
                };
                options.AddRange(fonts.Where(f => f != defaultValue).Select(f => new FontOption(f, f)));
                var selected = getFont();
                if (!options.Any(f => f.Value == selected)) options.Add(new FontOption(selected, selected));
                return options;
            }
            var value = getFont();
            var combo = new ComboBox { ItemsSource = Options(common), DisplayMemberPath = nameof(FontOption.Label), SelectedValuePath = nameof(FontOption.Value),
                IsEditable = false, MinHeight = 30, SelectedValue = value, Margin = new Thickness(0, 0, 0, 3) };
            combo.SetResourceReference(StyleProperty, "FontPickerComboBoxStyle");
            // The platform ComboBox style otherwise keeps its system font in this detached popup.
            combo.SetResourceReference(Control.FontFamilyProperty, "UiFontFamily");
            combo.SetResourceReference(Control.FontSizeProperty, "UiFontSize");
            System.Windows.Automation.AutomationProperties.SetName(combo, label);
            var sample = new TextBlock { Text = T("Traditional lettering · Abc 123", "傳統文字 · Abc 123", "伝統書体 · Abc 123"),
                TextWrapping = TextWrapping.Wrap, FontSize = 18, Margin = new Thickness(0, 3, 0, 0) };
            var familyName = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 3) };
            familyName.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            void UpdateSample()
            {
                var family = resolvedFont();
                sample.FontFamily = new FontFamily(family);
                familyName.Text = family;
            }
            UpdateSample();
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedValue is not string font || font == getFont()) return;
                setFont(font); App.ApplyPreferences(); UpdateSample();
            };
            body.Children.Add(combo);
            body.Children.Add(sample);
            body.Children.Add(familyName);
            var more = new Button { Content = T("More fonts…", "更多字型…", "その他のフォント…"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 3, 0, 3) };
            more.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
            more.Click += (_, _) => { var selected = getFont(); combo.ItemsSource = Options(allFonts); combo.SelectedValue = selected; combo.IsDropDownOpen = true; };
            body.Children.Add(more);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 8) };
            var size = new TextBox { Text = getSize().ToString(CultureInfo.InvariantCulture), Width = 66, TextAlignment = TextAlignment.Center,
                ToolTip = $"{min}–{max}" };
            System.Windows.Automation.AutomationProperties.SetName(size, label + " · " + T("Font size", "字級", "サイズ"));
            void Commit(double? explicitValue = null)
            {
                if (!explicitValue.HasValue && !double.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                { size.Text = getSize().ToString(CultureInfo.InvariantCulture); return; }
                var requested = explicitValue ?? double.Parse(size.Text, CultureInfo.InvariantCulture);
                if (!double.IsFinite(requested)) { size.Text = getSize().ToString(CultureInfo.InvariantCulture); return; }
                var number = Math.Clamp(requested, min, max);
                size.Text = number.ToString(CultureInfo.InvariantCulture);
                if (number == getSize()) return;
                setSize(number); App.ApplyPreferences();
            }
            var minus = new Button { Content = "−", Width = 38 }; var plus = new Button { Content = "+", Width = 38 };
            System.Windows.Automation.AutomationProperties.SetName(minus, label + " · " + T("Decrease font size", "縮小字級", "文字を小さく"));
            System.Windows.Automation.AutomationProperties.SetName(plus, label + " · " + T("Increase font size", "放大字級", "文字を大きく"));
            minus.Click += (_, _) => Commit(getSize() - 1); plus.Click += (_, _) => Commit(getSize() + 1);
            size.LostKeyboardFocus += (_, _) => Commit();
            size.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            row.Children.Add(minus); row.Children.Add(size); row.Children.Add(plus); body.Children.Add(row);
        }
        AddFontRow(T("User interface", "UI 介面", "UI インターフェース"), () => Settings.UiFontFamily, v => Settings.UiFontFamily = v, () => UiFontName,
            () => Settings.UiFontSize, v => Settings.UiFontSize = v, 10, 20);
        AddFontRow(T("Document preview", "內文預覽", "本文プレビュー"), () => Settings.PreviewFontFamily, v => Settings.PreviewFontFamily = v, () => PreviewFontName,
            () => Settings.PreviewFontSize, v => Settings.PreviewFontSize = v, 8, 72);
        AddFontRow(T("Document editor", "內文編輯器", "本文エディター"), () => Settings.EditorFontFamily, v => Settings.EditorFontFamily = v, () => EditorFontName,
            () => Settings.EditorFontSize, v => Settings.EditorFontSize = v, 8, 72, "Cascadia Mono");
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Keep the three sections reachable in a short window, including when UI size changes live.
        void FitPicker()
        {
            scroll.Width = Math.Max(288, UiSize(288));
            scroll.MaxHeight = Math.Max(200, Math.Min(ActualHeight - UiTitleHeight - 32, SystemParameters.WorkArea.Height - 80));
        }
        body.SizeChanged += (_, _) => FitPicker();
        FitPicker(); ShowFlyout(scroll);
    }
}
