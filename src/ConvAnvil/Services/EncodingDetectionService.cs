using UtfUnknown;

namespace ConvAnvil.Services;

public enum EncodingDetectionBasis { Bom, Empty, Ascii, Unrecognized, Statistical }

public sealed record EncodingDetection(string BomName, int BomLength, EncodingOption? Candidate, double? Confidence, string Reason,
    EncodingDetectionBasis Basis = EncodingDetectionBasis.Statistical, string? DetectedEncodingName = null);

public static class EncodingDetectionService
{
    public static EncodingDetection Detect(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        // Registers legacy encodings before UTF.Unknown resolves its candidate.
        _ = EncodingCatalog.All;
        var bom = FindBom(bytes);
        if (bom.Length > 0)
        {
            return new EncodingDetection(bom.Name, bom.Length, EncodingCatalog.FindByCodePage(bom.CodePage, true), null,
                "依 BOM 選擇來源編碼；BOM 是格式標記，仍須通過完整內容解碼檢查。", EncodingDetectionBasis.Bom);
        }

        if (bytes.Length == 0)
        {
            return new EncodingDetection("無", 0, EncodingCatalog.Default, null,
                "空檔案無法判定編碼；暫以 UTF-8 開啟。", EncodingDetectionBasis.Empty);
        }

        // ASCII is compatible with several encodings; never attach a misleading
        // detector confidence to a claim that UTF-8 is the original encoding.
        if (bytes.All(value => value < 0x80))
        {
            return new EncodingDetection("無", 0, EncodingCatalog.Default, null,
                "內容只有 ASCII 範圍位元組，無法判定原始編碼；暫以 UTF-8 開啟。含 NUL 的檔案也可能是無 BOM 的 UTF-16／32，請手動確認。", EncodingDetectionBasis.Ascii);
        }

        var detail = CharsetDetector.DetectFromBytes(bytes).Detected;
        if (detail is null)
        {
            return new EncodingDetection("無", 0, null, null, "UTF.Unknown 無法提出候選編碼；請手動選擇來源編碼。", EncodingDetectionBasis.Unrecognized);
        }

        var candidate = detail.Encoding is { } encoding ? EncodingCatalog.FindByCodePage(encoding.CodePage) : null;
        var confidence = double.IsFinite(detail.Confidence) ? Math.Clamp((double)detail.Confidence, 0, 1) : (double?)null;
        return new EncodingDetection("無", 0, candidate, confidence,
            candidate is null
                ? $"UTF.Unknown 推測可能是 {detail.EncodingName}；目前清單未提供此編碼，請手動確認。偵測結果不是保證。"
                : $"UTF.Unknown 推測可能是 {detail.EncodingName}；請檢視文字並確認來源編碼，偵測結果不是保證。",
            EncodingDetectionBasis.Statistical, detail.EncodingName);
    }

    internal static (string Name, int Length, int CodePage) FindBom(ReadOnlySpan<byte> bytes)
    {
        // UTF-32 LE begins with UTF-16 LE's signature; longest signatures first.
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 })) return ("UTF-32 LE", 4, 12000);
        if (bytes.StartsWith(new byte[] { 0, 0, 0xFE, 0xFF })) return ("UTF-32 BE", 4, 12001);
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return ("UTF-8", 3, 65001);
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE })) return ("UTF-16 LE", 2, 1200);
        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF })) return ("UTF-16 BE", 2, 1201);
        return ("無", 0, 0);
    }
}
