using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Win32;

namespace ToolKeeper.Services;

/// <summary>Per-user registration for external toolkeeper:// shortcuts. Internal entries use the same URI router.</summary>
internal static class ToolKeeperProtocolRegistration
{
    internal const string RegistrationOwnerValueName = "ToolKeeperRegistrationOwner";
    internal const string RegistrationOwner = "ToolKeeper.Host.Protocol.v1";
    private const string ProtocolPath = @"Software\Classes\toolkeeper";

    public static string CreateCommand(string processPath, string? entryAssemblyPath)
    {
        static string QuotePath(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
                throw new ArgumentException("Protocol command paths must be absolute file paths.");
            return "\"" + path + "\"";
        }
        var command = QuotePath(processPath);
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            command += " " + QuotePath(entryAssemblyPath ?? throw new ArgumentException("Missing ToolKeeper entry assembly."));
        return command + " --protocol-activate \"%1\"";
    }

    public static string? TryRegister()
    {
        try
        {
            var process = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot identify the ToolKeeper executable.");
            var entry = Assembly.GetEntryAssembly();
            if (entry?.GetName().Name != "ToolKeeper") return "Protocol registration is available only in the ToolKeeper host.";
            Register(Registry.CurrentUser, CreateCommand(process, entry.Location));
            return null;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException or ArgumentException or InvalidOperationException)
        {
            return error.Message;
        }
    }

    // Tests can supply a disposable key; they never modify the user's live toolkeeper protocol.
    internal static void Register(RegistryKey root, string command)
    {
        // Inspect before creating or writing anything. An identical command or URL marker does
        // not establish ownership: another app or the user may have registered this scheme.
        using (var existing = root.OpenSubKey(ProtocolPath, writable: false))
        {
            if (existing is not null &&
                !string.Equals(existing.GetValue(RegistrationOwnerValueName) as string, RegistrationOwner, StringComparison.Ordinal))
                throw new InvalidOperationException("The toolkeeper protocol already has another registration. Its existing handler was preserved.");
        }

        using var protocol = root.CreateSubKey(ProtocolPath, true)
            ?? throw new IOException("Cannot create the ToolKeeper protocol registration.");
        // Mark new registrations first so a partially completed write can be repaired next run.
        protocol.SetValue(RegistrationOwnerValueName, RegistrationOwner, RegistryValueKind.String);
        protocol.SetValue(null, "URL:ToolKeeper Protocol", RegistryValueKind.String);
        protocol.SetValue("URL Protocol", "", RegistryValueKind.String);
        using var open = protocol.CreateSubKey(@"shell\open\command", true)
            ?? throw new IOException("Cannot create the ToolKeeper protocol command.");
        // DelegateExecute overrides the ordinary command. Remove an obsolete delegate even
        // when the executable command is unchanged, or Shell can keep using the old handler.
        open.DeleteValue("DelegateExecute", throwOnMissingValue: false);
        open.SetValue(null, command, RegistryValueKind.String);
    }
}
