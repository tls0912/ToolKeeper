using System.Globalization;
using System.Text;

namespace ConvAnvil.Services;

public enum ByteFormat
{
    Hex,
    HexPrefix,
    HexComma,
    HexEscape,
    Decimal,
    Binary,
}

public sealed class ByteParseException : FormatException
{
    public ByteParseException(string message, int offset)
        : base($"第 {offset + 1} 個字元：{message}")
    {
        Offset = offset;
    }

    /// <summary>Zero-based UTF-16 offset; the end of input is a valid error position.</summary>
    public int Offset { get; }
}

public static class ByteNotation
{
    public const int MaxByteCount = 4 * 1024 * 1024;
    public const int MaxInputCharacters = 64 * 1024 * 1024;

    public static byte[] Parse(string text, ByteFormat format)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateFormat(format);
        if (text.Length > MaxInputCharacters)
        {
            throw new ByteParseException("輸入過長，請分段處理（最多 64 Mi 個字元）。", MaxInputCharacters);
        }

        var bytes = new List<byte>(Math.Min(text.Length / 3, 4096));
        var position = 0;
        SkipWhitespace(text, ref position);
        if (position == text.Length)
        {
            return [];
        }

        while (position < text.Length)
        {
            if (bytes.Count == MaxByteCount)
            {
                throw new ByteParseException("位元組數量超過 4 MiB，請分段處理。", position);
            }

            var escaped = false;
            byte value;
            if (IsHex(format))
            {
                if (text[position] == '\\')
                {
                    escaped = true;
                    position++;
                    ExpectPrefixX(text, ref position);
                }
                else if (text[position] == '0' && position + 1 < text.Length && text[position + 1] is 'x' or 'X')
                {
                    position += 2;
                }

                value = (byte)((ReadDigit(text, ref position, 16) << 4) | ReadDigit(text, ref position, 16));
            }
            else if (format == ByteFormat.Binary)
            {
                var number = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    number = (number << 1) | ReadDigit(text, ref position, 2);
                }

                value = (byte)number;
            }
            else
            {
                var number = ReadDigit(text, ref position, 10);
                while (position < text.Length && text[position] is >= '0' and <= '9')
                {
                    var digitOffset = position;
                    number = number * 10 + (text[position++] - '0');
                    if (number > byte.MaxValue)
                    {
                        throw new ByteParseException("十進位位元組必須介於 0 與 255。", digitOffset);
                    }
                }

                value = (byte)number;
            }

            bytes.Add(value);
            var tokenEnd = position;
            SkipWhitespace(text, ref position);
            if (position == text.Length)
            {
                break;
            }

            if (text[position] == ',')
            {
                var commaOffset = position++;
                SkipWhitespace(text, ref position);
                if (position == text.Length)
                {
                    throw new ByteParseException("逗號後缺少位元組。", commaOffset);
                }

                if (text[position] == ',')
                {
                    throw new ByteParseException("兩個逗號之間缺少位元組。", position);
                }

                continue;
            }

            if (position != tokenEnd || (escaped && text[position] == '\\'))
            {
                continue;
            }

            throw new ByteParseException("位元組之間必須使用空白或逗號；只有 \\xHH 可直接相連。", position);
        }

        return bytes.ToArray();
    }

    public static string Format(byte[] bytes, ByteFormat format)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ValidateFormat(format);
        if (bytes.Length > MaxByteCount)
        {
            throw new ArgumentException("位元組數量超過 4 MiB，請分段處理。", nameof(bytes));
        }

        var builder = new StringBuilder(Math.Min(bytes.Length * 3, 4096));
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index != 0 && format != ByteFormat.HexEscape)
            {
                builder.Append(format == ByteFormat.HexComma ? ',' : ' ');
            }

            switch (format)
            {
                case ByteFormat.Hex:
                case ByteFormat.HexComma:
                    builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
                    break;
                case ByteFormat.HexPrefix:
                    builder.Append("0x").Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
                    break;
                case ByteFormat.HexEscape:
                    builder.Append("\\x").Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
                    break;
                case ByteFormat.Decimal:
                    builder.Append(bytes[index].ToString(CultureInfo.InvariantCulture));
                    break;
                case ByteFormat.Binary:
                    builder.Append(Convert.ToString(bytes[index], 2).PadLeft(8, '0'));
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsHex(ByteFormat format) => format is ByteFormat.Hex or ByteFormat.HexPrefix or ByteFormat.HexComma or ByteFormat.HexEscape;

    private static void ValidateFormat(ByteFormat format)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    private static void ExpectPrefixX(string text, ref int position)
    {
        if (position == text.Length || text[position] is not ('x' or 'X'))
        {
            throw new ByteParseException("跳脫位元組必須使用 \\xHH 格式。", position);
        }

        position++;
    }

    private static int ReadDigit(string text, ref int position, int numberBase)
    {
        if (position == text.Length)
        {
            throw new ByteParseException(numberBase == 2 ? "二進位位元組必須有 8 個位元。" : "位元組尚未輸入完整。", position);
        }

        var character = text[position];
        var digit = character switch
        {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _ => -1,
        };
        if (digit < 0 || digit >= numberBase)
        {
            var message = numberBase switch
            {
                16 => "此處需要十六進位數字（0–9、A–F）；每個位元組必須有 2 位數字。",
                2 => "此處需要二進位數字（0 或 1）；每個位元組必須有 8 位數字。",
                _ => "此處需要十進位數字（0–255）。",
            };
            throw new ByteParseException(message, position);
        }

        position++;
        return digit;
    }
}
