using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CabiDock.Views;
using Xunit;

namespace CabiDock.Tests;

public sealed class ShellContextMenuTests
{
    [Fact]
    public Task NativeShellMenuIncludesRealFileCommandsAndRename() => OnSta(() =>
    {
        using var files = new TemporaryFiles();
        var path = files.Write("文件-繁體中文.txt", "unchanged");
        var owner = new Window { ShowInTaskbar = false };
        try
        {
            using var item = ShellContextMenu.ShellMenuItem.Open(path, new WindowInteropHelper(owner).EnsureHandle());
            using var menu = item.CreateMenu(false);
            var verbs = Enumerable.Range(0, GetMenuItemCount(menu.Handle))
                .Select(index => GetMenuItemID(menu.Handle, index)).Where(id => id is >= 1 and <= 0x6fff)
                .Select(id => item.GetCanonicalVerb(id - 1)).ToArray();
            Assert.Contains("rename", verbs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("copy", verbs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("delete", verbs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("properties", verbs, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("unchanged", File.ReadAllText(path)); // No menu is shown or command invoked.
        }
        finally { owner.Close(); }
    });

    [Theory]
    [InlineData(".txt")]
    [InlineData(".lnk")]
    [InlineData(".url")]
    public Task ShellRenamePreservesUnicodeFileContentAndExtension(string extension) => OnSta(() =>
    {
        using var files = new TemporaryFiles();
        var original = files.Write("原始名稱" + extension, "內容保持不變");
        var destination = Path.Combine(files.DirectoryPath, "新的名稱" + extension);
        using (var item = ShellContextMenu.ShellMenuItem.Open(original, 0)) item.Rename("新的名稱" + extension);
        Assert.False(File.Exists(original));
        Assert.Equal("內容保持不變", File.ReadAllText(destination));
        Assert.Single(Directory.GetFiles(files.DirectoryPath));
    });

    [Theory]
    [InlineData("..\\escape.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("folder/child.txt")]
    [InlineData("bad:name.txt")]
    [InlineData("trailing.")]
    [InlineData(" ")]
    [InlineData("NUL.txt")]
    [InlineData("COM1")]
    public void RenameRejectsPathsAndReservedNamesBeforeCallingShell(string name) =>
        Assert.Throws<ArgumentException>(() => ShellContextMenu.ShellMenuItem.ValidateName(name));

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class TemporaryFiles : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "CabiDock-shell-test-" + Guid.NewGuid().ToString("N"));
        internal TemporaryFiles() => Directory.CreateDirectory(DirectoryPath);
        internal string Write(string name, string content)
        {
            var path = Path.Combine(DirectoryPath, name);
            File.WriteAllText(path, content);
            return path;
        }
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    [DllImport("user32.dll")] private static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(nint menu, int position);
}
