using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Services.Store;

namespace MarkPad.Services;

/// <summary>Uses Windows' Store license (including its offline cache), never a local trial counter.</summary>
public sealed class StoreLicenseService
{
    private readonly string? _packageFamily = ReadPackageFamily();
    private readonly string _expectedFamily = Metadata("StorePackageFamilyName");
    private readonly string _productId = Metadata("StoreProductId");
    private readonly bool _storeBuild = string.Equals(Metadata("StoreLicenseRequired"), "true", StringComparison.OrdinalIgnoreCase);
    private readonly PaidLicensePolicy _paidLicense;
    public bool RequiresStoreLicense => _storeBuild || _packageFamily is not null;
    private bool MatchesPackage => !string.IsNullOrEmpty(_expectedFamily) && _packageFamily == _expectedFamily;
    public bool CanStartWithoutStore => !RequiresStoreLicense || MatchesPackage && _paidLicense.HasConfirmedPurchase();
    public string InstanceName => RequiresStoreLicense ? "MarkPad.Store." + _expectedFamily : "MarkPad";

    public StoreLicenseService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "MarkPad", "licensing");
        _paidLicense = new PaidLicensePolicy(new PaidLicenseCache(directory, _expectedFamily, _productId), LocalLog.Write);
    }

    public async Task<AppLicenseState> CheckAsync(nint owner, CancellationToken cancellationToken)
    {
        if (!RequiresStoreLicense) return AppLicenseState.Development;
        // The Store binary also fails closed when copied out of its installed package.
        if (!MatchesPackage) return AppLicenseState.PackageRequired;
        return await _paidLicense.CheckAsync(token => QueryStoreAsync(owner, token), cancellationToken);
    }

    private static async Task<AppLicenseState> QueryStoreAsync(nint owner, CancellationToken cancellationToken)
    {
        if (owner == 0) throw new ArgumentException("Store licensing requires an owner window.", nameof(owner));
        var context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, owner);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var license = await context.GetAppLicenseAsync().AsTask(timeout.Token).WaitAsync(timeout.Token);
        return license is null ? AppLicenseState.Unavailable
            : Evaluate(license.IsActive, license.IsTrial, license.ExpirationDate, DateTimeOffset.UtcNow);
    }

    public static AppLicenseState Evaluate(bool isActive, bool isTrial, DateTimeOffset expiration, DateTimeOffset now)
    {
        // IsActive is the Store's entitlement decision. Dates only refine the denied message.
        if (isActive) return isTrial ? AppLicenseState.Trial : AppLicenseState.Full;
        return expiration > DateTimeOffset.FromFileTime(0) && expiration <= now
            ? AppLicenseState.Expired : AppLicenseState.NotOwned;
    }

    public void OpenStore()
    {
        if (string.IsNullOrWhiteSpace(_productId)) throw new InvalidOperationException("The Store product ID is missing.");
        Process.Start(new ProcessStartInfo("ms-windows-store://pdp/?ProductId=" + Uri.EscapeDataString(_productId))
        { UseShellExecute = true });
    }

    private static string Metadata(string name) => typeof(StoreLicenseService).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(value => value.Key == name)?.Value ?? "";

    private static string? ReadPackageFamily()
    {
        uint length = 0;
        var error = GetCurrentPackageFamilyName(ref length, null);
        if (error == 15700) return null; // APPMODEL_ERROR_NO_PACKAGE
        if (error != 122) throw new Win32Exception(error); // ERROR_INSUFFICIENT_BUFFER
        var buffer = new StringBuilder(checked((int)length));
        error = GetCurrentPackageFamilyName(ref length, buffer);
        if (error != 0) throw new Win32Exception(error);
        return buffer.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFamilyName(ref uint packageFamilyNameLength, StringBuilder? packageFamilyName);
}
