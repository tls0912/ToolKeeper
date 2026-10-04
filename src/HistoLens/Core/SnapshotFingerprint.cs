using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HistoLens.Core;

public static class SnapshotFingerprint
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        Converters = { new CanonicalDecimalConverter() }
    };

    /// <summary>Canonical input content; snapshot IDs, supplied hashes and retrieval timestamps are not content.</summary>
    public static string Compute(DataSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // Sorting is solely for canonical serialization and does not repair duplicates or invalid input.
        var canonical = snapshot with
        {
            SnapshotId = "", ContentHash = "", RetrievedAtUtc = default,
            Bars = snapshot.Bars.OrderBy(b => b.Date).ToArray(),
            Calendar = snapshot.Calendar with { TradingDates = snapshot.Calendar.TradingDates.Order().ToArray() },
            CorporateActions = snapshot.CorporateActions.OrderBy(a => a.EffectiveDate)
                .ThenBy(a => a.Kind, StringComparer.Ordinal).ThenBy(a => a.SourceId, StringComparer.Ordinal)
                .ThenBy(a => a.AffectsPriceComparison).ToArray(),
            ActionCoverage = snapshot.ActionCoverage with
            {
                Gaps = snapshot.ActionCoverage.Gaps.OrderBy(g => g.Start).ThenBy(g => g.End).ToArray()
            },
            ComparabilityCoverage = snapshot.ComparabilityCoverage is { } comparison ? comparison with
            {
                Gaps = comparison.Gaps.OrderBy(g => g.Start).ThenBy(g => g.End).ToArray()
            } : null
        };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical, CanonicalOptions)));
    }

    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();

        // Decimal scale is display precision, not a change to the actual price/volume content.
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }
}
