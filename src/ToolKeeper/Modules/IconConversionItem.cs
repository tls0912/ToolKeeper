using System.ComponentModel;
using System.IO;

namespace ToolKeeper;

public sealed class IconConversionItem(string sourcePath, string outputPath, Func<string> message, bool success) : INotifyPropertyChanged
{
    public IconConversionItem(string sourcePath, string outputPath, string message, bool success)
        : this(sourcePath, outputPath, () => message, success) { }

    public string SourcePath { get; } = sourcePath;
    public string OutputPath { get; } = outputPath;
    public bool Success { get; } = success;
    public string Message => message();
    public string FileName => Path.GetFileName(SourcePath);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshMessage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
}
