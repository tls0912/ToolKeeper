using System.IO;
using System.Security;
using ToolKeeper.Services;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class ProductCatalogTests
{
    [Fact]
    public void DefinesAllModulesWithStableIdsKindsAndVerifiedStoreIdentity()
    {
        Assert.Equal(["001", "002", "003", "004", "005", "006", "007"], ProductCatalogService.Definitions.Select(product => product.Id));
        Assert.Equal(["001", "003", "007"], ProductCatalogService.Definitions
            .Where(product => product.ModuleKind == ModuleKind.Standalone).Select(product => product.Id));
        Assert.Equal(["002", "004", "005", "006"], ProductCatalogService.Definitions
            .Where(product => product.ModuleKind == ModuleKind.BuiltIn).Select(product => product.Id));
        var hanqing = ProductCatalogService.Definitions[0];
        Assert.Equal("汗青", hanqing.Name);
        Assert.Equal("toolkeeper-markpad", hanqing.ProtocolScheme);
        Assert.Equal("9NHF764PXW9C", hanqing.StoreId);
        Assert.Null(ProductCatalogService.Definitions[2].ProtocolScheme);
        Assert.Null(ProductCatalogService.Definitions[2].StoreId);
        var translamp = Assert.Single(ProductCatalogService.Definitions, product => product.Id == "007");
        Assert.Equal("TransLamp", translamp.Name);
        Assert.Equal("TransLamp.exe", translamp.ExecutableName);
        Assert.Equal("TransLamp", translamp.ProjectName);
        Assert.Equal("net10.0-windows", translamp.TargetFramework);
        Assert.Equal("translamp", translamp.ProtocolScheme);
        Assert.Equal("translamp://open", translamp.ProductActivationUri);
        Assert.Null(translamp.StoreId);
        Assert.True(((IList<ProductDefinition>)ProductCatalogService.Definitions).IsReadOnly);
    }

    [Theory]
    [InlineData("002", "CabiDock")]
    [InlineData("004", "Hash Checker")]
    [InlineData("005", "Image → ICO")]
    [InlineData("006", "HistoLens")]
    public void BuiltInModulesAreAvailableWithoutInstallationOrRegistryProbes(string id, string name)
    {
        var environment = new ProductTestEnvironment();

        var status = environment.Catalog.Find(id)!;

        Assert.Equal(name, status.Name);
        Assert.Equal(ModuleKind.BuiltIn, status.Product.ModuleKind);
        Assert.Equal(ProductAvailability.Available, status.Availability);
        Assert.True(status.CanLaunch);
        Assert.True(status.CanActivate);
        Assert.False(status.CanAcquire);
        Assert.Equal($"toolkeeper://run/{id}", status.ActivationUri);
        Assert.Equal(status.ActivationUri, status.LaunchTarget);
        Assert.Null(status.Product.StoreId);
        Assert.Empty(environment.ProtocolQueries);
        Assert.Empty(environment.FileQueries);
    }

    [Fact]
    public void EveryCatalogEntryHasItsOwnHostActivationUri()
    {
        var environment = new ProductTestEnvironment();

        var statuses = environment.Catalog.Refresh();

        Assert.Equal(["toolkeeper://run/001", "toolkeeper://run/002", "toolkeeper://run/003",
            "toolkeeper://run/004", "toolkeeper://run/005", "toolkeeper://run/006", "toolkeeper://run/007"], statuses.Select(status => status.ActivationUri));
        Assert.All(statuses, status => Assert.Equal(status.Product.ActivationUri, status.ActivationUri));
    }

    [Fact]
    public void ExistingDefinitionConstructorDefaultsToStandalone()
    {
        var product = new ProductDefinition("999", "Example", "Example", "範例", "例",
            "Example.exe", "Example", "net10.0-windows", null, null);

        Assert.Equal(ModuleKind.Standalone, product.ModuleKind);
        Assert.Null(product.ProductActivationUri);
        Assert.Equal("toolkeeper://run/999", product.ActivationUri);
    }

    [Fact]
    public void HistoLensDescribesTwseAndSyntheticDataWithoutStandaloneOrStoreClaims()
    {
        var histolens = Assert.Single(ProductCatalogService.Definitions, product => product.Id == "006");

        Assert.Equal("HistoLens", histolens.Name);
        Assert.Contains("development preview", histolens.DescriptionEnglish);
        Assert.Contains("TWSE downloads", histolens.DescriptionEnglish);
        Assert.Contains("synthetic data", histolens.DescriptionEnglish);
        Assert.Contains("開發預覽", histolens.DescriptionChinese);
        Assert.Contains("合成資料", histolens.DescriptionChinese);
        Assert.Contains("開発プレビュー", histolens.DescriptionJapanese);
        Assert.Contains("合成データ", histolens.DescriptionJapanese);
        Assert.Equal(ModuleKind.BuiltIn, histolens.ModuleKind);
        Assert.Empty(histolens.ExecutableName);
        Assert.Empty(histolens.ProjectName);
        Assert.Empty(histolens.TargetFramework);
        Assert.Null(histolens.ProtocolScheme);
        Assert.Null(histolens.ProductActivationUri);
        Assert.Null(histolens.StoreId);
    }

    [Fact]
    public void MissingAppsOfferOnlyTheKnownStorePage()
    {
        var environment = new ProductTestEnvironment();
        var statuses = environment.Catalog.Refresh();

        Assert.Equal(ProductAvailability.Get, statuses[0].Availability);
        Assert.Equal("ms-windows-store://pdp/?ProductId=9NHF764PXW9C", statuses[0].LaunchTarget);
        Assert.False(statuses[0].CanLaunch);
        Assert.True(statuses[0].CanAcquire);
        Assert.True(statuses[0].CanActivate);
        Assert.Equal(ProductAvailability.Unavailable, statuses[2].Availability);
        Assert.Null(statuses[2].LaunchTarget);
        Assert.False(statuses[2].CanActivate);
        var translamp = Assert.Single(statuses, status => status.Id == "007");
        Assert.Equal(ProductAvailability.Unavailable, translamp.Availability);
        Assert.Null(translamp.LaunchTarget);
        Assert.False(translamp.CanActivate);
        Assert.Equal(["toolkeeper-markpad", "translamp"], environment.ProtocolQueries);
    }

    [Fact]
    public void RegisteredProtocolTakesPrecedenceOverPortableCopy()
    {
        var environment = new ProductTestEnvironment();
        environment.Protocols.Add("toolkeeper-markpad");
        environment.AddApplication(Path.Combine(environment.BaseDirectory, "Hanqing.exe"));

        var status = environment.Catalog.Find("001")!;

        Assert.Equal(ProductAvailability.Available, status.Availability);
        Assert.Equal("toolkeeper-markpad:", status.LaunchTarget);
        Assert.True(status.CanLaunch);
        Assert.False(status.CanAcquire);
        Assert.Empty(environment.FileQueries);
    }

    [Fact]
    public void TransLampProtocolUsesOpenContractBeforePortableCopy()
    {
        var environment = new ProductTestEnvironment();
        environment.Protocols.Add("translamp");
        environment.AddApplication(Path.Combine(environment.BaseDirectory, "TransLamp.exe"));

        var status = environment.Catalog.Find("007")!;

        Assert.Equal(ProductAvailability.Available, status.Availability);
        Assert.Equal("translamp://open", status.LaunchTarget);
        Assert.Equal("toolkeeper://run/007", status.ActivationUri);
        Assert.True(status.CanLaunch);
        Assert.False(status.CanAcquire);
        Assert.Equal(["translamp"], environment.ProtocolQueries);
        Assert.Empty(environment.FileQueries);
    }

    [Theory]
    [InlineData("001", "Hanqing", "Hanqing.exe")]
    [InlineData("003", "ConvAnvil", "ConvAnvil.exe")]
    [InlineData("007", "TransLamp", "TransLamp.exe")]
    public void DetectsCompleteSiblingPortableApplication(string id, string folder, string executable)
    {
        var environment = new ProductTestEnvironment();
        var path = Path.Combine(Directory.GetParent(environment.BaseDirectory)!.FullName, folder, executable);
        environment.AddApplication(path);

        var status = environment.Catalog.Find(id)!;

        Assert.True(status.CanLaunch);
        Assert.Equal(path, status.LaunchTarget);
    }

    [Fact]
    public void DoesNotTreatLoneExecutableAsInstalledProduct()
    {
        var environment = new ProductTestEnvironment();
        environment.Files.Add(Path.Combine(environment.BaseDirectory, "ConvAnvil.exe"));

        Assert.Equal(ProductAvailability.Unavailable, environment.Catalog.Find("003")!.Availability);
    }

    [Theory]
    [InlineData("003", "ConvAnvil")]
    [InlineData("007", "TransLamp")]
    public void DevelopmentDiscoveryRequiresBothRepositoryMarkers(string id, string project)
    {
        const string repository = @"C:\work\ToolKeeper";
        var environment = new ProductTestEnvironment(Path.Combine(repository, "src", "ToolKeeper", "bin", "Debug", "net10.0-windows"));
        var path = Path.Combine(repository, "src", project, "bin", "Debug", "net10.0-windows", project + ".exe");
        environment.AddApplication(path);
        environment.Files.Add(Path.Combine(repository, "ToolKeeper.sln"));
        Assert.False(environment.Catalog.Find(id)!.CanLaunch);

        environment.Files.Add(Path.Combine(repository, "src", "ToolKeeper", "ToolKeeper.csproj"));

        Assert.Equal(path, environment.Catalog.Find(id)!.LaunchTarget);
    }

    [Fact]
    public void ReleaseHostPrefersReleaseBuildWithoutScanningOldArtifacts()
    {
        const string repository = @"C:\work\ToolKeeper";
        var environment = new ProductTestEnvironment(Path.Combine(repository, "src", "ToolKeeper", "bin", "Release", "net10.0-windows"));
        environment.Files.Add(Path.Combine(repository, "ToolKeeper.sln"));
        environment.Files.Add(Path.Combine(repository, "src", "ToolKeeper", "ToolKeeper.csproj"));
        var release = Path.Combine(repository, "src", "ConvAnvil", "bin", "Release", "net10.0-windows", "ConvAnvil.exe");
        environment.AddApplication(release);
        environment.AddApplication(Path.Combine(repository, "src", "ConvAnvil", "bin", "Debug", "net10.0-windows", "ConvAnvil.exe"));

        Assert.Equal(release, environment.Catalog.Find("003")!.LaunchTarget);
        Assert.DoesNotContain(environment.FileQueries, path => path.Contains("artifacts", StringComparison.OrdinalIgnoreCase));
        Assert.All(environment.FileQueries, path => Assert.True(Path.IsPathFullyQualified(path)));
    }

    [Fact]
    public void RefreshReflectsInstallAndRemovalAndDoesNotMutateEarlierSnapshot()
    {
        var environment = new ProductTestEnvironment();
        var before = environment.Catalog.Refresh();
        environment.Protocols.Add("toolkeeper-markpad");
        var installed = environment.Catalog.Refresh();
        environment.Protocols.Clear();
        var removed = environment.Catalog.Refresh();

        Assert.Equal(ProductAvailability.Get, before[0].Availability);
        Assert.Equal(ProductAvailability.Available, installed[0].Availability);
        Assert.Equal(ProductAvailability.Get, removed[0].Availability);
    }

    [Fact]
    public void InaccessibleDiscoveryDoesNotPreventDisplayingOtherProducts()
    {
        var catalog = new ProductCatalogService(@"C:\Apps\ToolKeeper",
            _ => throw new SecurityException("registration denied"),
            _ => throw new UnauthorizedAccessException("file denied"));

        var statuses = catalog.Refresh();

        Assert.Equal(ProductAvailability.Get, statuses[0].Availability);
        Assert.Equal(ProductAvailability.Unavailable, statuses[2].Availability);
        Assert.All(statuses.Where(status => status.Product.ModuleKind == ModuleKind.BuiltIn),
            status => Assert.True(status.CanLaunch));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("../ConvAnvil.exe")]
    [InlineData("https://example.com")]
    public void UnknownIdsDoNotProbeAnything(string id)
    {
        var environment = new ProductTestEnvironment();

        Assert.Null(environment.Catalog.Find(id));
        Assert.Empty(environment.ProtocolQueries);
        Assert.Empty(environment.FileQueries);
    }
}

internal sealed class ProductTestEnvironment
{
    public string BaseDirectory { get; }
    public HashSet<string> Protocols { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ProtocolQueries { get; } = [];
    public List<string> FileQueries { get; } = [];
    public ProductCatalogService Catalog { get; }

    public ProductTestEnvironment(string baseDirectory = @"C:\Apps\ToolKeeper")
    {
        BaseDirectory = baseDirectory;
        Catalog = new(baseDirectory, scheme =>
        {
            ProtocolQueries.Add(scheme);
            return Protocols.Contains(scheme);
        }, path =>
        {
            FileQueries.Add(path);
            return Files.Contains(path);
        });
    }

    public void AddApplication(string executable)
    {
        Files.Add(executable);
        Files.Add(Path.ChangeExtension(executable, ".dll"));
        Files.Add(Path.ChangeExtension(executable, ".runtimeconfig.json"));
    }
}
