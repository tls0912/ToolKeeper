using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class StartupLicenseGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> ActiveLicenses()
    {
        foreach (var isTrial in new[] { false, true })
        foreach (var expiration in new[] { DateTimeOffset.MinValue, DateTimeOffset.FromFileTime(0), Now.AddDays(-1), Now, Now.AddDays(1) })
            yield return [isTrial, expiration];
    }

    [Theory]
    [MemberData(nameof(ActiveLicenses))]
    public void ActiveStoreEntitlementIsAuthoritativeRegardlessOfExpiration(bool isTrial, DateTimeOffset expiration)
    {
        Assert.Equal(isTrial ? AppLicenseState.Trial : AppLicenseState.Full,
            StoreLicenseService.Evaluate(true, isTrial, expiration, Now));
    }

    [Theory]
    [InlineData(false, -1, AppLicenseState.Expired)]
    [InlineData(false, 0, AppLicenseState.Expired)]
    [InlineData(false, 1, AppLicenseState.NotOwned)]
    [InlineData(true, -1, AppLicenseState.Expired)]
    [InlineData(true, 0, AppLicenseState.Expired)]
    [InlineData(true, 1, AppLicenseState.NotOwned)]
    public void InactiveLicenseExpiresAtTheBoundaryWithoutGrantingFutureDatedAccess(
        bool isTrial, int expirationSeconds, AppLicenseState expected)
    {
        Assert.Equal(expected, StoreLicenseService.Evaluate(false, isTrial, Now.AddSeconds(expirationSeconds), Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndSentinelExpirationDatesMeanNotOwned(bool isTrial)
    {
        Assert.Equal(AppLicenseState.NotOwned, StoreLicenseService.Evaluate(false, isTrial, default, Now));
        Assert.Equal(AppLicenseState.NotOwned, StoreLicenseService.Evaluate(false, isTrial, DateTimeOffset.FromFileTime(0), Now));
        Assert.Equal(AppLicenseState.NotOwned, StoreLicenseService.Evaluate(false, isTrial, DateTimeOffset.MaxValue, Now));
    }

    [Theory]
    [InlineData(AppLicenseState.Full)]
    [InlineData(AppLicenseState.Trial)]
    [InlineData(AppLicenseState.Development)]
    public async Task AuthorizedStatesAllowStartupWithoutDialogsOrStore(AppLicenseState state)
    {
        var dialogs = new FakeDialogs();
        var checks = 0;
        var opened = 0;
        var gate = new StartupLicenseGate(_ => { checks++; return Task.FromResult(state); }, dialogs, () => opened++);

        Assert.True(await gate.CanStartAsync());
        Assert.Equal(1, checks);
        AssertNoDialogs(dialogs);
        Assert.Equal(0, opened);
    }

    [Theory]
    [InlineData(AppLicenseState.Expired, false)]
    [InlineData(AppLicenseState.Expired, true)]
    [InlineData(AppLicenseState.NotOwned, false)]
    [InlineData(AppLicenseState.NotOwned, true)]
    public async Task PurchaseDecisionNeverGrantsAccess(AppLicenseState state, bool purchase)
    {
        var dialogs = new FakeDialogs { Purchase = _ => purchase };
        var opened = 0;
        var gate = new StartupLicenseGate(_ => Task.FromResult(state), dialogs, () => opened++);

        Assert.False(await gate.CanStartAsync());
        Assert.Equal(new[] { state }, dialogs.PurchaseOffers);
        Assert.Equal(purchase ? 1 : 0, opened);
        Assert.Equal(0, dialogs.Retries);
        Assert.Equal(0, dialogs.PackageRequired);
        Assert.Equal(0, dialogs.StoreOpenFailures);
    }

    [Fact]
    public async Task StoreOpenFailureIsReportedAndStillDeniesStartup()
    {
        var dialogs = new FakeDialogs { Purchase = _ => true };
        var failure = new InvalidOperationException("The Store handler is unavailable.");
        var logged = new List<Exception>();
        var attempts = 0;
        var gate = new StartupLicenseGate(_ => Task.FromResult(AppLicenseState.Expired), dialogs,
            () => { attempts++; throw failure; }, logged.Add);

        Assert.False(await gate.CanStartAsync());
        Assert.Equal(1, attempts);
        Assert.Equal(1, dialogs.StoreOpenFailures);
        Assert.Same(failure, Assert.Single(logged));
        Assert.Equal(new[] { AppLicenseState.Expired }, dialogs.PurchaseOffers);
        Assert.Equal(0, dialogs.Retries);
    }

    [Fact]
    public async Task PackageRequiredExplainsTheInstallationRequirementWithoutOfferingPurchase()
    {
        var dialogs = new FakeDialogs();
        var opened = 0;
        var gate = new StartupLicenseGate(_ => Task.FromResult(AppLicenseState.PackageRequired), dialogs, () => opened++);

        Assert.False(await gate.CanStartAsync());
        Assert.Equal(1, dialogs.PackageRequired);
        Assert.Empty(dialogs.PurchaseOffers);
        Assert.Equal(0, dialogs.Retries);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task UnavailableLicenseCanShutDownWithoutTreatingTheUserAsUnlicensed()
    {
        var dialogs = new FakeDialogs { Retry = () => false };
        var opened = 0;
        var gate = new StartupLicenseGate(_ => Task.FromResult(AppLicenseState.Unavailable), dialogs, () => opened++);

        Assert.False(await gate.CanStartAsync());
        Assert.Equal(1, dialogs.Retries);
        Assert.Empty(dialogs.PurchaseOffers);
        Assert.Equal(0, dialogs.PackageRequired);
        Assert.Equal(0, opened);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("timeout")]
    [InlineData("internal-cancellation")]
    public async Task QueryErrorsAndTimeoutsOfferRetryOrShutdownInsteadOfPurchase(string failureKind)
    {
        Exception failure = failureKind switch
        {
            "query" => new InvalidOperationException("License query failed."),
            "timeout" => new TimeoutException("License query timed out."),
            _ => new OperationCanceledException(new CancellationToken(canceled: true))
        };
        var dialogs = new FakeDialogs { Retry = () => false };
        var opened = 0;
        var logged = new List<Exception>();
        var gate = new StartupLicenseGate(_ => Task.FromException<AppLicenseState>(failure), dialogs, () => opened++, logged.Add);

        Assert.False(await gate.CanStartAsync());
        Assert.Same(failure, Assert.Single(logged));
        Assert.Equal(1, dialogs.Retries);
        Assert.Empty(dialogs.PurchaseOffers);
        Assert.Equal(0, dialogs.StoreOpenFailures);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task RetryCanRecoverFromRepeatedFailuresBeforeAllowingAnActiveLicense()
    {
        var failure = new InvalidOperationException("Temporary query failure.");
        var timeout = new TimeoutException("Temporary timeout.");
        var dialogs = new FakeDialogs { Retry = () => true };
        var logged = new List<Exception>();
        var calls = 0;
        var opened = 0;
        var gate = new StartupLicenseGate(_ => ++calls switch
        {
            1 => Task.FromException<AppLicenseState>(failure),
            2 => Task.FromException<AppLicenseState>(timeout),
            _ => Task.FromResult(AppLicenseState.Trial)
        }, dialogs, () => opened++, logged.Add);

        Assert.True(await gate.CanStartAsync());
        Assert.Equal(3, calls);
        Assert.Equal(2, dialogs.Retries);
        Assert.Equal(new Exception[] { failure, timeout }, logged);
        Assert.Empty(dialogs.PurchaseOffers);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task CancellationBeforeCheckingDoesNotQueryPromptOrOpenStore()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var dialogs = new FakeDialogs();
        var calls = 0;
        var opened = 0;
        var gate = new StartupLicenseGate(_ => { calls++; return Task.FromResult(AppLicenseState.Full); }, dialogs, () => opened++);

        Assert.False(await gate.CanStartAsync(cancellation.Token));
        Assert.Equal(0, calls);
        AssertNoDialogs(dialogs);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task CancellationDuringCooperativeQueryDeniesStartupWithoutRetryOrLogging()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new FakeDialogs();
        var logged = new List<Exception>();
        var opened = 0;
        var gate = new StartupLicenseGate(async token =>
        {
            Assert.Equal(cancellation.Token, token);
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return AppLicenseState.Full;
        }, dialogs, () => opened++, logged.Add);

        var result = gate.CanStartAsync(cancellation.Token);
        await entered.Task;
        cancellation.Cancel();

        Assert.False(await result);
        AssertNoDialogs(dialogs);
        Assert.Empty(logged);
        Assert.Equal(0, opened);
    }

    [Theory]
    [InlineData(AppLicenseState.Full)]
    [InlineData(AppLicenseState.Expired)]
    public async Task CanceledQueryCompletionCannotAuthorizeOrShowPurchase(AppLicenseState lateResult)
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<AppLicenseState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new FakeDialogs();
        var opened = 0;
        var gate = new StartupLicenseGate(_ => completion.Task, dialogs, () => opened++);

        var result = gate.CanStartAsync(cancellation.Token);
        cancellation.Cancel();
        completion.SetResult(lateResult);

        Assert.False(await result);
        AssertNoDialogs(dialogs);
        Assert.Equal(0, opened);
    }

    [Theory]
    [InlineData(AppLicenseState.Expired)]
    [InlineData(AppLicenseState.NotOwned)]
    public async Task CancellationWhilePurchasePromptIsOpenSuppressesStoreLaunch(AppLicenseState state)
    {
        using var cancellation = new CancellationTokenSource();
        var dialogs = new FakeDialogs { Purchase = _ => { cancellation.Cancel(); return true; } };
        var opened = 0;
        var gate = new StartupLicenseGate(_ => Task.FromResult(state), dialogs, () => opened++);

        Assert.False(await gate.CanStartAsync(cancellation.Token));
        Assert.Equal(new[] { state }, dialogs.PurchaseOffers);
        Assert.Equal(0, opened);
        Assert.Equal(0, dialogs.Retries);
    }

    [Fact]
    public async Task CancellationWhileRetryPromptIsOpenStopsFurtherChecks()
    {
        using var cancellation = new CancellationTokenSource();
        var dialogs = new FakeDialogs { Retry = () => { cancellation.Cancel(); return true; } };
        var calls = 0;
        var opened = 0;
        var gate = new StartupLicenseGate(_ => { calls++; return Task.FromResult(AppLicenseState.Unavailable); }, dialogs, () => opened++);

        Assert.False(await gate.CanStartAsync(cancellation.Token));
        Assert.Equal(1, calls);
        Assert.Equal(1, dialogs.Retries);
        Assert.Empty(dialogs.PurchaseOffers);
        Assert.Equal(0, opened);
    }

    private static void AssertNoDialogs(FakeDialogs dialogs)
    {
        Assert.Empty(dialogs.PurchaseOffers);
        Assert.Equal(0, dialogs.Retries);
        Assert.Equal(0, dialogs.PackageRequired);
        Assert.Equal(0, dialogs.StoreOpenFailures);
    }

    private sealed class FakeDialogs : IStartupLicenseDialogs
    {
        public Func<bool> Retry { get; init; } = () => false;
        public Func<AppLicenseState, bool> Purchase { get; init; } = _ => false;
        public int Retries { get; private set; }
        public int PackageRequired { get; private set; }
        public int StoreOpenFailures { get; private set; }
        public List<AppLicenseState> PurchaseOffers { get; } = [];
        public bool RetryCheck() { Retries++; return Retry(); }
        public bool OfferPurchase(AppLicenseState state) { PurchaseOffers.Add(state); return Purchase(state); }
        public void ShowPackageRequired() => PackageRequired++;
        public void ShowStoreOpenFailed() => StoreOpenFailures++;
    }
}
