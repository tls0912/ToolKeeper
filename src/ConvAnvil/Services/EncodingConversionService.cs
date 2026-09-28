using System.Text;

namespace ConvAnvil.Services;

public sealed class EncodingConversionException(string message, int offset, bool isByteOffset, Exception? innerException = null)
    : FormatException(message, innerException)
{
    public int Offset { get; } = offset;
    public bool IsByteOffset { get; } = isByteOffset;
}

public static class EncodingConversionService
{
    public static string Decode(byte[] bytes, EncodingOption option, bool stripMatchingBom = true)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var encoding = EncodingCatalog.GetEncoding(option);
        // Detection prefers the longest signature, but an explicit source choice
        // must win: UTF-16 LE BOM + initial NUL shares UTF-32 LE's signature.
        var sourcePreamble = EncodingCatalog.GetEncoding(option with { EmitBom = true }).GetPreamble();
        var skip = stripMatchingBom && bytes.AsSpan().StartsWith(sourcePreamble) ? sourcePreamble.Length : 0;
        try
        {
            return encoding.GetString(bytes.AsSpan(skip));
        }
        catch (DecoderFallbackException exception)
        {
            var offset = skip + Math.Max(0, exception.Index);
            throw new EncodingConversionException(
                $"{option.Name} 無法解讀位元組位置 {offset + 1:N0}（從 1 起算，十六進位偏移 0x{offset:X}）。請確認來源編碼。",
                offset, true, exception);
        }
    }

    public static byte[] Encode(string text, EncodingOption option)
    {
        ArgumentNullException.ThrowIfNull(text);
        var encoding = EncodingCatalog.GetEncoding(option);
        byte[] content;
        try
        {
            content = encoding.GetBytes(text);
        }
        catch (EncoderFallbackException exception)
        {
            var offset = Math.Max(0, exception.Index);
            throw new EncodingConversionException(
                $"{option.Name} 無法完整表示文字位置 {offset + 1:N0} 的字元（UTF-16 單位，從 1 起算）；已阻止有損轉換。",
                offset, false, exception);
        }

        // Exception fallback also needs this check: some legacy code pages have
        // valid many-to-one mappings, which otherwise change the original text.
        var roundTrip = encoding.GetString(content);
        if (!string.Equals(text, roundTrip, StringComparison.Ordinal))
        {
            var offset = 0;
            while (offset < text.Length && offset < roundTrip.Length && text[offset] == roundTrip[offset])
            {
                offset++;
            }

            throw new EncodingConversionException(
                $"{option.Name} 會改變文字位置 {offset + 1:N0} 的字元（UTF-16 單位，從 1 起算）；已阻止有損轉換。",
                offset, false);
        }

        var preamble = option.EmitBom ? encoding.GetPreamble() : [];
        if (preamble.Length == 0)
        {
            return content;
        }

        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }
}
