using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MarkPad.Editing;
using MarkPad.Services;
using ToolKeeper.UI;

namespace MarkPad;

public partial class MainWindow
{
    private sealed record HeadingOption(int Level, string Label);
    private static readonly Lazy<string[]> EditorToolbarFonts = new(() => Fonts.SystemFontFamilies
        .Select(font => font.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray());
    private static readonly Regex EditorHeadingPattern = new(@"^ {0,3}(#{1,6})(?:[ \t]+|$)", RegexOptions.Compiled);
    private ComboBox _editorFontCombo = null!, _editorHeadingCombo = null!;
    private TextBox _editorFontSizeBox = null!;
    private Button _editorSizeDown = null!, _editorSizeUp = null!;
    private WrapPanel _editorFormatButtons = null!;
    private bool _updatingEditorToolbar;
    private string? _editorToolbarLanguage;

    private bool CanFormatFromToolbar => !_disposed && _current is { IsPreviewMode: false, IsReadOnly: false }
        && _currentView is not null && !_editor.Editor.IsReadOnly;

    private void SetupEditorToolbar()
    {
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        EditorToolbarHost.Child = row;
        ComboBox Combo(string valuePath)
        {
            var combo = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = valuePath,
                MinHeight = 30, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 6, 3) };
            combo.SetResourceReference(StyleProperty, "FontPickerComboBoxStyle");
            row.Children.Add(combo);
            return combo;
        }
        _editorFontCombo = Combo(nameof(FontOption.Value));
        _editorFontCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingEditorToolbar || _editorFontCombo.SelectedValue is not string family
                || family == Settings.EditorFontFamily) return;
            Settings.EditorFontFamily = family;
            App.ApplyPreferences();
        };
        _editorFontCombo.DropDownClosed += (_, _) =>
        { if (!_updatingEditorToolbar && _current?.IsPreviewMode == false) _currentView?.Editor.FocusEditor(); };

        var sizes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 6, 3) };
        row.Children.Add(sizes);
        _editorSizeDown = new Button { Content = "−", Tag = "toolbar-size-down", Width = 28, Padding = new Thickness(3) };
        _editorSizeUp = new Button { Content = "+", Tag = "toolbar-size-up", Width = 28, Padding = new Thickness(3) };
        _editorFontSizeBox = new TextBox { Width = 44, Padding = new Thickness(3), TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center };
        sizes.Children.Add(_editorSizeDown); sizes.Children.Add(_editorFontSizeBox); sizes.Children.Add(_editorSizeUp);
        _editorSizeDown.Click += async (_, _) => await GuardAsync(() => ChangeContentFontSizeAsync(false, Settings.EditorFontSize - 1));
        _editorSizeUp.Click += async (_, _) => await GuardAsync(() => ChangeContentFontSizeAsync(false, Settings.EditorFontSize + 1));
        _editorFontSizeBox.LostKeyboardFocus += async (_, _) => await CommitEditorToolbarFontSizeAsync();
        _editorFontSizeBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await CommitEditorToolbarFontSizeAsync();
            if (_current?.IsPreviewMode == false) _currentView?.Editor.FocusEditor();
        };

        _editorHeadingCombo = Combo(nameof(HeadingOption.Level));
        _editorHeadingCombo.Width = 88;
        var headingItemStyle = new Style(typeof(ComboBoxItem), (Style)FindResource("FontPickerItemStyle"));
        var mixedItem = new DataTrigger { Binding = new Binding(nameof(HeadingOption.Level)), Value = -1 };
        mixedItem.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        mixedItem.Setters.Add(new Setter(IsEnabledProperty, false));
        headingItemStyle.Triggers.Add(mixedItem);
        _editorHeadingCombo.ItemContainerStyle = headingItemStyle;
        _editorHeadingCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingEditorToolbar || _editorHeadingCombo.SelectedValue is not int level || level < 0) return;
            _editorHeadingCombo.IsDropDownOpen = false;
            FormatFromToolbar(editor => editor.SetHeading(level));
        };
        _editorFormatButtons = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 6, 3) };
        row.Children.Add(_editorFormatButtons);
        void FormatButton(string label, string tag, Action<EditorPane> action, FontWeight? weight = null, FontStyle? style = null)
        {
            var button = new Button { Content = label, Tag = tag, Width = 30, Padding = new Thickness(4, 3, 4, 3),
                FontFamily = new FontFamily("Segoe UI"), FontWeight = weight ?? FontWeights.Normal,
                FontStyle = style ?? FontStyles.Normal };
            button.Click += (_, _) => FormatFromToolbar(action);
            _editorFormatButtons.Children.Add(button);
        }
        FormatButton("B", "toolbar-bold", editor => editor.WrapSelection("**", "**"), FontWeights.Bold);
        FormatButton("I", "toolbar-italic", editor => editor.WrapSelection("*", "*"), style: FontStyles.Italic);
        FormatButton("S̶", "toolbar-strike", editor => editor.WrapSelection("~~", "~~"));
        FormatButton("`", "toolbar-code", editor => editor.WrapSelection("`", "`"));
        FormatButton("↗", "toolbar-link", editor => editor.InsertLink());
    }

    private void RefreshEditorToolbar()
    {
        if (_editorFontCombo is null) return;
        _updatingEditorToolbar = true;
        try
        {
            if (_editorToolbarLanguage != UiLanguage)
            {
                _editorToolbarLanguage = UiLanguage;
                var fonts = new List<FontOption>
                {
                    new("Cascadia Mono", T("Automatic (mono)", "自動（等寬）", "自動（等幅）")),
                    new(InkTypography.FontChoice, T("Traditional", "傳統文字", "伝統書体"))
                };
                fonts.AddRange(EditorToolbarFonts.Value.Where(name => name != "Cascadia Mono").Select(name => new FontOption(name, name)));
                _editorFontCombo.ItemsSource = fonts;
                _editorHeadingCombo.ItemsSource = Enumerable.Range(-1, 8)
                    .Select(level => new HeadingOption(level, level == -1 ? T("Mixed", "混合", "混在")
                        : level == 0 ? T("Body", "內文", "本文") : "H" + level)).ToArray();
            }
            var choices = (List<FontOption>)_editorFontCombo.ItemsSource;
            if (!choices.Any(option => option.Value == Settings.EditorFontFamily))
            {
                choices = [.. choices, new FontOption(Settings.EditorFontFamily, Settings.EditorFontFamily)];
                _editorFontCombo.ItemsSource = choices;
            }
            _editorFontCombo.SelectedValue = Settings.EditorFontFamily;
            _editorFontCombo.Width = Math.Clamp(UiSize(146), 146, 200);
            _editorFontSizeBox.Text = Settings.EditorFontSize.ToString(CultureInfo.InvariantCulture);
            _editorSizeDown.IsEnabled = Settings.EditorFontSize > 8;
            _editorSizeUp.IsEnabled = Settings.EditorFontSize < 72;
            var height = Math.Max(30, UiSize(26));
            _editorFontCombo.MinHeight = _editorHeadingCombo.MinHeight = _editorFontSizeBox.MinHeight = height;
            _editorSizeDown.MinHeight = _editorSizeUp.MinHeight = height;
            foreach (var button in _editorFormatButtons.Children.OfType<Button>()) button.MinHeight = height;
            Describe(_editorFontCombo, T("Editor font", "編輯字型", "編集フォント"),
                T("Changes the editor display font; document text stays unchanged.", "調整編輯顯示字型，不改變文件文字。", "編集表示のフォントを変更します。文書の文字は変更しません。") + "\n" + EditorFontName);
            Describe(_editorFontSizeBox, T("Editor font size", "編輯字級", "編集文字サイズ"), "8–72");
            Describe(_editorSizeDown, T("Decrease editor font size", "縮小編輯字級", "編集文字を小さく"));
            Describe(_editorSizeUp, T("Increase editor font size", "放大編輯字級", "編集文字を大きく"));
            Describe(_editorHeadingCombo, T("Paragraph style", "段落樣式", "段落スタイル"),
                T("Body / Markdown heading", "內文／Markdown 標題", "本文／Markdown 見出し"));
            var descriptions = new[]
            {
                T("Bold · Ctrl+B", "粗體 · Ctrl+B", "太字 · Ctrl+B"),
                T("Italic · Ctrl+I", "斜體 · Ctrl+I", "斜体 · Ctrl+I"),
                T("Strikethrough", "刪除線", "取り消し線"),
                T("Inline code", "行內程式碼", "インラインコード"),
                T("Link", "連結", "リンク")
            };
            for (var i = 0; i < descriptions.Length; i++) Describe((Button)_editorFormatButtons.Children[i], descriptions[i]);
            AutomationProperties.SetName(EditorToolbarHost, T("Editor toolbar", "編輯工具列", "編集ツールバー"));
        }
        finally { _updatingEditorToolbar = false; }
        UpdateEditorToolbarSelection();
    }

    private static void Describe(FrameworkElement control, string name, string? help = null)
    {
        control.ToolTip = help is null ? name : name + "\n" + help;
        AutomationProperties.SetName(control, name);
        if (help is not null) AutomationProperties.SetHelpText(control, help);
    }

    private void UpdateEditorToolbarSelection()
    {
        if (_editorFontCombo is null || _updatingEditorToolbar) return;
        var editing = _current?.IsPreviewMode == false && _currentView is not null;
        EditorToolbarHost.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        _editorHeadingCombo.IsEnabled = _editorFormatButtons.IsEnabled = CanFormatFromToolbar;
        _updatingEditorToolbar = true;
        try
        {
            if (!editing)
            {
                _editorFontCombo.IsDropDownOpen = _editorHeadingCombo.IsDropDownOpen = false;
                return;
            }
            var editor = _editor.Editor;
            var first = editor.Document.GetLineByOffset(editor.SelectionStart).LineNumber;
            var last = editor.Document.GetLineByOffset(editor.SelectionStart + Math.Max(0, editor.SelectionLength - 1)).LineNumber;
            // Bound work while dragging selections in very large files. A mixed/unspecified
            // state still lets any heading command apply to the complete selected range.
            var level = -1;
            if (last - first < 128)
                for (var number = first; number <= last; number++)
                {
                    var line = editor.Document.GetLineByNumber(number);
                    var heading = EditorHeadingPattern.Match(editor.Document.GetText(line.Offset, Math.Min(10, line.Length)));
                    var next = heading.Success ? heading.Groups[1].Length : 0;
                    if (number != first && level != next) { level = -1; break; }
                    level = next;
                }
            _editorHeadingCombo.SelectedValue = level;
        }
        finally { _updatingEditorToolbar = false; }
    }

    private void FormatFromToolbar(Action<EditorPane> action)
    {
        if (!CanFormatFromToolbar) return;
        _updatingEditorToolbar = true;
        try { action(_editor); }
        catch (Exception ex) { Report(ex); }
        finally { _updatingEditorToolbar = false; UpdateEditorToolbarSelection(); }
    }

    private async Task CommitEditorToolbarFontSizeAsync()
    {
        if (_updatingEditorToolbar || _disposed) return;
        if (double.TryParse(_editorFontSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value))
            await GuardAsync(() => ChangeContentFontSizeAsync(false, value));
        _editorFontSizeBox.Text = Settings.EditorFontSize.ToString(CultureInfo.InvariantCulture);
    }

    private bool HandleEditorToolbarEscape()
    {
        if (_editorFontCombo is null || !EditorToolbarHost.IsVisible) return false;
        if (!_editorFontCombo.IsDropDownOpen && !_editorHeadingCombo.IsDropDownOpen && !_editorFontSizeBox.IsKeyboardFocusWithin) return false;
        _editorFontSizeBox.Text = Settings.EditorFontSize.ToString(CultureInfo.InvariantCulture);
        _editorFontCombo.IsDropDownOpen = _editorHeadingCombo.IsDropDownOpen = false;
        _currentView?.Editor.FocusEditor();
        return true;
    }
}
