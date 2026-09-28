using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace CabiDock.Views;

public sealed partial class SettingsWindow
{
    private readonly List<Action> _localizedControls = [];
    private Func<string>? _statusText;
    private bool _desktopEnabled = true;
    private bool _desktopAvailable = true;

    // Only constructor-created interface labels are registered. Names, extensions,
    // keywords and the user's live DataGrid editors are never translated/rebuilt.
    private static readonly Dictionary<string, (string English, string Japanese)> Labels = new()
    {
        ["開啟群組操作預覽"] = ("Group preview", "グループプレビュー"),
        ["分類名稱"] = ("Category name", "分類名"),
        ["種類"] = ("Kind", "種類"),
        ["副檔名（以逗號或空白分隔）"] = ("Extensions (comma or space separated)", "拡張子（カンマ・空白区切り）"),
        ["分類"] = ("Categories", "分類"),
        ["自訂分類的副檔名優先；資料夾與「其他」保留特殊判定。"] = ("Custom extensions take priority. Folders and Other keep their special rules.", "カスタム拡張子を優先します。フォルダーと「その他」は特別な判定を保持します。"),
        ["＋ 新增分類"] = ("＋ Add category", "＋ 分類を追加"),
        ["刪除自訂分類"] = ("Delete custom", "カスタム分類を削除"),
        ["排序"] = ("Order", "順序"),
        ["檔名包含"] = ("Filename contains", "ファイル名に含む"),
        ["目標分類"] = ("Target category", "対象分類"),
        ["檔名關鍵字規則"] = ("Filename keyword rules", "ファイル名キーワードルール"),
        ["由上至下比對，第一個命中者生效；僅比對檔名，不讀取內容。"] = ("The first matching rule wins. Only filenames are checked; file contents are not read.", "上から最初に一致したルールを適用します。ファイル名のみを照合し、内容は読み取りません。"),
        ["＋ 新增規則"] = ("＋ Add rule", "＋ ルールを追加"),
        ["刪除規則"] = ("Delete rule", "ルールを削除"),
        ["分類區不透明度"] = ("Group opacity", "グループの不透明度"),
        ["100% 為完全不透明；儲存後套用至所有分類區。"] = ("100% is fully opaque. Save to apply to all groups.", "100% は完全に不透明です。保存すると全グループに適用します。"),
        ["儲存並套用"] = ("Save and apply", "保存して適用")
    };

    private void InitializePreferences()
    {
        foreach (var element in InterfaceElements((DependencyObject)Workspace!))
        {
            if (element is TextBlock text)
            {
                RegisterTranslation(text, TextBlock.TextProperty, text.Text);
                // ViewTheme.Text supplies local brushes for the desktop views. The
                // settings window instead follows its own selected palette.
                var muted = text.Foreground is SolidColorBrush brush && brush.Color == ViewTheme.Muted.Color;
                text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
            }
            if (element is Button button)
            {
                if (button.Content is string label) RegisterTranslation(button, ContentControl.ContentProperty, label);
                button.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
                button.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                button.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
            }
            if (element is FrameworkElement { ToolTip: string help } control)
                RegisterTranslation(control, FrameworkElement.ToolTipProperty, help);
        }
        foreach (var grid in new[] { _categoryGrid, _ruleGrid })
        {
            grid.SetResourceReference(Control.BackgroundProperty, "SurfaceBrush");
            grid.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            grid.SetResourceReference(Control.BorderBrushProperty, "LineBrush");
            grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "LineBrush");
            grid.SetResourceReference(DataGrid.RowBackgroundProperty, "SurfaceBrush");
            grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "WindowBackground");
            foreach (var column in grid.Columns)
            {
                if (column.Header is string label)
                {
                    var header = new TextBlock { Text = label };
                    header.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    column.Header = header;
                    RegisterTranslation(header, TextBlock.TextProperty, label);
                }
                // DataGridTextColumn supplies an explicit default editing style;
                // implicit window TextBox styles cannot override that style.
                if (column is DataGridTextColumn textColumn)
                {
                    var display = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle);
                    display.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
                    textColumn.ElementStyle = display;
                    textColumn.EditingElementStyle = (Style)FindResource(typeof(TextBox));
                }
            }
        }
        _groupOpacity.SetResourceReference(Control.ForegroundProperty, "AccentBrush");
        _groupOpacity.SetResourceReference(Control.BackgroundProperty, "LineBrush");
        SetLocalizedStatus("Preparing desktop control; groups will appear on the desktop.", "正在準備桌面接管；群組會顯示在桌面上。", "デスクトップ制御を準備しています。グループはデスクトップに表示されます。");
        UiPreferencesChanged += (_, _) => RefreshPreferences();
        RefreshPreferences();
    }

    private void RegisterTranslation(DependencyObject target, DependencyProperty property, string chinese)
    {
        if (Labels.TryGetValue(chinese, out var label))
            _localizedControls.Add(() => target.SetCurrentValue(property, T(label.English, chinese, label.Japanese)));
    }

    private void RefreshPreferences()
    {
        Description = T("Desktop files grouped automatically, tucked away until you need them.",
            "桌面檔案自動分組，平時收起，需要時點開。", "デスクトップのファイルを自動分類。普段は折りたたみ、必要なときに開けます。");
        foreach (var update in _localizedControls) update();
        foreach (var category in _categories) category.SetLanguage(ResolvedLanguage);
        Resources["ExtensionsHelp"] = T("Separate with commas or spaces, e.g. md, pdf, .txt", "以逗號或空白分隔，例如：md, pdf, .txt", "カンマまたは空白で区切ります。例：md, pdf, .txt");
        Resources["RuleOrderHelp"] = T("Drag to reorder rules", "拖曳以調整規則順序", "ドラッグしてルールの順序を変更");
        AutomationProperties.SetName(_groupOpacity, T("Group opacity percentage", "分類區不透明度百分比", "グループの不透明度（パーセント）"));
        SetDesktopState(_desktopEnabled, _desktopAvailable);
        if (_statusText is not null) _status.Text = _statusText();
    }

    public void SetLocalizedStatus(string english, string chinese, string japanese, bool isError = false)
    {
        _statusText = () => T(english, chinese, japanese);
        _status.Text = _statusText();
        _status.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ErrorBrush" : "MutedBrush");
    }

    private static IEnumerable<DependencyObject> InterfaceElements(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in InterfaceElements(child)) yield return nested;
    }
}
