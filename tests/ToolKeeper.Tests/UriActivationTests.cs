using System.IO;
using ToolKeeper.Services;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class UriActivationTests
{
    [Theory]
    [InlineData("001")]
    [InlineData("002")]
    [InlineData("003")]
    [InlineData("004")]
    [InlineData("005")]
    public void EveryCatalogEntryHasAnUnambiguousUri(string id)
    {
        var uri = ToolActivationUri.ForProduct(id);
        Assert.Equal($"toolkeeper://run/{id}", uri);
        Assert.True(ToolActivationUri.TryParse(uri, out var parsed));
        Assert.Equal(id, parsed);
        Assert.Equal(uri, StartupOptions.Parse([uri]).ActivationUri);
        Assert.Equal(uri, StartupOptions.Parse(["--activate", uri]).ActivationUri);
        Assert.Equal(uri, StartupOptions.Parse(["--protocol-activate", uri]).ActivationUri);
    }

    [Fact]
    public void SchemeAndHostAreCaseInsensitiveButCanonicalizedAtStartup()
    {
        Assert.True(ToolActivationUri.TryParse("TOOLKEEPER://RUN/004", out var id));
        Assert.Equal("004", id);
        Assert.Equal("toolkeeper://run/004", StartupOptions.Parse(["TOOLKEEPER://RUN/004"]).ActivationUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("toolkeeper://run/999")]
    [InlineData("toolkeeper://run/4")]
    [InlineData("toolkeeper://run/004/")]
    [InlineData("toolkeeper://run//004")]
    [InlineData("toolkeeper://run/../004")]
    [InlineData("toolkeeper://run/001/../004")]
    [InlineData("toolkeeper://run/./004")]
    [InlineData("toolkeeper://run/%30%30%34")]
    [InlineData("toolkeeper://run/004?file=C:/Windows/cmd.exe")]
    [InlineData("toolkeeper://run/004#005")]
    [InlineData("toolkeeper://run:80/004")]
    [InlineData("toolkeeper://user@run/004")]
    [InlineData("toolkeeper://evil/004")]
    [InlineData("file://run/004")]
    [InlineData("https://run/004")]
    [InlineData("toolkeeper://run\\004")]
    [InlineData(" toolkeeper://run/004")]
    [InlineData("toolkeeper://run/004\n")]
    [InlineData("toolkeeper://run/004\" --desktop-recovery")]
    public void RejectsUnknownOrNonCanonicalInputs(string? uri)
    {
        Assert.False(ToolActivationUri.TryParse(uri, out var id));
        Assert.Equal("", id);
        if (uri is not null) Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--activate", uri]));
    }

    [Fact]
    public void InvalidMixedOptionsDoNotStartAHost()
    {
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--activate"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["toolkeeper://run/004", "toolkeeper://run/005"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--activate", "toolkeeper://run/004", "--activate", "toolkeeper://run/005"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["toolkeeper://run/004", "--diagnose-desktop", "report.json"]));
        Assert.Throws<ArgumentException>(() => ToolActivationUri.ForProduct("999"));
    }

    [Fact]
    public void IsolatedProfilesCannotReplaceTheUsersExternalProtocol()
    {
        Assert.True(StartupOptions.Parse([]).RegisterProtocol);
        Assert.True(StartupOptions.Parse(["toolkeeper://run/004"]).RegisterProtocol);
        var isolated = StartupOptions.Parse(["--data-directory", "artifacts/activation-profile", "--activate", "toolkeeper://run/004"]);
        Assert.False(isolated.RegisterProtocol);
        Assert.Equal("toolkeeper://run/004", isolated.ActivationUri);
        Assert.False(StartupOptions.Parse(["--desktop-directory", "artifacts/activation-desktop"]).RegisterProtocol);
    }

    [Fact]
    public void ExternalProtocolCommandQuotesBothTheExecutableAndUri()
    {
        Assert.Equal("\"C:\\Program Files\\ToolKeeper\\ToolKeeper.exe\" --protocol-activate \"%1\"",
            ToolKeeperProtocolRegistration.CreateCommand(@"C:\Program Files\ToolKeeper\ToolKeeper.exe", null));
        Assert.Equal("\"C:\\Program Files\\dotnet\\dotnet.exe\" \"D:\\My tools\\ToolKeeper.dll\" --protocol-activate \"%1\"",
            ToolKeeperProtocolRegistration.CreateCommand(@"C:\Program Files\dotnet\dotnet.exe", @"D:\My tools\ToolKeeper.dll"));
        Assert.Throws<ArgumentException>(() => ToolKeeperProtocolRegistration.CreateCommand("ToolKeeper.exe", null));
        Assert.Throws<ArgumentException>(() => ToolKeeperProtocolRegistration.CreateCommand(@"C:\dotnet.exe", null));
        Assert.Throws<ArgumentException>(() => ToolKeeperProtocolRegistration.CreateCommand("C:\\bad\" --arg", null));
    }

    [Fact]
    public async Task ExistingHostReceivesHomeAndRepeatedModuleActivations()
    {
        var path = IsolatedPath();
        using var owner = new ActivationBroker(path);
        using var client = new ActivationBroker(path.ToLowerInvariant() + Path.DirectorySeparatorChar);
        var received = new List<string?>();
        owner.Start((uri, _) => { received.Add(uri); return Task.FromResult(true); });
        foreach (var uri in new string?[] { null, "toolkeeper://run/002", "toolkeeper://run/004", "toolkeeper://run/005", "toolkeeper://run/004" })
            Assert.True(await client.ForwardAsync(uri));
        Assert.Equal(new string?[] { null, "toolkeeper://run/002", "toolkeeper://run/004", "toolkeeper://run/005", "toolkeeper://run/004" }, received);
    }

    [Fact]
    public async Task AFaultedModuleDoesNotBreakSubsequentActivation()
    {
        var path = IsolatedPath();
        using var owner = new ActivationBroker(path);
        using var client = new ActivationBroker(path);
        var count = 0;
        owner.Start((_, _) => ++count == 1 ? Task.FromException<bool>(new InvalidOperationException("Module failed")) : Task.FromResult(true));
        Assert.False(await client.ForwardAsync("toolkeeper://run/004"));
        Assert.True(await client.ForwardAsync("toolkeeper://run/005"));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task InvalidUriNeverReachesTheHostAndRejectionIsAcknowledged()
    {
        var path = IsolatedPath();
        using var owner = new ActivationBroker(path);
        using var client = new ActivationBroker(path);
        var count = 0;
        owner.Start((_, _) => { count++; return Task.FromResult(false); });
        Assert.False(await client.ForwardAsync("toolkeeper://run/999"));
        Assert.Equal(0, count);
        Assert.False(await client.ForwardAsync("toolkeeper://run/004"));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DifferentProfilesDoNotDeliverToEachOther()
    {
        using var owner = new ActivationBroker(IsolatedPath());
        using var client = new ActivationBroker(IsolatedPath());
        var received = false;
        owner.Start((_, _) => { received = true; return Task.FromResult(true); });
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        Assert.False(await client.ForwardAsync("toolkeeper://run/004", deadline.Token));
        Assert.False(received);
    }

    [Fact]
    public async Task DisposingAnIdleOwnerReleasesItsPipe()
    {
        var path = IsolatedPath();
        var owner = new ActivationBroker(path);
        owner.Start((_, _) => Task.FromResult(true));
        using var client = new ActivationBroker(path);
        Assert.True(await client.ForwardAsync(null));
        owner.Dispose();
        owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owner.Start((_, _) => Task.FromResult(true)));
        using var replacement = new ActivationBroker(path);
        replacement.Start((_, _) => Task.FromResult(true));
        Assert.True(await client.ForwardAsync("toolkeeper://run/005"));
    }

    [Fact]
    public void ShellQuoteExpansionCannotInjectHostOptions()
    {
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--protocol-activate", "toolkeeper://run/004", "--data-directory", @"C:\alternate"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--protocol-activate", "toolkeeper://run/004", "--desktop-directory", @"C:\alternate"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--protocol-activate", "toolkeeper://run/004", "--diagnose-desktop", @"C:\report.json"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--protocol-activate", "toolkeeper://run/004", "--desktop-recovery"]));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--protocol-activate"]));
    }

    [Fact]
    public async Task TimedOutActivationIsCancelledBeforeTheNextRequest()
    {
        var path = IsolatedPath();
        using var owner = new ActivationBroker(path);
        using var client = new ActivationBroker(path);
        CancellationToken queuedActivation = default;
        var count = 0;
        owner.Start(async (_, cancellation) =>
        {
            if (++count > 1) return true;
            queuedActivation = cancellation;
            await Task.Delay(Timeout.Infinite, cancellation);
            return true;
        });
        Assert.False(await client.ForwardAsync("toolkeeper://run/004"));
        Assert.True(queuedActivation.IsCancellationRequested);
        Assert.True(await client.ForwardAsync("toolkeeper://run/005"));
    }

    private static string IsolatedPath() => Path.Combine(Path.GetTempPath(), "ToolKeeper.ActivationTests", Guid.NewGuid().ToString("N"));
}
