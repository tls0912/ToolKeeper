using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CabiDock.Models;
using CabiDock.Services;
using Controls = System.Windows.Controls;

namespace CabiDock.Views;

public sealed class SettingsSaveRequestedEventArgs(CabiDockConfiguration configuration) : EventArgs
{
    public CabiDockConfiguration Configuration { get; } = configuration;
    public string? ErrorMessage { get; set; }
}

public sealed class SettingsWindow : Window
{
    private const string RuleDragFormat = "CabiDock.KeywordRule";
    private readonly ObservableCollection<CategoryEditor> _categories = [];
    private readonly ObservableCollection<RuleEditor> _rules = [];
    private readonly Controls.DataGrid _categoryGrid;
    private readonly Controls.DataGrid _ruleGrid;
    private readonly Controls.TextBlock _status;
    private System.Windows.Point _ruleDragStart;
    private bool _isDraggingRule;

    public event EventHandler<SettingsSaveRequestedEventArgs>? SaveRequested;
    public event EventHandler? PreviewRequested;
    public bool AllowClose { get; set; }
    public ObservableCollection<CategoryEditor> Categories => _categories;

    public SettingsWindow(CabiDockConfiguration configuration)
    {
        Title = "CabiDock — 設定";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(940, Math.Max(320, workArea.Width - 32));
        Height = Math.Min(830, Math.Max(320, workArea.Height - 32));
        MinWidth = Math.Min(740, Width);
        MinHeight = Math.Min(660, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ViewTheme.Apply(this);
        DataContext = this;
        foreach (var category in configuration.Categories) _categories.Add(new(category));
        foreach (var rule in configuration.KeywordRules) _rules.Add(new(rule));

        var root = new Controls.Grid { Margin = new Thickness(28, 22, 28, 20) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1.1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = root;

        var header = new Controls.Grid { Margin = new Thickness(0, 0, 0, 22) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new Controls.StackPanel();
        brand.Children.Add(new Controls.TextBlock { Text = "CabiDock", FontSize = 30, FontWeight = FontWeights.SemiBold });
        brand.Children.Add(ViewTheme.Text("桌面檔案自動分組，平時收起，需要時點開。", 14, ViewTheme.Muted));
        header.Children.Add(brand);
        var preview = ViewTheme.Button("開啟群組操作預覽", (_, _) => PreviewRequested?.Invoke(this, EventArgs.Empty));
        preview.VerticalAlignment = VerticalAlignment.Center;
        Controls.Grid.SetColumn(preview, 1);
        header.Children.Add(preview);
        root.Children.Add(header);

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
        extensionText.SetValue(Controls.Control.ToolTipProperty, "以逗號或空白分隔，例如：md, pdf, .txt");
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
        grip.SetValue(ToolTipProperty, "拖曳以調整規則順序");
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
        target.SetValue(Controls.Control.BackgroundProperty, Brushes.White);
        _ruleGrid.Columns.Add(new Controls.DataGridTemplateColumn
        {
            Header = "目標分類", Width = 200, CellTemplate = new DataTemplate { VisualTree = target }
        });
        var rulesSection = Section("檔名關鍵字規則", "由上至下比對，第一個命中者生效；僅比對檔名，不讀取內容。", _ruleGrid,
            ViewTheme.Button("＋ 新增規則", AddRule), ViewTheme.Button("刪除規則", DeleteRule));
        Controls.Grid.SetRow(rulesSection, 2);
        root.Children.Add(rulesSection);

        var footer = new Controls.Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _status = ViewTheme.Text("桌面整合尚在驗證中，可先在操作預覽中試用群組。", 12, ViewTheme.Muted);
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
        Controls.Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
    }

    public void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        _status.Foreground = isError ? ViewTheme.Brush("#B03C32") : ViewTheme.Muted;
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
        var category = new CategoryEditor(new CategoryDefinition
        {
            Id = Guid.NewGuid().ToString("N"), Name = "新分類", IsCustom = true, Kind = CategoryKind.Extension
        });
        var number = 2;
        while (_categories.Any(c => c.Name == category.Name)) category.Name = $"新分類 {number++}";
        _categories.Add(category);
        _categoryGrid.SelectedItem = category;
        _categoryGrid.ScrollIntoView(category);
        SetStatus("請輸入分類名稱與副檔名，完成後按「儲存並套用」。");
    }

    private void DeleteCategory(object sender, RoutedEventArgs e)
    {
        if (_categoryGrid.SelectedItem is not CategoryEditor category) return;
        if (!category.IsCustom) { SetStatus("預設分類可改名或調整副檔名，但不能刪除。", true); return; }
        _categories.Remove(category);
        var removed = _rules.Where(r => r.CategoryId == category.Id).ToList();
        foreach (var rule in removed) _rules.Remove(rule);
        SetStatus($"已移除「{category.Name}」及 {removed.Count} 條相關規則；儲存後生效。");
    }

    private void AddRule(object sender, RoutedEventArgs e)
    {
        if (_categories.Count == 0) return;
        var rule = new RuleEditor(new KeywordRule { Keyword = "", CategoryId = _categories[0].Id });
        _rules.Add(rule);
        _ruleGrid.SelectedItem = rule;
        _ruleGrid.ScrollIntoView(rule);
        SetStatus("輸入檔名關鍵字並選擇目標分類，可拖曳左側把手調整優先順序。");
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
            Categories = _categories.Select(c => c.ToDefinition()).ToList(),
            KeywordRules = _rules.Select(r => new KeywordRule { Keyword = r.Keyword.Trim(), CategoryId = r.CategoryId }).ToList()
        };
        var errors = ConfigurationService.Validate(configuration);
        if (errors.Count > 0) { SetStatus(string.Join("\n", errors), true); return; }
        var args = new SettingsSaveRequestedEventArgs(ConfigurationService.Normalize(configuration));
        SaveRequested?.Invoke(this, args);
        SetStatus(args.ErrorMessage ?? "設定已儲存，正在更新自動分類；有效的手動指定會保留。", args.ErrorMessage is not null);
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
        public string KindLabel => IsCustom ? "自訂" : Kind switch { CategoryKind.Folder => "資料夾", CategoryKind.Fallback => "兜底", _ => "預設" };
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
