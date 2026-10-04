using System.IO;
using HistoLens.Core;

namespace HistoLens;

internal static class SupportedResearchData
{
    // This is a format/source boundary, not a digital signature or an assertion of download origin.
    internal static void ValidateIdentity(DataSnapshot snapshot)
    {
        if (snapshot.Instrument is not { } instrument)
            throw new InvalidDataException("Missing instrument.");
        if (snapshot.IsSynthetic && instrument.SecurityType == SecurityType.Synthetic) return;
        if (!snapshot.IsSynthetic && instrument.SecurityType == SecurityType.CommonStock &&
            instrument.Market is "TWSE" or "TPEx" && (snapshot.SourceId == instrument.Market || snapshot.SourceId == "FinMind") && instrument.Currency == "TWD" &&
            instrument.Code is { Length: 4 } code && code.All(char.IsAsciiDigit)) return;
        throw new InvalidDataException("Unsupported source or inconsistent synthetic/market identity.");
    }
}
