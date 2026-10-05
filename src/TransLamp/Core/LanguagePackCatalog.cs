namespace TransLamp.Core;

/// <summary>Reviewed, bundled download metadata. Reading this catalog never accesses the network.</summary>
public static class LanguagePackCatalog
{
    public const string VerifiedDate = "2026-10-04";
    public const string IndexSource = "https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json";
    // SHA256 and byte counts were measured from the official HTTPS archives, not publisher signatures.
    // en-zh/zh-en retain the verified 2026-10-03 Offline Kit resource baselines.
    public static IReadOnlyList<DownloadableLanguagePack> Packs { get; } = Array.AsReadOnly(new[]
    {
        new DownloadableLanguagePack("en-zh", "en", "zh", "English → 中文", "1.9", 70743021,
            "433e7c4f034d87fbe2353161e05f18646d7999452f801a4e1f0378522b9850ab", "translate-en_zh-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("zh-en", "zh", "en", "中文 → English", "1.9", 74481402,
            "62e7af5a3a48b530e47b7b3e5c78c2de79073ecd815750d2bf3ab35b4a67da2d", "translate-zh_en-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("en-fr", "en", "fr", "English → Français", "1.9", 65472327,
            "3a65ed83364f4e7b06e30f9dd823db1934899ed3ce839e63f46dc7b09dc797b4", "translate-en_fr-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("fr-en", "fr", "en", "Français → English", "1.9", 66585033,
            "3b3052fee6bb1e8e8e632a26a723eb2a2c7710dfe73ba61ffd9b83e85d4f14c1", "translate-fr_en-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("en-pt", "en", "pt", "English → Português", "1.9", 66179184,
            "0c5350a2fa5b923de1346edc0d42e08e38bab2e33cede3a0b9a48eb4281ad8a9", "translate-en_pt-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("pt-en", "pt", "en", "Português → English", "1.9", 69447231,
            "ae76df6f650895c16f2b582065014fab496755ca846ecb19fae81d51f332a38e", "translate-pt_en-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("en-zt", "en", "zt", "English → 繁體中文", "1.9", 70743045,
            "b97b6532196c4421240c0788cb711cc1fe5e5ecbde553c937193d13e5c67826c", "translate-en_zt-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("zt-en", "zt", "en", "繁體中文 → English", "1.9", 74498034,
            "a9ea826d801f059dd47f1f47b6c1dc9d762519a80ea3afd2a982130a42a25486", "translate-zt_en-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("en-ja", "en", "ja", "English → 日本語", "1.1", 120470284,
            "16300cc4eaa85320520cabcf433b63d01be40ef6966251de72043a083408f716", "en_ja", "MIT", "translate-en_ja-1_1.argosmodel"),
        new DownloadableLanguagePack("ja-en", "ja", "en", "日本語 → English", "1.1", 117155716,
            "623e3477959a815eb0a5ef53e09079ae8f1f9d3bbcd230473baf28c03fb83335", "ja_en", "MIT", "translate-ja_en-1_1.argosmodel"),
        new DownloadableLanguagePack("en-ko", "en", "ko", "English → 한국어", "1.1", 120789009,
            "e03d8e65e6d44525ec5808c3409fcf8728c76c2c76925372b6d3dc3278de17fc", "en_ko", "MIT", "translate-en_ko-1_1.argosmodel"),
        new DownloadableLanguagePack("ko-en", "ko", "en", "한국어 → English", "1.1", 118852077,
            "6da8f3db6ca40f42b1875570a1c06856f6e17c7ef62845d85de217ba548c1471", "ko_en", "MIT", "translate-ko_en-1_1.argosmodel"),
        new DownloadableLanguagePack("en-es", "en", "es", "English → Español", "1.0", 87503191,
            "d698d0ef87ad70d5d184b7fa6965905bf4368f09a2bb9ffb165a79bac96af0c4", "en_es", "MIT", "translate-en_es-1_0.argosmodel"),
        new DownloadableLanguagePack("en-de", "en", "de", "English → Deutsch", "1.3", 150508297,
            "6cd847f0c06c9c66013e6b0932e07fd54a6d90894659c02bf6c5247b72fb25b1", "translate-en_de-1_3", "MIT"),
        new DownloadableLanguagePack("de-en", "de", "en", "Deutsch → English", "1.3", 150512831,
            "becc2b0011f8249fcb89be9ecb75ba0d876b1fab93c28ee6ff0420936897d637", "translate-de_en-1_3", "MIT"),
        new DownloadableLanguagePack("en-it", "en", "it", "English → Italiano", "1.0", 87660780,
            "dde2180001a47904ecbbd688a41e35db8a040e4fd5b52e4f29b4bb499516ab32", "en_it", "MIT", "translate-en_it-1_0.argosmodel"),
        new DownloadableLanguagePack("it-en", "it", "en", "Italiano → English", "1.0", 87190224,
            "d2dd23b8b702f612b8127f07c7a0391f5f2a8b93344e6ddc9dd93c81824e85cb", "it_en", "MIT", "translate-it_en-1_0.argosmodel"),
        new DownloadableLanguagePack("en-ru", "en", "ru", "English → Русский", "1.9", 195746693,
            "591d743ae103752b88ffc38785c50421320f4eff93c8967e0d3d2e14d4e27811", "translate-en_ru-1_9", "MIT"),
        new DownloadableLanguagePack("ru-en", "ru", "en", "Русский → English", "1.9", 156239112,
            "e9ba8bf722d10a4a4c39f74289d5938fd47eac08dbe4ed0afd22d89445a5c3ac", "translate-ru_en-1_9", "MIT"),
        new DownloadableLanguagePack("en-ar", "en", "ar", "English → العربية", "1.0", 88502631,
            "e2a6e84337f7ebbb55f9dc61dcba3a861c0d786ae5a46fa198fd8d6473f5c775", "en_ar", "MIT", "translate-en_ar-1_0.argosmodel"),
        new DownloadableLanguagePack("ar-en", "ar", "en", "العربية → English", "1.0", 81869670,
            "bc98cd4e27ca1cebfae9b7086b2ebc635e4dbed45e7cb5f0891cb7d22feacfad", "ar_en", "MIT", "translate-ar_en-1_0.argosmodel"),
        new DownloadableLanguagePack("en-hi", "en", "hi", "English → हिन्दी", "1.1", 106752178,
            "60470a003a9c7339db8c060ed8eafc7cf999ba90dfa5dceebd8d0a4f75f1d3f0", "en_hi", "MIT", "translate-en_hi-1_1.argosmodel"),
        new DownloadableLanguagePack("hi-en", "hi", "en", "हिन्दी → English", "1.1", 102381771,
            "f99eadf297073c0f5a320df5d634ebb73e9ab0b819404edf7bd0a0daf6b1ce43", "hi_en", "MIT", "translate-hi_en-1_1.argosmodel"),
        new DownloadableLanguagePack("en-th", "en", "th", "English → ไทย", "1.9", 66978916,
            "b0fd94eceb04b967b91c2c7d8d1f7b5493c25362e43027a52649242385d0b2a3", "translate-en_th-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("th-en", "th", "en", "ไทย → English", "1.9", 71737116,
            "b6642c07389b156f568aa08c64140243bc1b1926f82df2559a2eae08316f84ce", "translate-th_en-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("en-vi", "en", "vi", "English → Tiếng Việt", "1.9", 67770159,
            "86957101aa4099aa9a1a7492e41987d938d3cf0fdaf4fb684c0797a9d567dd16", "translate-en_vi-1_9", "CC-BY-4.0 AND MIT"),
        new DownloadableLanguagePack("vi-en", "vi", "en", "Tiếng Việt → English", "1.9", 65895840,
            "ebd51b7189b13eccb9238a5777de1d343008c4c05284a8b89610242364433953", "translate-vi_en-1_9", "CC-BY-4.0 AND MIT")
    });

    public static bool SupportsDirection(string sourceLanguage, string targetLanguage) =>
        Packs.Any(pack => pack.SourceLanguage == sourceLanguage && pack.TargetLanguage == targetLanguage);

    internal static bool SupportsId(string id) => Packs.Any(pack => pack.Id == id);
}

public sealed record DownloadableLanguagePack(string Id, string SourceLanguage, string TargetLanguage,
    string DisplayName, string Version, long SizeBytes, string Sha256, string ArchiveRoot, string License,
    string? DownloadFileName = null)
{
    // Early official packages use roots such as en_ja but a versioned download filename.
    public Uri DownloadUri => new($"https://argos-net.com/v1/{DownloadFileName ?? ArchiveRoot + ".argosmodel"}");
    public string Source => DownloadUri.AbsoluteUri;
}

public sealed record LanguagePackDownloadProgress(long BytesDownloaded, long TotalBytes, string Stage)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesDownloaded / TotalBytes, 0, 1) : 0;
}
