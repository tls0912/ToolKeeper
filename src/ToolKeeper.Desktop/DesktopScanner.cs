using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using CabiDock.Models;

namespace CabiDock;

public sealed record DesktopScanResult(IReadOnlyList<DesktopItem> Items, bool Succeeded, string? Error);

/// <summary>Enumerates the actual known desktop folders, never their descendants.</summary>
public sealed class DesktopScanner
{
    public static IReadOnlyList<string> ResolveRoots(Func<Environment.SpecialFolder, string>? resolver = null)
    {
        resolver ??= folder => Environment.GetFolderPath(folder);
        string[] roots =
        [
            resolver(Environment.SpecialFolder.DesktopDirectory),
            resolver(Environment.SpecialFolder.CommonDesktopDirectory)
        ];
        // Missing one known folder is not a complete desktop snapshot. In particular, a
        // temporarily unavailable redirected desktop must not erase its saved assignments.
        return roots.Any(string.IsNullOrWhiteSpace) ? [] : roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public DesktopScanResult Scan(IReadOnlyList<string> roots)
    {
        if (roots.Count == 0 || roots.Any(string.IsNullOrWhiteSpace))
            return new([], false, "Windows 未完整提供使用者與公共桌面位置。");
        var items = new Dictionary<string, DesktopItem>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var root in roots)
        {
            try
            {
                // Unlike Directory.Exists, enumeration preserves access/network errors.
                foreach (var path in Directory.EnumerateFileSystemEntries(root))
                {
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                        var fullPath = Path.GetFullPath(path);
                        items[fullPath] = new DesktopItem
                        {
                            FullPath = fullPath,
                            IsDirectory = attributes.HasFlag(FileAttributes.Directory),
                            Identity = TryGetIdentity(fullPath)
                        };
                    }
                    catch (FileNotFoundException) { /* An item disappeared during enumeration. */ }
                    catch (DirectoryNotFoundException) { }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{path}：{ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{root}：{ex.Message}");
            }
        }
        return new(items.Values.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            errors.Count == 0, errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
    }

    private static string? TryGetIdentity(string path)
    {
        // No write access. This is only used to recognize a rename, not to track history.
        using var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) return null;
        return $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
}
