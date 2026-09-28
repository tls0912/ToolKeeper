using System.Globalization;
using System.Text;

namespace ConvAnvil.Services;

public sealed record TextInspection(
    int Utf16Length,
    int UnicodeScalarCount,
    int InvalidSurrogateCount,
    int CrLfCount,
    int LfCount,
    int CrCount,
    int TabCount,
    int NulCount,
    int StxCount,
    int EtxCount,
    int OtherControlCount,
    string LineEnding)
{
    public int CharacterCount => UnicodeScalarCount;
    public bool HasInvalidUtf16 => InvalidSurrogateCount != 0;
    public int ControlCount => CrLfCount * 2 + LfCount + CrCount + TabCount + NulCount + StxCount + EtxCount + OtherControlCount;
}

public static class TextInspector
{
    public static TextInspection Inspect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var scalarCount = 0;
        var invalidSurrogates = 0;
        var crlf = 0;
        var lf = 0;
        var cr = 0;
        var tab = 0;
        var nul = 0;
        var stx = 0;
        var etx = 0;
        var other = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsSurrogate(character))
            {
                if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    scalarCount++;
                    index++;
                }
                else
                {
                    invalidSurrogates++;
                }

                continue;
            }

            scalarCount++;
            switch (character)
            {
                case '\r':
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                    {
                        crlf++;
                    }
                    else
                    {
                        cr++;
                    }

                    break;
                case '\n':
                    if (index == 0 || text[index - 1] != '\r')
                    {
                        lf++;
                    }

                    break;
                case '\t': tab++; break;
                case '\0': nul++; break;
                case '\u0002': stx++; break;
                case '\u0003': etx++; break;
                default:
                    if (char.IsControl(character))
                    {
                        other++;
                    }

                    break;
            }
        }

        var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        var lineEnding = kinds switch
        {
            0 => "None",
            > 1 => "Mixed",
            _ when crlf > 0 => "CRLF",
            _ when lf > 0 => "LF",
            _ => "CR",
        };
        return new TextInspection(text.Length, scalarCount, invalidSurrogates, crlf, lf, cr, tab, nul, stx, etx, other, lineEnding);
    }

    /// <summary>
    /// A read-only diagnostic display. Literal backslashes and opening marker brackets are
    /// escaped so typed escape sequences and marker-shaped text cannot imitate real controls.
    /// </summary>
    public static string VisualizeControls(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(Math.Min(text.Length, 4096));
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            switch (character)
            {
                case '\\': builder.Append("\\\\"); break;
                case '⟦': builder.Append("\\⟦"); break;
                case '\0': builder.Append("⟦NUL⟧"); break;
                case '\u0002': builder.Append("⟦STX⟧"); break;
                case '\u0003': builder.Append("⟦ETX⟧"); break;
                case '\t': builder.Append("⟦TAB⟧"); break;
                case '\r':
                    builder.Append("⟦CR⟧");
                    if (index + 1 == text.Length || text[index + 1] != '\n')
                    {
                        builder.Append('\n');
                    }

                    break;
                case '\n': builder.Append("⟦LF⟧\n"); break;
                default:
                    if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                    {
                        builder.Append(character).Append(text[++index]);
                    }
                    else if (char.IsSurrogate(character))
                    {
                        builder.Append("⟦INVALID U+").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture)).Append('⟧');
                    }
                    else if (char.IsControl(character))
                    {
                        builder.Append("⟦U+").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture)).Append('⟧');
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    public static string NormalizeLineEndings(string text, bool crlf)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ending = crlf ? "\r\n" : "\n";
        var builder = new StringBuilder(Math.Min(text.Length, 4096));
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                builder.Append(ending);
            }
            else if (text[index] == '\n')
            {
                builder.Append(ending);
            }
            else
            {
                builder.Append(text[index]);
            }
        }

        return builder.ToString();
    }
}
