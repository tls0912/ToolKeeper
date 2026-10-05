using System.Text.RegularExpressions;

namespace TransLamp.Core;

/// <summary>Flags source literals that occur less often in the translation; it never changes the translation.</summary>
public static class TranslationQualityCheck
{
    // Structured values are matched before ordinary numbers, so 3 in an IP, D3 or 3000 does not preserve a standalone 3.
    private static readonly Regex Literals = new("""
        (?<quotedPath>["'](?<path>(?:[A-Za-z]:[\\/]|\\\\|\.{1,2}[\\/]|/)[^"'\r\n<>|?*]+)["'])
        | (?<url>(?i:https?://|www\.)[^\s"'<>，。；！？「」『』（）]+)
        | (?<barePath>(?:[A-Za-z]:[\\/]|\\\\|\.{1,2}[\\/]|/)[^\s"'<>|?*，。；！？「」『』（）]+)
        | [0-9]{1,3}(?:\.[0-9]{1,3}){3}(?::[0-9]{1,5})?
        | \[(?:[A-Fa-f0-9]{0,4}:){1,7}[A-Fa-f0-9]{0,4}\](?::[0-9]{1,5})?
        | (?:[A-Fa-f0-9]{0,4}:){2,7}[A-Fa-f0-9]{0,4}
        | (?:0[xX][0-9A-Fa-f]+|(?:2|8|16)\#[0-9A-Fa-f]+)
        | (?<identifier>%?[A-Za-z_][A-Za-z0-9_]*(?:[.-][A-Za-z0-9_]+)*)
        | [-+]?(?:[0-9]+(?:[.,][0-9]+)*|\.[0-9]+)(?:/[0-9]+)?(?:[eE][-+]?[0-9]+)?%?
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static IReadOnlyList<TranslationLiteralDifference> FindChanges(string source, string translation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(translation);
        var sourceCounts = CountLiterals(source);
        if (sourceCounts.Count == 0) return [];
        var translationCounts = CountLiterals(translation, sourceCounts);
        return sourceCounts.Where(pair => translationCounts.GetValueOrDefault(pair.Key) < pair.Value)
            .Select(pair => new TranslationLiteralDifference(pair.Key, pair.Value, translationCounts.GetValueOrDefault(pair.Key)))
            .ToArray();
    }

    private static Dictionary<string, int> CountLiterals(string text, IReadOnlyDictionary<string, int>? expected = null)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in Literals.Matches(text))
        {
            if (match.Index > 0 && IsIdentifierCharacter(text[match.Index - 1]) ||
                match.Index + match.Length < text.Length && IsIdentifierCharacter(text[match.Index + match.Length])) continue;
            var literal = match.Groups["path"].Success ? match.Groups["path"].Value : match.Value;
            if (match.Groups["identifier"].Success && !literal.Any(character => character is >= '0' and <= '9' or '_')) continue;
            if (match.Groups["url"].Success || match.Groups["barePath"].Success) literal = literal.TrimEnd('.', ',', ';', '!', '?');
            if (literal.Length == 0 || expected is not null && !expected.ContainsKey(literal)) continue;
            counts[literal] = counts.GetValueOrDefault(literal) + 1;
        }
        return counts;
    }

    private static bool IsIdentifierCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character == '_';
}

public sealed record TranslationLiteralDifference(string Literal, int SourceOccurrences, int TranslationOccurrences);
