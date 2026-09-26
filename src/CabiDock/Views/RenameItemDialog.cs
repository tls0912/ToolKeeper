using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using CabiDock.Models;
using Controls = System.Windows.Controls;

namespace CabiDock.Views;

internal sealed class RenameItemDialog : Window
{
    private readonly DesktopItem _item;
    private readonly Controls.TextBox _name;
    private readonly Controls.TextBlock _error;

    private RenameItemDialog(DesktopItem item)
    {
        _item = item;
        Title = "重新命名";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ViewTheme.Apply(this);
        var panel = new Controls.StackPanel { Margin = new Thickness(22) };
        Content = panel;
        panel.Children.Add(ViewTheme.Text("新名稱（包含副檔名）"));
        _name = new Controls.TextBox { Text = item.Name, Margin = new Thickness(0, 10, 0, 8) };
        panel.Children.Add(_name);
        _error = ViewTheme.Text("", 12, ViewTheme.Brush("#B42318"));
        _error.Visibility = Visibility.Collapsed;
        panel.Children.Add(_error);
        var buttons = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = ViewTheme.Button("取消", (_, _) => DialogResult = false);
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(0, 0, 8, 0);
        var save = ViewTheme.Button("重新命名", (_, _) => Rename(), true);
        save.IsDefault = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.Select(0, item.IsDirectory ? item.Name.Length : Path.GetFileNameWithoutExtension(item.Name).Length);
        };
    }

    internal static void Show(FrameworkElement anchor, DesktopItem item)
    {
        var dialog = new RenameItemDialog(item);
        if (PresentationSource.FromVisual(anchor) is HwndSource source)
            new WindowInteropHelper(dialog).Owner = source.Handle;
        dialog.ShowDialog();
    }

    private void Rename()
    {
        try
        {
            var newName = _name.Text;
            ShellContextMenu.ShellMenuItem.ValidateName(newName);
            if (newName == _item.Name) { DialogResult = true; return; }
            var parent = Path.GetDirectoryName(_item.FullPath) ?? throw new IOException("找不到原本的資料夾。");
            var destination = Path.Combine(parent, newName);
            if (!string.Equals(destination, _item.FullPath, StringComparison.OrdinalIgnoreCase)
                && (File.Exists(destination) || Directory.Exists(destination)))
                throw new IOException("已有相同名稱的項目，請使用其他名稱。");
            if (!_item.IsDirectory && !string.Equals(Path.GetExtension(newName), Path.GetExtension(_item.Name), StringComparison.OrdinalIgnoreCase)
                && MessageBox.Show(this, "變更副檔名可能會讓檔案無法正常開啟。仍要變更嗎？", "變更副檔名",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            using var shellItem = ShellContextMenu.ShellMenuItem.Open(_item.FullPath, new WindowInteropHelper(this).Handle);
            shellItem.Rename(newName);
            DialogResult = true;
        }
        catch (Exception error) when (error is ArgumentException or IOException or COMException or UnauthorizedAccessException)
        {
            _error.Text = error.Message;
            _error.Visibility = Visibility.Visible;
            _name.Focus();
        }
    }
}
