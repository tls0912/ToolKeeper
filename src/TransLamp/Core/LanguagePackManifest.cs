using System.Text.Json;

namespace TransLamp.Core;

public sealed record LanguagePackManifest
{
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = "";
    public string SourceLanguage { get; init; } = "";
    public string TargetLanguage { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string PackageVersion { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string ModelVersion { get; init; } = "";
    public string Runtime { get; init; } = "";
    public string ModelSource { get; init; } = "";
    public string LicenseIdentifier { get; init; } = "";
    public List<LanguagePackFile> Files { get; init; } = [];

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16,
        WriteIndented = true
    };
}

public sealed record LanguagePackFile
{
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
}

public sealed record InstalledLanguagePack(LanguagePackManifest Manifest, string DirectoryPath, long SizeBytes);
public sealed record LanguagePackInstallFailure(string ArchivePath, string Code, string Message);
public sealed record LanguagePackInstallResult(int InstalledCount, IReadOnlyList<LanguagePackInstallFailure> Failures);
public sealed record TranslationProgress(int Completed, int Total);

public sealed class TransLampException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
