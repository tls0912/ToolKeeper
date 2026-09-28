using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace ToolKeeper.Services;

public enum ProductLaunchAction { Open, Get, Unavailable, Failed }

public sealed record ProductLaunchResult(bool Succeeded, ProductLaunchAction Action, string? Error = null);

public sealed class ProductLauncherService(ProductCatalogService catalog, Action<ProcessStartInfo>? startProcess = null,
    Action<string>? activateModule = null)
{
    private readonly Action<ProcessStartInfo> _startProcess = startProcess ?? Start;

    public ProductLaunchResult Launch(string productId)
    {
        // Do not trust a UI snapshot: an app may have been installed or removed since refresh.
        var product = catalog.Find(productId);
        if (product is not { CanActivate: true })
            return new(false, ProductLaunchAction.Unavailable);

        var action = product.CanLaunch ? ProductLaunchAction.Open : ProductLaunchAction.Get;
        try
        {
            if (product.Product.ModuleKind == ModuleKind.BuiltIn)
            {
                if (activateModule is null)
                    return new(false, ProductLaunchAction.Unavailable, "The host has not configured built-in module activation.");
                // Dispatch directly to the current host. Shell-opening our own URI would create
                // another process and needlessly route back through protocol registration and IPC.
                activateModule(product.Id);
                return new(true, ProductLaunchAction.Open);
            }
            if (product.LaunchTarget is not { } target)
                return new(false, ProductLaunchAction.Unavailable);
            var startInfo = new ProcessStartInfo(target) { UseShellExecute = true };
            if (Path.IsPathFullyQualified(target)) startInfo.WorkingDirectory = Path.GetDirectoryName(target)!;
            _startProcess(startInfo);
            return new(true, action);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or
            SecurityException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // Report failure; never silently send a failed app launch to a purchase page.
            return new(false, ProductLaunchAction.Failed, exception.Message);
        }
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        // Shell activation can successfully reuse an existing process and return null.
        using var process = Process.Start(startInfo);
    }
}
