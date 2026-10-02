using System.IO;
using System.Security;

namespace ToolKeeper.Services;

public enum ModuleKind { BuiltIn, Standalone }

public sealed record ProductDefinition(
    string Id,
    string Name,
    string DescriptionEnglish,
    string DescriptionChinese,
    string DescriptionJapanese,
    string ExecutableName,
    string ProjectName,
    string TargetFramework,
    string? ProtocolScheme,
    string? StoreId,
    ModuleKind ModuleKind = ModuleKind.Standalone)
{
    public string ActivationUri => $"toolkeeper://run/{Id}";
}

public enum ProductAvailability { Available, Get, Unavailable }

public sealed record ProductStatus(ProductDefinition Product, ProductAvailability Availability, string? LaunchTarget)
{
    public string Id => Product.Id;
    public string Name => Product.Name;
    public string ActivationUri => Product.ActivationUri;
    public bool CanLaunch => Availability == ProductAvailability.Available;
    public bool CanAcquire => Availability == ProductAvailability.Get;
    public bool CanActivate => CanLaunch || CanAcquire;
}

/// <summary>The shared source of product availability for the catalog and desktop group.</summary>
public sealed class ProductCatalogService
{
    // IDs remain the product numbers even when display names or assembly names change.
    // Hanqing's protocol and Store ID are declared in packaging/MarkPad and MarkPad.csproj.
    // ConvAnvil has no published protocol or Store ID. Built-in modules activate through the host.
    // HistoLens is temporarily hosted for its synthetic-data preview; release packaging is undecided.
    public static IReadOnlyList<ProductDefinition> Definitions { get; } = Array.AsReadOnly<ProductDefinition>(
    [
        new("001", "汗青", "Markdown reading and editing", "Markdown 閱讀與編輯", "Markdown の閲覧と編集",
            "Hanqing.exe", "MarkPad", "net10.0-windows10.0.17763.0", "toolkeeper-markpad", "9NHF764PXW9C"),
        new("002", "CabiDock", "Desktop organization and groups", "桌面整理與分類群組", "デスクトップの整理とグループ",
            "", "", "", null, null, ModuleKind.BuiltIn),
        new("003", "ConvAnvil", "Text, encoding and byte conversion", "文字、編碼與位元組轉換", "テキスト・エンコード・バイト変換",
            "ConvAnvil.exe", "ConvAnvil", "net10.0-windows", null, null),
        new("004", "Hash Checker", "Calculate and compare file hashes", "計算與比對檔案雜湊", "ファイルのハッシュを計算・照合",
            "", "", "", null, null, ModuleKind.BuiltIn),
        new("005", "Image → ICO", "Convert PNG, JPG and BMP images to ICO", "將 PNG、JPG、BMP 圖片轉成 ICO", "PNG・JPG・BMP 画像を ICO に変換",
            "", "", "", null, null, ModuleKind.BuiltIn),
        new("006", "HistoLens", "Historical research development preview with synthetic data", "歷史研究開發預覽，使用合成資料", "合成データによる履歴研究の開発プレビュー",
            "", "", "", null, null, ModuleKind.BuiltIn)
    ]);

    private readonly string _baseDirectory;
    private readonly Func<string, bool> _protocolAvailable;
    private readonly Func<string, bool> _fileExists;

    public ProductCatalogService(string? baseDirectory = null,
        Func<string, bool>? protocolAvailable = null, Func<string, bool>? fileExists = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        _protocolAvailable = protocolAvailable ?? ProductProtocol.IsRegistered;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>Returns built-in modules and re-probes independent apps; no installation or registration is performed.</summary>
    public IReadOnlyList<ProductStatus> Refresh() => Definitions.Select(Resolve).ToArray();

    public ProductStatus? Find(string id)
    {
        var product = Definitions.FirstOrDefault(product => product.Id == id);
        return product is null ? null : Resolve(product);
    }

    private ProductStatus Resolve(ProductDefinition product)
    {
        if (product.ModuleKind == ModuleKind.BuiltIn)
            return new(product, ProductAvailability.Available, product.ActivationUri);

        if (product.ProtocolScheme is { } scheme && Probe(() => _protocolAvailable(scheme)))
            return new(product, ProductAvailability.Available, scheme + ":");

        var executable = LocalCandidates(product).Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(IsCompleteApplication);
        if (executable is not null)
            return new(product, ProductAvailability.Available, executable);

        return product.StoreId is { } storeId
            ? new(product, ProductAvailability.Get, "ms-windows-store://pdp/?ProductId=" + Uri.EscapeDataString(storeId))
            : new(product, ProductAvailability.Unavailable, null);
    }

    private bool IsCompleteApplication(string executable)
    {
        // These projects publish as an apphost with its managed assembly and runtime settings.
        // An unrelated or incomplete .exe file is not sufficient to advertise a usable app.
        return Exists(executable) && Exists(Path.ChangeExtension(executable, ".dll")) &&
            Exists(Path.ChangeExtension(executable, ".runtimeconfig.json"));
    }

    private IEnumerable<string> LocalCandidates(ProductDefinition product)
    {
        yield return Path.Combine(_baseDirectory, product.ExecutableName);
        yield return Path.Combine(_baseDirectory, product.ProjectName, product.ExecutableName);
        var applicationName = Path.GetFileNameWithoutExtension(product.ExecutableName);
        yield return Path.Combine(_baseDirectory, applicationName, product.ExecutableName);
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(_baseDirectory));
        if (parent is not null)
        {
            yield return Path.Combine(parent.FullName, product.ProjectName, product.ExecutableName);
            yield return Path.Combine(parent.FullName, applicationName, product.ExecutableName);
        }

        // Support running this checkout's apps during development, without searching drives,
        // PATH, arbitrary working directories, or historical artifact/version directories.
        for (var directory = new DirectoryInfo(_baseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!Exists(Path.Combine(directory.FullName, "ToolKeeper.sln")) ||
                !Exists(Path.Combine(directory.FullName, "src", "ToolKeeper", "ToolKeeper.csproj"))) continue;

            // Prefer the host's configuration when it is launched from a normal build folder.
            var releaseHost = _baseDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains("Release", StringComparer.OrdinalIgnoreCase);
            foreach (var configuration in releaseHost ? new[] { "Release", "Debug" } : ["Debug", "Release"])
                yield return Path.Combine(directory.FullName, "src", product.ProjectName, "bin", configuration,
                    product.TargetFramework, product.ExecutableName);
            yield break;
        }
    }

    private bool Exists(string path) => Probe(() => _fileExists(path));

    private static bool Probe(Func<bool> probe)
    {
        try { return probe(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            // An inaccessible registration or file cannot make the rest of the catalog unusable.
            return false;
        }
    }
}
