namespace TransLamp.Core;

/// <summary>Uses a reviewed direct pack when available, otherwise exactly two packs through English.</summary>
public sealed record TranslationRoute(IReadOnlyList<DownloadableLanguagePack> Steps)
{
    public bool ViaEnglish => Steps.Count == 2;

    public static TranslationRoute? Resolve(string source, string target,
        IReadOnlyList<DownloadableLanguagePack>? catalog = null)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target) || source == target) return null;
        catalog ??= LanguagePackCatalog.Packs;
        var direct = catalog.FirstOrDefault(pack => pack.SourceLanguage == source && pack.TargetLanguage == target);
        if (direct is not null) return new(Array.AsReadOnly(new[] { direct }));
        var first = catalog.FirstOrDefault(pack => pack.SourceLanguage == source && pack.TargetLanguage == "en");
        var second = catalog.FirstOrDefault(pack => pack.SourceLanguage == "en" && pack.TargetLanguage == target);
        return first is not null && second is not null
            ? new(Array.AsReadOnly(new[] { first, second })) : null;
    }
}

public sealed record TranslationRouteProgress(int Stage, int Stages, TranslationProgress Segment);
