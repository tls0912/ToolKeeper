using System.Windows;
using System.Windows.Controls;
using MarkPad.Models;

namespace MarkPad;

public partial class MainWindow
{
    private Window Dialog(string title, UIElement body, StackPanel buttons)
    {
        var layout = new DockPanel { Margin = new Thickness(22) };
        DockPanel.SetDock(buttons, Dock.Bottom); buttons.Margin = new Thickness(0, 22, 0, 0); layout.Children.Add(buttons);
        layout.Children.Add(body);
        return new Window
        {
            Owner = this, Title = title, Content = layout, Width = Math.Min(Math.Max(460, UiSize(460)), SystemParameters.WorkArea.Width), MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height * .8),
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = B("SurfaceBrush"), Foreground = B("TextBrush"),
            FontFamily = FontFamily, FontSize = FontSize
        };
    }

    private string? Choose(string message, params (string Id, string Label)[] choices)
    {
        string? selected = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        var dialog = Dialog("MarkPad", text, buttons);
        foreach (var (id, label) in choices)
        {
            var button = new Button { Content = label, Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(10, 7, 10, 7), MinWidth = 60, IsCancel = id == "cancel", IsDefault = id == choices[0].Id };
            button.Click += (_, _) => { selected = id; dialog.DialogResult = true; };
            buttons.Children.Add(button);
        }
        dialog.ShowDialog();
        return selected;
    }

    private DocumentTab[]? ConfirmClose(DocumentTab[] documents)
    {
        DocumentTab[]? selected = null;
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = T("Save changes before closing?", "關閉前要儲存變更嗎？", "閉じる前に変更を保存しますか？"), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        var list = new StackPanel();
        var checks = new List<(DocumentTab Tab, CheckBox Box)>();
        foreach (var document in documents)
        {
            var box = new CheckBox { Content = document.DisplayName, ToolTip = document.FilePath, IsChecked = true, Margin = new Thickness(0, 5, 0, 5), Foreground = B("TextBrush") };
            checks.Add((document, box)); list.Children.Add(box);
        }
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = Dialog("MarkPad", body, buttons);
        void Add(string label, Action action, bool cancel = false)
        {
            var button = new Button { Content = label, Margin = new Thickness(5, 0, 0, 0), Padding = new Thickness(10, 7, 10, 7), IsCancel = cancel };
            button.Click += (_, _) => { action(); dialog.Close(); }; buttons.Children.Add(button);
        }
        Add(T("Save selected", "儲存勾選文件", "選択した文書を保存"), () => selected = checks.Where(c => c.Box.IsChecked == true).Select(c => c.Tab).ToArray());
        Add(T("Don't save", "不要儲存", "保存しない"), () => selected = []);
        Add(T("Cancel", "取消", "キャンセル"), () => selected = null, true);
        dialog.ShowDialog();
        return selected;
    }
}
