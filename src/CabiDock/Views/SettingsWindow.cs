using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CabiDock.Models;
using CabiDock.Services;
using ToolKeeper.UI;
using Controls = System.Windows.Controls;

namespace CabiDock.Views;

public sealed class SettingsSaveRequestedEventArgs(CabiDockConfiguration configuration) : EventArgs
{
    public CabiDockConfiguration Configuration { get; } = configuration;
    public string? ErrorMessage { get; set; }
}

public sealed partial class SettingsWindow : AppWindow
{
    private const string RuleDragFormat = "CabiDock.KeywordRule";
    private readonly ObservableCollection<CategoryEditor> _categories = [];
    private readonly ObservableCollection<RuleEditor> _rules = [];
    private readonly Controls.DataGrid _categoryGrid;
    private readonly Controls.DataGrid _ruleGrid;
    private readonly Controls.TextBlock _status;
    private readonly Controls.Button _desktopToggle;
    private readonly Controls.Slider _groupOpacity;
    private System.Windows.Point _ruleDragStart;
    private bool _isDraggingRule;

    public event EventHandler<SettingsSaveRequestedEventArgs>? SaveRequested;
    public event EventHandler? PreviewRequested;
    public event EventHandler? DesktopToggleRequested;
    public bool AllowClose { get; set; }
    public ObservableCollection<CategoryEditor> Categories => _categories;

    public SettingsWindow(CabiDockConfiguration configuration)
    {
        MainName = "CabiDock";
        SubName = "Desktop Organizer";
        Description = "桌面檔案自動分組，平時收起，需要時點開。";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(940, Math.Max(320, workArea.Width - 32));
        Height = Math.Min(830, Math.Max(320, workArea.Height - 32));
        MinWidth = Math.Min(740, Width);
        MinHeight = Math.Min(660, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/CabiDock;component/Views/SettingsStyles.xaml", UriKind.Relative)
        });
        AboutAuthor = "不告訴你";
        DataContext = this;
        foreach (var category in configuration.Categories) _categories.Add(new(category));
        foreach (var rule in configuration.KeywordRules) _rules.Add(new(rule));

        var root = new Controls.Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1.1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Workspace = root;

        var preview = ViewTheme.Button("開啟群組操作預覽", (_, _) => PreviewRequested?.Invoke(this, EventArgs.Empty));
        preview.VerticalAlignment = VerticalAlignment.Center;
        var actions = new Controls.WrapPanel
        {
            Name = "WorkspaceActions", HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 12)
        };
        _desktopToggle = ViewTheme.Button("暫停桌面接管", (_, _) => DesktopToggleRequested?.Invoke(this, EventArgs.Empty));
        _desktopToggle.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(_desktopToggle);
        actions.Children.Add(preview);
        root.Children.Add(actions);

        _categoryGrid = CreateGrid(_categories);
        _categoryGrid.Columns.Add(new Controls.DataGridTextColumn
        {
            Header = "分類名稱", Binding = EditBinding(nameof(CategoryEditor.Name)), Width = 150
        });
        _categoryGrid.Columns.Add(new Controls.DataGridTextColumn
        {
            Header = "種類", Binding = new Binding(nameof(CategoryEditor.KindLabel)), Width = 90, IsReadOnly = true
        });
        var extensionText = new FrameworkElementFactory(typeof(Controls.TextBox));
        extensionText.SetBinding(Controls.TextBox.TextProperty, EditBinding(nameof(CategoryEditor.ExtensionText)));
        extensionText.SetBinding(IsEnabledProperty, new Binding(nameof(CategoryEditor.CanEditExtensions)));
        extensionText.SetValue(Controls.Control.BorderThicknessProperty, new Thickness(0));
        extensionText.SetValue(Controls.Control.BackgroundProperty, Brushes.Transparent);
        extensionText.SetResourceReference(Controls.Control.ForegroundProperty, "TextBrush");
        extensionText.SetResourceReference(Controls.TextBox.CaretBrushProperty, "TextBrush");
        extensionText.SetResourceReference(Controls.Control.ToolTipProperty, "ExtensionsHelp");
        _categoryGrid.Columns.Add(new Controls.DataGridTemplateColumn
        {
            Header = "副檔名（以逗號或空白分隔）", CellTemplate = new DataTemplate { VisualTree = extensionText },
            Width = new Controls.DataGridLength(1, Controls.DataGridLengthUnitType.Star)
        });
        var categorySection = Section("分類", "自訂分類的副檔名優先；資料夾與「其他」保留特殊判定。", _categoryGrid,
            ViewTheme.Button("＋ 新增分類", AddCategory), ViewTheme.Button("刪除自訂分類", DeleteCategory));
        Controls.Grid.SetRow(categorySection, 1);
        root.Children.Add(categorySection);

        _ruleGrid = CreateGrid(_rules);
        _ruleGrid.AllowDrop = true;
        _ruleGrid.Drop += RuleDrop;
        _ruleGrid.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(RuleDragFormat) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
            e.Handled = true;
        };
        var grip = new FrameworkElementFactory(typeof(Controls.TextBlock));
        grip.SetValue(Controls.TextBlock.TextProperty, "⠿");
        grip.SetValue(Controls.TextBlock.FontSizeProperty, 22d);
        grip.SetValue(Controls.TextBlock.PaddingProperty, new Thickness(9, 2, 9, 2));
        grip.SetValue(CursorProperty, Cursors.SizeAll);
        grip.SetResourceReference(ToolTipProperty, "RuleOrderHelp");
        grip.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) => _ruleDragStart = e.GetPosition(_ruleGrid)));
        grip.AddHandler(MouseMoveEvent, new MouseEventHandler(RuleGripMouseMove));
        _ruleGrid.Columns.Add(new Controls.DataGridTemplateColumn { Header = "排序", Width = 52, CellTemplate = new DataTemplate { VisualTree = grip } });
        _ruleGrid.Columns.Add(new Controls.DataGridTextColumn
        {
            Header = "檔名包含", Binding = EditBinding(nameof(RuleEditor.Keyword)), Width = new Controls.DataGridLength(1, Controls.DataGridLengthUnitType.Star)
        });
        var target = new FrameworkElementFactory(typeof(Controls.ComboBox));
        target.SetBinding(Controls.ItemsControl.ItemsSourceProperty, new Binding(nameof(Categories)) { Source = this });
        target.SetValue(Controls.ComboBox.DisplayMemberPathProperty, nameof(CategoryEditor.Name));
        target.SetValue(Controls.ComboBox.SelectedValuePathProperty, nameof(CategoryEditor.Id));
        target.SetBinding(Controls.ComboBox.SelectedValueProperty, EditBinding(nameof(RuleEditor.CategoryId)));
        target.SetValue(Controls.Control.PaddingProperty, new Thickness(6));
        target.SetValue(Controls.Control.BorderThicknessProperty, new Thickness(0));
        target.SetResourceReference(Controls.Control.BackgroundProperty, "SurfaceBrush");
        target.SetResourceReference(Controls.Control.ForegroundProperty, "TextBrush");
        _ruleGrid.Columns.Add(new Controls.DataGridTemplateColumn
        {
            Header = "目標分類", Width = 200, CellTemplate = new DataTemplate { VisualTree = target }
        });
        var rulesSection = Section("檔名關鍵字規則", "由上至下比對，第一個命中者生效；僅比對檔名，不讀取內容。", _ruleGrid,
            ViewTheme.Button("＋ 新增規則", AddRule), ViewTheme.Button("刪除規則", DeleteRule));
        Controls.Grid.SetRow(rulesSection, 2);
        root.Children.Add(rulesSection);

        var opacityRow = new Controls.Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "100% 為完全不透明；儲存後套用至所有分類區。"
        };
        opacityRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        opacityRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        opacityRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var opacityLabel = ViewTheme.Text("分類區不透明度", 14);
        opacityLabel.VerticalAlignment = VerticalAlignment.Center;
        opacityLabel.Margin = new Thickness(0, 0, 16, 0);
        opacityRow.Children.Add(opacityLabel);
        _groupOpacity = new Controls.Slider
        {
            Minimum = 30, Maximum = 100, Value = configuration.GroupOpacity * 100,
            TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10,
            VerticalAlignment = VerticalAlignment.Center, AutoToolTipPlacement = Controls.Primitives.AutoToolTipPlacement.TopLeft,
            AutoToolTipPrecision = 0
        };
        System.Windows.Automation.AutomationProperties.SetName(_groupOpacity, "分類區不透明度百分比");
        Controls.Grid.SetColumn(_groupOpacity, 1);
        opacityRow.Children.Add(_groupOpacity);
        var opacityValue = ViewTheme.Text(string.Empty, 14);
        opacityValue.MinWidth = 46;
        opacityValue.Margin = new Thickness(12, 0, 0, 0);
        opacityValue.VerticalAlignment = VerticalAlignment.Center;
        opacityValue.TextAlignment = TextAlignment.Right;
        opacityValue.SetBinding(Controls.TextBlock.TextProperty,
            new Binding(nameof(Controls.Slider.Value)) { Source = _groupOpacity, StringFormat = "{0:0}%" });
        Controls.Grid.SetColumn(opacityValue, 2);
        opacityRow.Children.Add(opacityValue);
        Controls.Grid.SetRow(opacityRow, 3);
        root.Children.Add(opacityRow);

        var footer = new Controls.Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _status = ViewTheme.Text("正在準備桌面接管；群組會顯示在桌面上。", 12, ViewTheme.Muted);
        _status.Margin = new Thickness(0, 0, 20, 0);
        footer.Children.Add(new Controls.ScrollViewer
        {
            Content = _status, MaxHeight = 64,
            VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Disabled
        });
        var save = ViewTheme.Button("儲存並套用", Save, true);
        Controls.Grid.SetColumn(save, 1);
        footer.Children.Add(save);
        Controls.Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) return;
            // Let the native minimize transition finish before hiding the taskbar entry.
            Dispatcher.BeginInvoke(() => { if (WindowState == WindowState.Minimized && IsVisible) Hide(); });
        };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        InitializePreferences();
    }

    public void SetStatus(string message, bool isError = false)
    {
        _statusText = () => message;
        _status.Text = message;
        _status.SetResourceReference(Controls.TextBlock.ForegroundProperty, isError ? "ErrorBrush" : "MutedBrush");
    }

    public void SetDesktopState(bool enabled, bool available = true)
    {
        _desktopEnabled = enabled;
        _desktopAvailable = available;
        _desktopToggle.Content = enabled ? T("Pause desktop control", "暫停桌面接管", "デスクトップ制御を一時停止")
            : T("Enable desktop control", "啟用桌面接管", "デスクトップ制御を有効化");
        _desktopToggle.IsEnabled = available;
        _desktopToggle.ToolTip = available
            ? T("Pausing immediately restores native desktop icons.", "暫停後立即恢復原生桌面圖示。", "一時停止すると標準のデスクトップアイコンに戻ります。")
            : T("Custom folders use group preview without controlling the real desktop.", "自訂掃描目錄使用群組預覽，不接管真實桌面。", "指定フォルダーはグループプレビューを使用し、実際のデスクトップを変更しません。");
    }

    private static Binding EditBinding(string property) => new(property)
    {
        Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
    };

    private static Controls.DataGrid CreateGrid(System.Collections.IEnumerable source) => new()
    {
        ItemsSource = source, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
        CanUserSortColumns = false, CanUserReorderColumns = false, SelectionMode = Controls.DataGridSelectionMode.Single,
        HeadersVisibility = Controls.DataGridHeadersVisibility.Column, GridLinesVisibility = Controls.DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = ViewTheme.Line, BorderThickness = new Thickness(1), BorderBrush = ViewTheme.Line,
        Background = Brushes.White, AlternatingRowBackground = ViewTheme.Brush("#FAFCFC"), RowHeight = 40,
        ColumnHeaderHeight = 34, VerticalScrollBarVisibility = Controls.ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = Controls.ScrollBarVisibility.Auto
    };

    private static Controls.Grid Section(string title, string description, Controls.DataGrid grid, params Controls.Button[] buttons)
    {
        var panel = new Controls.Grid { Margin = new Thickness(0, 0, 0, 18) };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Controls.Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new Controls.StackPanel();
        text.Children.Add(new Controls.TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        text.Children.Add(ViewTheme.Text(description, 12, ViewTheme.Muted));
        header.Children.Add(text);
        var actions = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in buttons) { button.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(button); }
        Controls.Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        panel.Children.Add(header);
        Controls.Grid.SetRow(grid, 1);
        panel.Children.Add(grid);
        return panel;
    }

    private void AddCategory(object sender, RoutedEventArgs e)
    {
        var baseName = T("New category", "新分類", "新しい分類");
        var category = new CategoryEditor(new CategoryDefinition
        {
            Id = Guid.NewGuid().ToString("N"), Name = baseName, IsCustom = true, Kind = CategoryKind.Extension
        });
        category.SetLanguage(ResolvedLanguage);
        var number = 2;
        while (_categories.Any(c => c.Name == category.Name)) category.Name = $"{baseName} {number++}";
        _categories.Add(category);
        _categoryGrid.SelectedItem = category;
        _categoryGrid.ScrollIntoView(category);
        SetLocalizedStatus("Enter a category name and extensions, then choose Save and apply.", "請輸入分類名稱與副檔名，完成後按「儲存並套用」。", "分類名と拡張子を入力し、「保存して適用」を押してください。");
    }

    private void DeleteCategory(object sender, RoutedEventArgs e)
    {
        if (_categoryGrid.SelectedItem is not CategoryEditor category) return;
        if (!category.IsCustom) { SetLocalizedStatus("Default categories can be renamed or edited, but cannot be deleted.", "預設分類可改名或調整副檔名，但不能刪除。", "既定の分類は名前や拡張子を変更できますが、削除はできません。", true); return; }
        _categories.Remove(category);
        var removed = _rules.Where(r => r.CategoryId == category.Id).ToList();
        foreach (var rule in removed) _rules.Remove(rule);
        SetLocalizedStatus($"Removed “{category.Name}” and {removed.Count} related rules; save to apply.", $"已移除「{category.Name}」及 {removed.Count} 條相關規則；儲存後生效。", $"「{category.Name}」と関連ルール {removed.Count} 件を削除しました。保存すると適用されます。");
    }

    private void AddRule(object sender, RoutedEventArgs e)
    {
        if (_categories.Count == 0) return;
        var rule = new RuleEditor(new KeywordRule { Keyword = "", CategoryId = _categories[0].Id });
        _rules.Add(rule);
        _ruleGrid.SelectedItem = rule;
        _ruleGrid.ScrollIntoView(rule);
        SetLocalizedStatus("Enter a filename keyword and choose a category. Drag the left handle to reorder rules.", "輸入檔名關鍵字並選擇目標分類，可拖曳左側把手調整優先順序。", "ファイル名のキーワードと分類を指定してください。左のハンドルで優先順位を変更できます。");
    }

    private void DeleteRule(object sender, RoutedEventArgs e)
    {
        if (_ruleGrid.SelectedItem is RuleEditor rule) _rules.Remove(rule);
    }

    private void RuleGripMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingRule || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: RuleEditor rule } handle) return;
        var delta = e.GetPosition(_ruleGrid) - _ruleDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _isDraggingRule = true;
        try { System.Windows.DragDrop.DoDragDrop(handle, new System.Windows.DataObject(RuleDragFormat, rule), System.Windows.DragDropEffects.Move); }
        finally { _isDraggingRule = false; }
    }

    private void RuleDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(RuleDragFormat) is not RuleEditor source) return;
        var row = FindParent<Controls.DataGridRow>(e.OriginalSource as DependencyObject);
        var from = _rules.IndexOf(source);
        var to = row?.Item is RuleEditor target ? _rules.IndexOf(target) : _rules.Count - 1;
        if (from >= 0 && to >= 0 && from != to) _rules.Move(from, to);
        e.Handled = true;
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        _categoryGrid.CommitEdit(Controls.DataGridEditingUnit.Cell, true);
        _categoryGrid.CommitEdit(Controls.DataGridEditingUnit.Row, true);
        _ruleGrid.CommitEdit(Controls.DataGridEditingUnit.Cell, true);
        _ruleGrid.CommitEdit(Controls.DataGridEditingUnit.Row, true);
        var configuration = new CabiDockConfiguration
        {
            GroupOpacity = _groupOpacity.Value / 100,
            Categories = _categories.Select(c => c.ToDefinition()).ToList(),
            KeywordRules = _rules.Select(r => new KeywordRule { Keyword = r.Keyword.Trim(), CategoryId = r.CategoryId }).ToList()
        };
        var errors = ConfigurationService.Validate(configuration);
        if (errors.Count > 0)
        {
            var details = string.Join("\n", errors);
            SetLocalizedStatus("Please correct the configuration:\n" + details, "請修正設定：\n" + details, "設定を修正してください：\n" + details, true);
            return;
        }
        var args = new SettingsSaveRequestedEventArgs(ConfigurationService.Normalize(configuration));
        SaveRequested?.Invoke(this, args);
        if (args.ErrorMessage is { } error)
            SetLocalizedStatus("Could not save settings:\n" + error, "無法儲存設定：\n" + error, "設定を保存できませんでした：\n" + error, true);
        else SetLocalizedStatus("Settings saved and applied; valid manual assignments are retained.", "設定已儲存並套用；有效的手動指定會保留。", "設定を保存して適用しました。有効な手動割り当ては保持されます。");
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null && element is not T) element = VisualTreeHelper.GetParent(element);
        return element as T;
    }

    public sealed class CategoryEditor(CategoryDefinition category) : INotifyPropertyChanged
    {
        private string _name = category.Name;
        public string Id { get; } = category.Id;
        public string Name { get => _name; set { _name = value; Changed(); } }
        public string ExtensionText { get; set; } = string.Join(", ", category.Extensions);
        public bool IsCustom { get; } = category.IsCustom;
        public CategoryKind Kind { get; } = category.Kind;
        public bool CanEditExtensions => Kind == CategoryKind.Extension;
        private string _language = "zh-TW";
        public string KindLabel => IsCustom ? UiLanguage.Text(_language, "Custom", "自訂", "カスタム") : Kind switch
        {
            CategoryKind.Folder => UiLanguage.Text(_language, "Folder", "資料夾", "フォルダー"),
            CategoryKind.Fallback => UiLanguage.Text(_language, "Fallback", "兜底", "その他"),
            _ => UiLanguage.Text(_language, "Default", "預設", "既定")
        };
        public void SetLanguage(string language) { _language = language; Changed(nameof(KindLabel)); }
        public CategoryDefinition ToDefinition() => new()
        {
            Id = Id, Name = Name.Trim(), Kind = Kind, IsCustom = IsCustom,
            Extensions = CanEditExtensions ? ExtensionText.Split([',', '，', ';', '；', ' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList() : []
        };
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    }

    private sealed class RuleEditor(KeywordRule rule)
    {
        public string Keyword { get; set; } = rule.Keyword;
        public string CategoryId { get; set; } = rule.CategoryId;
    }
}
