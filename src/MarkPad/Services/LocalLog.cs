using System.IO;

namespace MarkPad.Services;

public static class LocalLog
{
    private static readonly object Gate = new();

    public static void Write(Exception exception)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper", "MarkPad");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {exception}\n\n");
            }
        }
        catch
        {
            // Logging must never prevent startup or mask the original error.
        }
    }
}
