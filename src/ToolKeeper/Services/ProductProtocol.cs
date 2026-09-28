using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ToolKeeper.Services;

internal static class ProductProtocol
{
    // Ask Windows for the user's actual protocol association, including packaged apps.
    // Reading only HKCR\<scheme> misses MSIX handlers and user-selected defaults.
    // https://learn.microsoft.com/windows/win32/api/shlwapi/nf-shlwapi-assocquerystringw
    // https://learn.microsoft.com/windows/win32/shell/assocf_str
    private const uint AssociationFlags = 0x100 | 0x400 | 0x1000 | 0x10000;
    private const uint Executable = 2;
    private const uint DelegateExecute = 18;
    private const uint AppId = 21;

    public static bool IsRegistered(string scheme)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var executable = Query(scheme, Executable);
        if (!string.IsNullOrWhiteSpace(executable))
            return Path.IsPathFullyQualified(executable) && File.Exists(executable);

        // A packaged app or COM handler need not expose an executable command.
        return !string.IsNullOrWhiteSpace(Query(scheme, AppId)) ||
            !string.IsNullOrWhiteSpace(Query(scheme, DelegateExecute));
    }

    private static string? Query(string scheme, uint value)
    {
        uint length = 0;
        AssocQueryString(AssociationFlags, value, scheme, null, null, ref length);
        if (length is 0 or > 32768) return null;
        var buffer = new StringBuilder((int)length);
        return AssocQueryString(AssociationFlags, value, scheme, null, buffer, ref length) == 0
            ? buffer.ToString() : null;
    }

    [DllImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryString(uint flags, uint value, string association,
        string? extra, StringBuilder? output, ref uint length);
}
