namespace MarkPad.Services;

public enum AppLicenseState { Full, Trial, Development, Expired, NotOwned, Unavailable, PackageRequired }

public interface IStartupLicenseDialogs
{
    bool RetryCheck();
    bool OfferPurchase(AppLicenseState state);
    void ShowPackageRequired();
    void ShowStoreOpenFailed();
}

/// <summary>Completes authorization before any editor, document recovery or IPC activation.</summary>
public sealed class StartupLicenseGate(
    Func<CancellationToken, Task<AppLicenseState>> check,
    IStartupLicenseDialogs dialogs,
    Action openStore,
    Action<Exception>? log = null)
{
    public async Task<bool> CanStartAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            AppLicenseState state;
            try { state = await check(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
            catch (Exception ex)
            {
                log?.Invoke(ex);
                state = AppLicenseState.Unavailable;
            }
            if (cancellationToken.IsCancellationRequested) return false;
            switch (state)
            {
                case AppLicenseState.Full:
                case AppLicenseState.Trial:
                case AppLicenseState.Development:
                    return true;
                case AppLicenseState.Expired:
                case AppLicenseState.NotOwned:
                    if (dialogs.OfferPurchase(state) && !cancellationToken.IsCancellationRequested)
                    {
                        try { openStore(); }
                        catch (Exception ex)
                        {
                            log?.Invoke(ex);
                            dialogs.ShowStoreOpenFailed();
                        }
                    }
                    // Opening the Store never grants access. A fresh launch checks the purchase.
                    return false;
                case AppLicenseState.PackageRequired:
                    dialogs.ShowPackageRequired();
                    return false;
                default:
                    if (!dialogs.RetryCheck()) return false;
                    break;
            }
        }
        return false;
    }
}
