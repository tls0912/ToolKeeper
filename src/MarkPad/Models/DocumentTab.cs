using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using ICSharpCode.AvalonEdit.Document;

namespace MarkPad.Models;

/// <summary>A document and its undo history stay alive when changing tabs or preview mode.</summary>
public sealed class DocumentTab : INotifyPropertyChanged
{
    private string? _filePath;
    private bool _isDirty;
    private bool _isReadOnly;
    private bool _isMissing;
    private bool _isLargeFile;
    private bool _isPreviewMode = true;
    private Encoding _encoding = new UTF8Encoding(false, true);
    private bool _hasBom;

    public DocumentTab()
    {
        Document = new TextDocument();
        Document.TextChanged += (_, _) => OnPropertyChanged(nameof(Content));
        Document.UndoStack.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Document.UndoStack.IsOriginalFile))
                SetDirty(!Document.UndoStack.IsOriginalFile);
        };
    }

    public Guid Id { get; set; } = Guid.NewGuid();
    public TextDocument Document { get; }
    public string Content { get => Document.Text; set => Document.Text = value ?? string.Empty; }
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (!Set(ref _filePath, value)) return;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Title));
        }
    }
    public string DisplayName => FilePath is null ? "Untitled.md" : Path.GetFileName(FilePath);
    public string Title => IsDirty ? $"● {DisplayName}" : DisplayName;
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (value) Document.UndoStack.DiscardOriginalFileMarker();
            else Document.UndoStack.MarkAsOriginalFile();
            SetDirty(value);
        }
    }
    public bool IsReadOnly { get => _isReadOnly; set => Set(ref _isReadOnly, value); }
    public bool IsMissing { get => _isMissing; set => Set(ref _isMissing, value); }
    public bool IsLargeFile { get => _isLargeFile; set => Set(ref _isLargeFile, value); }
    public bool IsPreviewMode { get => _isPreviewMode; set => Set(ref _isPreviewMode, value); }
    public Encoding Encoding { get => _encoding; set => Set(ref _encoding, value); }
    public bool HasBom { get => _hasBom; set => Set(ref _hasBom, value); }
    public DateTime LastWriteTimeUtc { get; set; }
    public double PreviewScroll { get; set; }
    public int CaretOffset { get; set; }
    public double EditorScroll { get; set; }
    public DateTime LastActivatedUtc { get; set; } = DateTime.UtcNow;

    // Protect saves even when external tools preserve timestamps.
    internal string? DiskHash { get; set; }
    internal long DiskLength { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetDirty(bool value)
    {
        if (Set(ref _isDirty, value, nameof(IsDirty))) OnPropertyChanged(nameof(Title));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
