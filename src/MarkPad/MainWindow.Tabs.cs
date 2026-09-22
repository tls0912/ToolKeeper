using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MarkPad.Models;

namespace MarkPad;

public partial class MainWindow
{
    private sealed record TabTransfer(MainWindow Source, DocumentTab Tab);
    private Point _tabDragStart;
    private bool _dragging;

    private string TabLabel(DocumentTab tab)
    {
        var duplicate = Documents.Count(t => string.Equals(t.DisplayName, tab.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1;
        return tab.Title + (duplicate && tab.FilePath is not null ? " · " + Path.GetFileName(Path.GetDirectoryName(tab.FilePath)) : "");
    }

    private void BuildTabs()
    {
        if (TabsPanel is null || _dragging) return;
        TabsPanel.Children.Clear();
        var available = Math.Max(100, TabsArea.ActualWidth);
        var widths = Documents.ToDictionary(d => d, d => UiSize(Math.Clamp(66 + TabLabel(d).Length * 7.0, 100, 220)));
        var visible = new List<DocumentTab>();
        var total = 0.0;
        foreach (var tab in Documents)
        {
            if (total + widths[tab] > available) break;
            visible.Add(tab); total += widths[tab];
        }
        if (_current is not null && !visible.Contains(_current))
        {
            while (visible.Count > 0 && total + widths[_current] > available)
            { var last = visible[^1]; total -= widths[last]; visible.RemoveAt(visible.Count - 1); }
            visible.Add(_current);
        }
        foreach (var tab in Documents.Where(visible.Contains))
        {
            var active = ReferenceEquals(tab, _current);
            var row = new DockPanel { LastChildFill = true };
            var close = new Button { Content = "×", Width = Math.Max(22, UiSize(22)), Height = Math.Max(24, UiSize(24)), Padding = new Thickness(0), Visibility = active ? Visibility.Visible : Visibility.Hidden, ToolTip = T("Close document", "關閉文件", "文書を閉じる") };
            DockPanel.SetDock(close, Dock.Right); row.Children.Add(close);
            row.Children.Add(new TextBlock { Text = TabLabel(tab), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal });
            var button = new Button { Content = row, Width = Math.Min(widths[tab], available), Height = Math.Max(34, UiSize(34)), Margin = new Thickness(1, UiSize(5), 1, 0), Padding = new Thickness(10, 2, 5, 2), Background = active ? B("SurfaceBrush") : Brushes.Transparent, ToolTip = tab.FilePath ?? tab.DisplayName, AllowDrop = true };
            System.Windows.Automation.AutomationProperties.SetName(button, TabLabel(tab));
            close.Click += async (_, e) => { e.Handled = true; await GuardAsync(() => CloseDocumentAsync(tab)); };
            button.Click += (_, _) => SelectDocument(tab);
            button.MouseEnter += (_, _) => close.Visibility = Visibility.Visible;
            button.MouseLeave += (_, _) => { if (_current != tab) close.Visibility = Visibility.Hidden; };
            button.PreviewMouseDown += async (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle) { e.Handled = true; await GuardAsync(() => CloseDocumentAsync(tab)); }
                else if (e.ChangedButton == MouseButton.Left) _tabDragStart = e.GetPosition(this);
            };
            button.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _dragging) return;
                var at = e.GetPosition(this);
                if (Math.Abs(at.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                var canceled = false;
                QueryContinueDragEventHandler onQuery = (_, args) => { if (args.EscapePressed) canceled = true; };
                button.QueryContinueDrag += onQuery;
                _dragging = true;
                var transfer = new TabTransfer(this, tab);
                var effect = DragDrop.DoDragDrop(button, new DataObject("MarkPad.Tab", transfer), DragDropEffects.Move);
                _dragging = false;
                button.QueryContinueDrag -= onQuery;
                if (effect == DragDropEffects.None && !canceled && Documents.Contains(tab))
                {
                    var position = Mouse.GetPosition(this);
                    if (position.X < 0 || position.Y < 0 || position.X > ActualWidth || position.Y > ActualHeight)
                    {
                        var window = new MainWindow { Width = ActualWidth, Height = ActualHeight };
                        window.Show(); window.AcceptTab(transfer);
                    }
                }
                BuildTabs();
            };
            button.Drop += (_, e) =>
            {
                if (e.Data.GetData("MarkPad.Tab") is not TabTransfer transfer) return;
                e.Handled = true; e.Effects = DragDropEffects.Move;
                AcceptTab(transfer, Documents.IndexOf(tab));
            };
            TabsPanel.Children.Add(button);
        }
        OverflowButton.IsEnabled = Documents.Count > 0;
        OverflowButton.Content = visible.Count < Documents.Count ? "⌄" : "⋮";
    }

    private void AcceptTab(TabTransfer transfer, int index = -1)
    {
        var tab = transfer.Tab;
        if (_closing || _disposed || transfer.Source._closing || transfer.Source._disposed
            || transfer.Source._saving.Contains(tab.Id) || !transfer.Source.Documents.Contains(tab)) return;
        var modified = transfer.Source._modified.GetValueOrDefault(tab.Id, DateTime.UtcNow);
        var sourceLine = transfer.Source._previewSourceLines.GetValueOrDefault(tab.Id, 1);
        if (transfer.Source == this)
        {
            var before = Documents.IndexOf(tab);
            Documents.Remove(tab);
            if (index > before) index--;
            Documents.Insert(index < 0 ? Documents.Count : Math.Clamp(index, 0, Documents.Count), tab);
        }
        else
        {
            transfer.Source.RemoveDocument(tab);
            Documents.Insert(index < 0 ? Documents.Count : Math.Clamp(index, 0, Documents.Count), tab);
            tab.PropertyChanged += TabChanged;
            if (tab.IsDirty) _modified[tab.Id] = modified;
            _previewSourceLines[tab.Id] = sourceLine;
        }
        SelectDocument(tab);
        ScheduleAutoSave();
    }

    private void ShowTabList()
    {
        var body = new StackPanel { Margin = new Thickness(8), Width = 292 };
        var list = new StackPanel();
        void Populate(string query)
        {
            list.Children.Clear();
            foreach (var tab in Documents.Where(t => TabLabel(t).Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            {
                var button = new Button { Content = new TextBlock { Text = TabLabel(tab), TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = tab.FilePath };
                button.Click += (_, _) => { if (_flyout is not null) _flyout.IsOpen = false; SelectDocument(tab); };
                list.Children.Add(button);
            }
        }
        if (Documents.Count > 10)
        {
            var filter = new TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = T("Find a tab", "尋找分頁", "タブを検索") };
            filter.TextChanged += (_, _) => Populate(filter.Text); body.Children.Add(filter);
        }
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Populate(""); ShowFlyout(body);
        if (_flyout is not null) { _flyout.PlacementTarget = OverflowButton; _flyout.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; }
    }
}
