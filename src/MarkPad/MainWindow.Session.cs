using System.Windows;
using MarkPad.Services;

namespace MarkPad;

public partial class MainWindow
{
    private void RememberSessionOnClose() => App.Session.RememberClosingWindow(App.Preferences, Documents,
        Application.Current?.Windows.OfType<MainWindow>().Where(window => !ReferenceEquals(window, this))
            .SelectMany(window => window.Documents) ?? [], _current?.FilePath);

    internal async Task RestoreSessionAsync()
    {
        await OpenPathsAsync(DocumentSessionService.FilesToRestore(Settings));
        if (Settings.SessionActiveFile is { } activeFile)
        {
            var active = Documents.FirstOrDefault(tab => string.Equals(tab.FilePath, activeFile, StringComparison.OrdinalIgnoreCase));
            if (active is not null) SelectDocument(active);
        }
    }
}
