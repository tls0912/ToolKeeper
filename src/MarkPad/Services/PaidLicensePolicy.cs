namespace MarkPad.Services;

/// <summary>Trials query the Store each launch. A confirmed purchase is remembered without revalidation.</summary>
public sealed class PaidLicensePolicy(IPaidLicenseCache cache, Action<Exception>? log = null)
{
    public bool HasConfirmedPurchase()
    {
        try { return cache.HasConfirmedPurchase(); }
        catch (Exception ex) { log?.Invoke(ex); return false; }
    }

    public async Task<AppLicenseState> CheckAsync(
        Func<CancellationToken, Task<AppLicenseState>> queryStore, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HasConfirmedPurchase()) return AppLicenseState.Full;
        var state = await queryStore(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (state == AppLicenseState.Full)
        {
            // A write failure must not reject a purchase that the Store just confirmed.
            // It only means the next launch has to query the Store again.
            try { cache.SaveConfirmedPurchase(); }
            catch (Exception ex) { log?.Invoke(ex); }
        }
        return state;
    }
}
