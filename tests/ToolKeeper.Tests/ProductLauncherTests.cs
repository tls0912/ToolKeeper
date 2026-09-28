using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ToolKeeper.Services;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class ProductLauncherTests
{
    [Fact]
    public void ActivatesRegisteredProtocolWithoutPassingFileOrCommandArguments()
    {
        var environment = new ProductTestEnvironment();
        environment.Protocols.Add("toolkeeper-markpad");
        var launches = new List<ProcessStartInfo>();
        var modules = new List<string>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add, modules.Add);

        var result = launcher.Launch("001");

        Assert.True(result.Succeeded);
        Assert.Equal(ProductLaunchAction.Open, result.Action);
        var launch = Assert.Single(launches);
        Assert.Equal("toolkeeper-markpad:", launch.FileName);
        Assert.True(launch.UseShellExecute);
        Assert.Empty(launch.Arguments);
        Assert.Empty(launch.ArgumentList);
        Assert.Empty(launch.WorkingDirectory);
        Assert.Empty(modules);
    }

    [Theory]
    [InlineData("002")]
    [InlineData("004")]
    [InlineData("005")]
    public void BuiltInModuleActivatesInHostWithoutStartingAProcess(string id)
    {
        var environment = new ProductTestEnvironment();
        var launches = new List<ProcessStartInfo>();
        var modules = new List<string>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add, modules.Add);

        var result = launcher.Launch(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ProductLaunchAction.Open, result.Action);
        Assert.Null(result.Error);
        Assert.Equal(id, Assert.Single(modules));
        Assert.Empty(launches);
        Assert.Empty(environment.FileQueries);
        Assert.Empty(environment.ProtocolQueries);
    }

    [Theory]
    [InlineData("002")]
    [InlineData("004")]
    [InlineData("005")]
    public void BuiltInModuleWithoutHostCallbackReportsUnavailableInsteadOfShellOpeningItsUri(string id)
    {
        var environment = new ProductTestEnvironment();
        var launches = new List<ProcessStartInfo>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add);

        var result = launcher.Launch(id);

        Assert.False(result.Succeeded);
        Assert.Equal(ProductLaunchAction.Unavailable, result.Action);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(launches);
    }

    [Fact]
    public void BuiltInModuleFailureNeverFallsBackToShellOrStore()
    {
        var environment = new ProductTestEnvironment();
        var launches = new List<ProcessStartInfo>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add,
            _ => throw new InvalidOperationException("The module window is unavailable."));

        var result = launcher.Launch("004");

        Assert.False(result.Succeeded);
        Assert.Equal(ProductLaunchAction.Failed, result.Action);
        Assert.Equal("The module window is unavailable.", result.Error);
        Assert.Empty(launches);
    }

    [Fact]
    public void LaunchesCompleteLocalApplicationUsingItsOwnWorkingDirectory()
    {
        var environment = new ProductTestEnvironment(@"C:\Apps with spaces\ToolKeeper");
        var path = Path.Combine(environment.BaseDirectory, "ConvAnvil", "ConvAnvil.exe");
        environment.AddApplication(path);
        ProcessStartInfo? launch = null;
        var launcher = new ProductLauncherService(environment.Catalog, info => launch = info);

        Assert.True(launcher.Launch("003").Succeeded);
        Assert.NotNull(launch);
        Assert.Equal(path, launch.FileName);
        Assert.Equal(Path.GetDirectoryName(path), launch.WorkingDirectory);
        Assert.Empty(launch.Arguments);
    }

    [Fact]
    public void GetActionUsesOnlyVerifiedStoreProductId()
    {
        var environment = new ProductTestEnvironment();
        var launches = new List<ProcessStartInfo>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add);

        var result = launcher.Launch("001");

        Assert.True(result.Succeeded);
        Assert.Equal(ProductLaunchAction.Get, result.Action);
        Assert.Equal("ms-windows-store://pdp/?ProductId=9NHF764PXW9C", Assert.Single(launches).FileName);
    }

    [Theory]
    [InlineData("003")]
    [InlineData("006")]
    [InlineData("unknown")]
    public void UnavailableAndUnknownProductsNeverStartAProcess(string id)
    {
        var environment = new ProductTestEnvironment();
        var launches = new List<ProcessStartInfo>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add);

        var result = launcher.Launch(id);

        Assert.False(result.Succeeded);
        Assert.Equal(ProductLaunchAction.Unavailable, result.Action);
        Assert.Empty(launches);
    }

    [Fact]
    public void RechecksAvailabilityWhenInvokedAfterRemoval()
    {
        var environment = new ProductTestEnvironment();
        environment.AddApplication(Path.Combine(environment.BaseDirectory, "ConvAnvil.exe"));
        Assert.True(environment.Catalog.Find("003")!.CanLaunch);
        environment.Files.Clear();
        var launches = new List<ProcessStartInfo>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add);

        Assert.Equal(ProductLaunchAction.Unavailable, launcher.Launch("003").Action);
        Assert.Empty(launches);
    }

    [Fact]
    public void RechecksAvailabilityWhenInvokedAfterInstallation()
    {
        var environment = new ProductTestEnvironment();
        Assert.True(environment.Catalog.Find("001")!.CanAcquire);
        environment.Protocols.Add("toolkeeper-markpad");
        var launches = new List<ProcessStartInfo>();
        var launcher = new ProductLauncherService(environment.Catalog, launches.Add);

        Assert.Equal(ProductLaunchAction.Open, launcher.Launch("001").Action);
        Assert.Equal("toolkeeper-markpad:", Assert.Single(launches).FileName);
    }

    [Fact]
    public void FailedLaunchReportsErrorWithoutFallingBackToStore()
    {
        var environment = new ProductTestEnvironment();
        environment.Protocols.Add("toolkeeper-markpad");
        var attempts = 0;
        var launcher = new ProductLauncherService(environment.Catalog, _ =>
        {
            attempts++;
            throw new Win32Exception(2, "The registered app was removed.");
        });

        var result = launcher.Launch("001");

        Assert.False(result.Succeeded);
        Assert.Equal(ProductLaunchAction.Failed, result.Action);
        Assert.Equal("The registered app was removed.", result.Error);
        Assert.Equal(1, attempts);
    }
}
