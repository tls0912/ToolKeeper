using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CabiDock.Models;
using CabiDock.Views;

namespace CabiDock;

/// <summary>Exercises the group interactions inside an ordinary application window.
/// Opening this preview pauses the desktop session; this window never attaches to Explorer.</summary>
public sealed class GroupPreviewWindow : Window
{
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(232, 240, 237)), ClipToBounds = true };
    private readonly TextBlock _empty = new() { Text = "桌面目前沒有可顯示的項目。", Margin = new Thickness(28), Foreground = Brushes.DimGray };
    private readonly Dictionary<string, GroupWindow> _groups = [];
    private readonly List<Action> _unsubscribe = [];
    private int _front;
    private bool _disposed;
    private IReadOnlyList<DesktopTool> _tools = [];
    private Action<string>? _activateTool;

    public event Action<DesktopItem>? ItemOpenRequested;
    public event Action<DesktopItem, string>? ManualAssignmentRequested;
    public event Action<string, GroupLayout>? LayoutChanged;
    public bool AllowClose { get; set; }

    public GroupPreviewWindow()
    {
        Title = "CabiDock｜群組操作預覽";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1120, Math.Max(320, workArea.Width - 32));
        Height = Math.Min(730, Math.Max(320, workArea.Height - 32));
        MinWidth = Math.Min(640, Width);
        MinHeight = Math.Min(480, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel();
        var caption = new StackPanel { Margin = new Thickness(22, 16, 22, 16) };
        caption.Children.Add(new TextBlock { Text = "群組操作預覽", FontSize = 22, FontWeight = FontWeights.SemiBold });
        caption.Children.Add(new TextBlock
        {
            Text = "分類：點標題展開或收合 · 雙擊開啟項目 · 拖曳標題移動並吸附（Shift 暫停吸附） · 拖曳項目改分類\n工具番固定展開。預覽時桌面接管暫停，原生圖示保留，可回設定啟用。",
            Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(caption, Dock.Top);
        root.Children.Add(caption);
        var field = new Grid();
        field.Children.Add(_canvas);
        field.Children.Add(_empty);
        root.Children.Add(field);
        Content = root;
        _canvas.SizeChanged += (_, _) => ClampGroups();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        IsVisibleChanged += (_, _) => { if (!IsVisible) foreach (var group in _groups.Values) group.Collapse(); };
        Closed += (_, _) => DisposeGroups();
    }

    public void SetTools(IReadOnlyList<DesktopTool> tools, Action<string> activateTool)
    {
        _tools = tools.ToArray();
        _activateTool = activateTool;
    }

    public void SetItems(CabiDockConfiguration configuration, CabiDockState state, IReadOnlyList<DesktopItem> items, bool rebuild = false)
    {
        if (_disposed) return;
        if (rebuild) DisposeGroups();
        _disposed = false;
        var byPath = items.ToDictionary(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
        var membership = state.Items.Where(item => byPath.ContainsKey(item.FullPath))
            .GroupBy(item => item.CategoryId).ToDictionary(group => group.Key, group => group.Select(item => byPath[item.FullPath]).ToList());
        // On the first layout, visible categories occupy the first slots. Saved positions win.
        var categories = configuration.Categories.OrderByDescending(category => membership.ContainsKey(category.Id)).ToList();
        if (_tools.Count > 0 || _groups.ContainsKey(DesktopToolsGroup.Id)) categories.Insert(0, DesktopToolsGroup.Category);
        foreach (var category in categories)
        {
            var members = membership.GetValueOrDefault(category.Id) ?? [];
            if (!_groups.TryGetValue(category.Id, out var group))
            {
                if (!state.Groups.TryGetValue(category.Id, out var layout))
                {
                    var bounds = new Rect(0, 0, _canvas.ActualWidth > 0 ? _canvas.ActualWidth : Math.Max(1, Width - 40),
                        _canvas.ActualHeight > 0 ? _canvas.ActualHeight : Math.Max(1, Height - 140));
                    var occupied = _groups.Values.Where(other => other.CardContent.Visibility == Visibility.Visible)
                        .Select(other => new Rect(other.Left, other.Top, other.Width, other.Height))
                        .Concat(categories.Where(other => other.Id != category.Id && !_groups.ContainsKey(other.Id)
                                && state.Groups.ContainsKey(other.Id) && (other.Id == DesktopToolsGroup.Id || membership.ContainsKey(other.Id)))
                            .Select(other =>
                            {
                                var position = state.Groups[other.Id];
                                return new Rect(position.X, position.Y, other.Id == DesktopToolsGroup.Id ? position.Width : 138,
                                    other.Id == DesktopToolsGroup.Id ? position.Height : 160);
                            }));
                    layout = InitialGroupLayout.Create(bounds, category.Id == DesktopToolsGroup.Id, occupied);
                    state.Groups[category.Id] = layout;
                }
                group = new GroupWindow(category, layout);
                var movingGroup = group;
                group.SnapTargets = () => _groups.Values
                    .Where(other => other != movingGroup && other.CardContent.Visibility == Visibility.Visible)
                    .Select(other => new Rect(other.Left, other.Top, other.Width, other.Height)).ToArray();
                _groups.Add(category.Id, group);
                var card = group.CardContent;
                group.Content = null;
                _canvas.Children.Add(card);
                void Sync(object? sender, EventArgs e)
                {
                    Canvas.SetLeft(card, double.IsFinite(group.Left) ? group.Left : 0);
                    Canvas.SetTop(card, double.IsFinite(group.Top) ? group.Top : 0);
                    card.Width = group.Width;
                    card.Height = group.Height;
                }
                foreach (var property in new[] { LeftProperty, TopProperty, WidthProperty, HeightProperty })
                {
                    var descriptor = DependencyPropertyDescriptor.FromProperty(property, typeof(Window));
                    descriptor.AddValueChanged(group, Sync);
                    _unsubscribe.Add(() => descriptor.RemoveValueChanged(group, Sync));
                }
                Sync(null, EventArgs.Empty);
                group.ItemOpenRequested += item => ItemOpenRequested?.Invoke(item);
                group.ManualAssignmentRequested += (item, target) => ManualAssignmentRequested?.Invoke(item, target);
                group.LayoutChanged += next => LayoutChanged?.Invoke(category.Id, next);
                group.Expanded += (_, _) =>
                {
                    if (!group.IsAlwaysExpanded)
                        foreach (var other in _groups.Values.Where(other => other != group && !other.IsAlwaysExpanded)) other.Collapse();
                    Panel.SetZIndex(card, ++_front);
                };
            }
            var isTools = category.Id == DesktopToolsGroup.Id;
            group.CardContent.Visibility = (isTools ? _tools.Count > 0 : members.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            group.CardContent.Opacity = configuration.GroupOpacity;
            if (isTools) group.UpdateTools(_tools, id => _activateTool?.Invoke(id));
            else group.UpdateItems(members, configuration.Categories);
        }
        _empty.Visibility = membership.Count == 0 && _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClampGroups();
    }

    private void ClampGroups()
    {
        if (_canvas.ActualWidth < 1 || _canvas.ActualHeight < 1) return;
        foreach (var group in _groups.Values)
            group.SetPrimaryBounds(new Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight));
    }

    private void DisposeGroups()
    {
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();
        foreach (var group in _groups.Values) group.Close();
        _groups.Clear();
        _canvas.Children.Clear();
        _disposed = true;
    }
}
