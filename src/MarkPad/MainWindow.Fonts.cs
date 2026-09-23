using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace MarkPad;

public partial class MainWindow
{
    private double UiSize(double size) => size * Settings.UiFontSize / 13;
    private double UiTitleHeight => Math.Max(40, UiSize(40));
    private string UiFontName => string.IsNullOrWhiteSpace(Settings.UiFontFamily)
        ? UiLanguage switch { "zh-TW" => "Microsoft JhengHei UI", "ja" => "Yu Gothic UI", _ => "Segoe UI" }
        : Settings.UiFontFamily;
    private string PreviewFontName => string.IsNullOrWhiteSpace(Settings.PreviewFontFamily)
        ? UiLanguage switch { "zh-TW" => "Microsoft JhengHei", "ja" => "Yu Gothic", _ => "Segoe UI" }
        : Settings.PreviewFontFamily;

    private void ApplyUiTypography()
    {
        FontFamily = new FontFamily(UiFontName);
        FontSize = Settings.UiFontSize;
        Resources["UiFontFamily"] = FontFamily;
        Resources["UiFontSize"] = FontSize;
        Resources["UiSmallFontSize"] = UiSize(9);
        Resources["UiHeadingFontSize"] = UiSize(17);
        Resources["UiSectionFontSize"] = UiSize(16);
        Resources["UiMenuMaxWidth"] = Math.Max(320, UiSize(360));
        BrandColumn.Width = new GridLength(Math.Max(104, UiSize(104)));
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
        var previewMode = _current.IsPreviewMode;
        var previous = previewMode ? Settings.PreviewFontSize : Settings.EditorFontSize;
        var size = Math.Clamp(previous + Math.Sign(e.Delta), 8, 72);
        if (size == previous) return;
        if (previewMode) Settings.PreviewFontSize = size;
        else Settings.EditorFontSize = size;

        await GuardAsync(async () =>
        {
            var updates = new List<Task>();
            foreach (var window in Application.Current.Windows.OfType<MainWindow>())
            {
                if (window._disposed) continue;
                foreach (var view in window._documentViews.Values)
                {
                    if (previewMode) updates.Add(view.Preview.SetFontSizeAsync(size));
                    else view.Editor.Editor.FontSize = size;
                }
            }
            App.Preferences.Save();
            await Task.WhenAll(updates);
        });
    }

    private void ShowFontPicker()
    {
        var body = new StackPanel { Margin = new Thickness(14) };
        var heading = new TextBlock { Text = T("Font & size", "字型與字級", "フォントとサイズ"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "UiSectionFontSize");
        body.Children.Add(heading);
        var allFonts = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(f => f).ToArray();
        void AddFontRow(string label, Func<string> getFont, Action<string> setFont, Func<double> getSize, Action<double> setSize, double min, double max)
        {
            body.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 5) });
            var common = new[] { "Segoe UI", "Microsoft JhengHei UI", "Microsoft JhengHei", "Yu Gothic UI", "Yu Gothic", "Cascadia Mono", "Consolas", "Arial" }
                .Where(f => allFonts.Contains(f)).ToList();
            var value = getFont();
            if (!common.Contains(value)) common.Insert(0, value);
            var combo = new ComboBox { ItemsSource = common, IsEditable = false, MinHeight = 30, SelectedItem = value, Margin = new Thickness(0, 0, 0, 3) };
            // The platform ComboBox style otherwise keeps its system font in this detached popup.
            combo.SetResourceReference(Control.FontFamilyProperty, "UiFontFamily");
            combo.SetResourceReference(Control.FontSizeProperty, "UiFontSize");
            System.Windows.Automation.AutomationProperties.SetName(combo, label);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is not string font || font == getFont()) return;
                setFont(font); App.ApplyPreferences();
            };
            body.Children.Add(combo);
            var more = new Button { Content = T("More fonts…", "更多字型…", "その他のフォント…"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 3, 0, 3) };
            more.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
            more.Click += (_, _) => { var selected = getFont(); combo.ItemsSource = allFonts; combo.SelectedItem = selected; combo.IsDropDownOpen = true; };
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
        AddFontRow(T("User interface", "UI 介面", "UI インターフェース"), () => UiFontName, v => Settings.UiFontFamily = v,
            () => Settings.UiFontSize, v => Settings.UiFontSize = v, 10, 20);
        AddFontRow(T("Document preview", "內文預覽", "本文プレビュー"), () => PreviewFontName, v => Settings.PreviewFontFamily = v,
            () => Settings.PreviewFontSize, v => Settings.PreviewFontSize = v, 8, 72);
        AddFontRow(T("Document editor", "內文編輯器", "本文エディター"), () => Settings.EditorFontFamily, v => Settings.EditorFontFamily = v,
            () => Settings.EditorFontSize, v => Settings.EditorFontSize = v, 8, 72);
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
