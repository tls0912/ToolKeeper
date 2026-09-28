using System.IO;
using System.Security.Cryptography;
using System.Text;
using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class PaidLicenseTests
{
    [Fact]
    public async Task ConfirmedPurchaseSkipsTheStoreQuery()
    {
        var cache = new FakeCache { Confirmed = true };
        var queries = 0;
        var policy = new PaidLicensePolicy(cache);

        var state = await policy.CheckAsync(_ =>
        {
            queries++;
            return Task.FromException<AppLicenseState>(new InvalidOperationException("Store must not be queried."));
        });

        Assert.Equal(AppLicenseState.Full, state);
        Assert.Equal(0, queries);
        Assert.Equal(1, cache.Reads);
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task TrialQueriesTheStoreOnEveryStartup()
    {
        var cache = new FakeCache();
        var queries = 0;
        for (var launch = 0; launch < 3; launch++)
        {
            var policy = new PaidLicensePolicy(cache);
            Assert.Equal(AppLicenseState.Trial,
                await policy.CheckAsync(_ => { queries++; return Task.FromResult(AppLicenseState.Trial); }));
        }

        Assert.Equal(3, queries);
        Assert.Equal(3, cache.Reads);
        Assert.Equal(0, cache.Writes);
        Assert.False(cache.Confirmed);
    }

    [Theory]
    [InlineData(AppLicenseState.Trial)]
    [InlineData(AppLicenseState.Expired)]
    [InlineData(AppLicenseState.NotOwned)]
    [InlineData(AppLicenseState.Unavailable)]
    [InlineData(AppLicenseState.PackageRequired)]
    [InlineData(AppLicenseState.Development)]
    public async Task OnlyAFullStorePurchaseCanCreateTheCache(AppLicenseState state)
    {
        var cache = new FakeCache();
        var policy = new PaidLicensePolicy(cache);

        Assert.Equal(state, await policy.CheckAsync(_ => Task.FromResult(state)));
        Assert.Equal(0, cache.Writes);
        Assert.False(cache.Confirmed);
    }

    [Fact]
    public async Task QueryFailurePropagatesWithoutCreatingAPurchase()
    {
        var cache = new FakeCache();
        var error = new InvalidOperationException("Store query failed.");
        var policy = new PaidLicensePolicy(cache);

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(
            () => policy.CheckAsync(_ => Task.FromException<AppLicenseState>(error)));

        Assert.Same(error, caught);
        Assert.Equal(0, cache.Writes);
        Assert.False(cache.Confirmed);
    }

    [Fact]
    public async Task FullPurchaseIsSavedOnceAndReusedByANewPolicy()
    {
        var cache = new FakeCache();
        var queries = 0;
        Task<AppLicenseState> Query(CancellationToken _) { queries++; return Task.FromResult(AppLicenseState.Full); }

        Assert.Equal(AppLicenseState.Full, await new PaidLicensePolicy(cache).CheckAsync(Query));
        Assert.True(cache.Confirmed);
        Assert.Equal(AppLicenseState.Full, await new PaidLicensePolicy(cache).CheckAsync(Query));

        Assert.Equal(1, queries);
        Assert.Equal(1, cache.Writes);
        Assert.Equal(2, cache.Reads);
    }

    [Fact]
    public async Task UnreadableCacheFallsBackToTheActualStoreResult()
    {
        var error = new CryptographicException("Damaged protected data.");
        var cache = new FakeCache { ReadError = error };
        var logged = new List<Exception>();
        var queries = 0;
        var policy = new PaidLicensePolicy(cache, logged.Add);

        Assert.Equal(AppLicenseState.NotOwned,
            await policy.CheckAsync(_ => { queries++; return Task.FromResult(AppLicenseState.NotOwned); }));

        Assert.Equal(1, queries);
        Assert.Same(error, Assert.Single(logged));
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task FailedCacheWriteAllowsConfirmedPurchaseButRequiresAnotherQueryNextLaunch()
    {
        var error = new IOException("Profile is not writable.");
        var cache = new FakeCache { WriteError = error };
        var logged = new List<Exception>();
        var queries = 0;
        for (var launch = 0; launch < 2; launch++)
        {
            var policy = new PaidLicensePolicy(cache, logged.Add);
            Assert.Equal(AppLicenseState.Full,
                await policy.CheckAsync(_ => { queries++; return Task.FromResult(AppLicenseState.Full); }));
        }

        Assert.Equal(2, queries);
        Assert.Equal(2, cache.Writes);
        Assert.False(cache.Confirmed);
        Assert.Equal(2, logged.Count);
        Assert.All(logged, exception => Assert.Same(error, exception));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeTheCheckDoesNotReadCacheOrQueryStore(bool alreadyPaid)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cache = new FakeCache { Confirmed = alreadyPaid };
        var queries = 0;
        var policy = new PaidLicensePolicy(cache);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.CheckAsync(
            _ => { queries++; return Task.FromResult(AppLicenseState.Full); }, cancellation.Token));

        Assert.Equal(0, queries);
        Assert.Equal(0, cache.Reads);
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task CancellationDuringQueryIsPassedThroughWithoutSaving()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeCache();
        var policy = new PaidLicensePolicy(cache);
        var pending = policy.CheckAsync(async token =>
        {
            Assert.Equal(cancellation.Token, token);
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return AppLicenseState.Full;
        }, cancellation.Token);

        await entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task FullResultArrivingAfterCancellationCannotBeSavedOrAuthorizeStartup()
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<AppLicenseState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new FakeCache();
        var policy = new PaidLicensePolicy(cache);
        var pending = policy.CheckAsync(_ => completion.Task, cancellation.Token);

        cancellation.Cancel();
        completion.SetResult(AppLicenseState.Full);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, cache.Writes);
        Assert.False(cache.Confirmed);
    }

    [Fact]
    public void DpapiCacheRoundTripsWithoutStoringPlaintextPurchaseOrIdentity()
    {
        using var directory = new TestDirectory();
        const string family = "Test.Hanqing.CachedPurchase_family";
        const string product = "TEST-PRODUCT-ROUNDTRIP";
        var cache = new PaidLicenseCache(directory.PathName, family, product);

        Assert.False(cache.HasConfirmedPurchase());
        cache.SaveConfirmedPurchase();

        Assert.True(new PaidLicenseCache(directory.PathName, family, product).HasConfirmedPurchase());
        var file = Assert.Single(Directory.EnumerateFiles(directory.PathName));
        var bytes = File.ReadAllBytes(file);
        Assert.NotEmpty(bytes);
        foreach (var plaintext in new[] { family, product, "StoreConfirmedFullPurchase" })
        {
            Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(plaintext)) >= 0);
            Assert.False(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(plaintext)) >= 0);
        }
        Assert.Empty(Directory.EnumerateFiles(directory.PathName, "*.tmp"));
    }

    [Theory]
    [InlineData("Other.Package_family", "TEST-PRODUCT-SOURCE")]
    [InlineData("Test.Hanqing.Source_family", "TEST-PRODUCT-OTHER")]
    public async Task CopiedProtectedCacheCannotAuthorizeAnotherPackageOrProduct(string targetFamily, string targetProduct)
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        var original = new PaidLicenseCache(source.PathName, "Test.Hanqing.Source_family", "TEST-PRODUCT-SOURCE");
        original.SaveConfirmedPurchase();
        var other = new PaidLicenseCache(target.PathName, targetFamily, targetProduct);
        // Discover the target's public on-disk record without duplicating its name algorithm.
        other.SaveConfirmedPurchase();
        Assert.True(other.HasConfirmedPurchase());
        File.Copy(Assert.Single(Directory.EnumerateFiles(source.PathName)),
            Assert.Single(Directory.EnumerateFiles(target.PathName)), overwrite: true);
        var policy = new PaidLicensePolicy(other);
        var queries = 0;

        Assert.False(policy.HasConfirmedPurchase());
        Assert.Equal(AppLicenseState.NotOwned,
            await policy.CheckAsync(_ => { queries++; return Task.FromResult(AppLicenseState.NotOwned); }));
        Assert.Equal(1, queries);
        Assert.True(original.HasConfirmedPurchase());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DamagedOrOversizedBlobFallsBackToStoreWithoutBypassingTheGate(bool oversized)
    {
        using var directory = new TestDirectory();
        var cache = new PaidLicenseCache(directory.PathName, "Test.Hanqing.Damaged_family", "TEST-PRODUCT-DAMAGED");
        cache.SaveConfirmedPurchase();
        var path = Assert.Single(Directory.EnumerateFiles(directory.PathName));
        File.WriteAllBytes(path, oversized ? new byte[16 * 1024 + 1] : [0x00, 0xAA, 0x55]);
        var logged = new List<Exception>();
        var policy = new PaidLicensePolicy(cache, logged.Add);
        var queries = 0;

        Assert.Equal(AppLicenseState.Expired,
            await policy.CheckAsync(_ => { queries++; return Task.FromResult(AppLicenseState.Expired); }));
        Assert.Equal(1, queries);
        Assert.False(policy.HasConfirmedPurchase());
        if (!oversized)
        {
            Assert.NotEmpty(logged);
            Assert.All(logged, exception => Assert.IsAssignableFrom<CryptographicException>(exception));
        }
    }

    private sealed class FakeCache : IPaidLicenseCache
    {
        public bool Confirmed { get; set; }
        public Exception? ReadError { get; init; }
        public Exception? WriteError { get; init; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public bool HasConfirmedPurchase()
        {
            Reads++;
            if (ReadError is not null) throw ReadError;
            return Confirmed;
        }
        public void SaveConfirmedPurchase()
        {
            Writes++;
            if (WriteError is not null) throw WriteError;
            Confirmed = true;
        }
    }
}
