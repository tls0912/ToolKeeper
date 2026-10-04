using System.IO;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HistoLens.Core;
using HistoLens.Data;

namespace HistoLens;

/// <summary>One atomic local cache per source and stable market/security identity, retaining incomplete coverage honestly.</summary>
public sealed class MarketDataStore(string directory)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true
    };
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public IReadOnlyList<string> List() => ListAsync().GetAwaiter().GetResult();
    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        !Directory.Exists(DirectoryPath) ? Task.FromResult<IReadOnlyList<string>>([]) : ConsolidateAsync(cancellationToken);

    public async Task<IReadOnlyList<MarketDataEntry>> ListEntriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(DirectoryPath)) return [];
        var entries = await ConsolidateEntriesAsync(cancellationToken).ConfigureAwait(false);
        return entries.Select(entry =>
        {
            var snapshot = entry.Download.Snapshot;
            var priceDates = snapshot.Bars.Where(bar => bar.Status == TradingStatus.Traded && bar.Close > 0)
                .Select(bar => (DateOnly?)bar.Date).ToArray();
            return new MarketDataEntry(entry.Path, snapshot.Instrument.Code, snapshot.Instrument.Name,
                snapshot.Calendar.CoverageStart, snapshot.Calendar.CoverageEnd,
                priceDates.Min(), priceDates.Max(), snapshot.Bars.Count,
                entry.Download.Diagnostics.Any(issue => issue.BlocksResearch) ||
                SnapshotValidator.Validate(snapshot, cancellationToken).Any(issue => issue.BlocksResearch),
                Market: snapshot.Instrument.Market, SourceId: snapshot.SourceId);
        }).ToArray();
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var selectedPath = ActiveDeletePath(path);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDirectoriesAreNotReparsePoints(DirectoryPath);
        using var locked = await LockAsync(cancellationToken).ConfigureAwait(false);
        EnsureDirectoriesAreNotReparsePoints(DirectoryPath);
        EnsureActiveFile(selectedPath);
        var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
        var selected = entries.FirstOrDefault(entry => SamePath(entry.Path, selectedPath))
            ?? throw new FileNotFoundException("Selected market data file no longer exists.", selectedPath);
        var identity = CacheIdentity(selected.Download);
        var matches = entries.Where(entry => CacheIdentity(entry.Download) == identity).ToArray();
        foreach (var entry in matches) EnsureActiveFile(entry.Path);
        if (matches.Any(entry => !SamePath(entry.Path, selectedPath)))
            EnsureDirectoriesAreNotReparsePoints(Path.Combine(DirectoryPath, "legacy"));
        cancellationToken.ThrowIfCancellationRequested();
        // Retain recoverable duplicates outside the active inventory before committing deletion.
        // Otherwise a later refresh would recreate the selected stock from an older top-level file.
        ArchiveLegacy(matches, selectedPath);
        File.Delete(selectedPath);
    }

    public async Task<string> SaveAsync(HistoricalDataDownload download, CancellationToken cancellationToken = default)
    {
        Validate(download, cancellationToken);
        return (await UpdateAsync(download.Snapshot.Instrument.Code, download.Snapshot.Instrument.Market, download.Snapshot.SourceId,
            (_, _) => Task.FromResult(download), cancellationToken)
            .ConfigureAwait(false)).Path;
    }

    // The same directory lock covers read/plan/download/merge/replace, including other store instances/processes.
    internal Task<(string Path, HistoricalDataDownload Download)> UpdateAsync(string code,
        Func<HistoricalDataDownload?, CancellationToken, Task<HistoricalDataDownload>> update,
        CancellationToken cancellationToken) => UpdateAsync(code, "TWSE", update, cancellationToken);

    internal Task<(string Path, HistoricalDataDownload Download)> UpdateAsync(string code, string market,
        Func<HistoricalDataDownload?, CancellationToken, Task<HistoricalDataDownload>> update,
        CancellationToken cancellationToken) => UpdateAsync(code, market, market, update, cancellationToken);

    internal async Task<(string Path, HistoricalDataDownload Download)> UpdateAsync(string code, string market, string sourceId,
        Func<HistoricalDataDownload?, CancellationToken, Task<HistoricalDataDownload>> update,
        CancellationToken cancellationToken)
    {
        if (market is not ("TWSE" or "TPEx")) throw new ArgumentException("Unsupported market.", nameof(market));
        if (sourceId != market && sourceId != "FinMind") throw new ArgumentException("Unsupported data source.", nameof(sourceId));
        using var locked = await LockAsync(cancellationToken).ConfigureAwait(false);
        var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
        var matches = entries.Where(entry => entry.Download.Snapshot.SourceId == sourceId && entry.Download.Snapshot.Instrument.Market == market &&
            entry.Download.Snapshot.Instrument.Code == code).ToArray();
        if (matches.Select(entry => MarketDataMerge.IdentityKey(entry.Download.Snapshot.Instrument)).Distinct().Count() > 1)
            throw new InvalidDataException($"{market} {code} 的本機資料有不同穩定身份；未合併或下載，請先檢查原檔。");
        var existing = matches.Length == 0 ? null : MarketDataMerge.Merge(matches.Select(entry => entry.Download));
        var incoming = await update(existing, cancellationToken).ConfigureAwait(false);
        Validate(incoming, cancellationToken);
        if (incoming.Snapshot.Instrument.Code != code || incoming.Snapshot.Instrument.Market != market || incoming.Snapshot.SourceId != sourceId)
            throw new InvalidDataException("回傳行情的來源、市場或股票代號與要求不符。");
        var merged = existing is null || ReferenceEquals(existing, incoming) ? incoming : MarketDataMerge.Merge([existing, incoming]);
        Validate(merged, cancellationToken);
        var final = CanonicalPath(merged);
        if (matches.Length != 1 || !SamePath(matches[0].Path, final) || !ReferenceEquals(existing, incoming))
            await WriteCoreAsync(final, merged, cancellationToken).ConfigureAwait(false);
        ArchiveLegacy(matches, final);
        return (final, merged);
    }

    private async Task<IReadOnlyList<string>> ConsolidateAsync(CancellationToken cancellationToken)
        => (await ConsolidateEntriesAsync(cancellationToken).ConfigureAwait(false)).Select(entry => entry.Path).ToArray();

    private async Task<Entry[]> ConsolidateEntriesAsync(CancellationToken cancellationToken)
    {
        using var locked = await LockAsync(cancellationToken).ConfigureAwait(false);
        var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<Entry>();
        foreach (var group in entries.GroupBy(entry => CacheIdentity(entry.Download)))
        {
            var members = group.ToArray();
            var merged = MarketDataMerge.Merge(members.Select(entry => entry.Download));
            var final = CanonicalPath(merged);
            if (members.Length != 1 || !SamePath(members[0].Path, final))
                await WriteCoreAsync(final, merged, cancellationToken).ConfigureAwait(false);
            ArchiveLegacy(members, final);
            result.Add(new(final, merged));
        }
        return result.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    private string CanonicalPath(HistoricalDataDownload download)
    {
        var instrument = download.Snapshot.Instrument;
        var identityHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(MarketDataMerge.IdentityKey(instrument))));
        var sourcePrefix = download.Snapshot.SourceId == instrument.Market ? "" : download.Snapshot.SourceId + "-";
        return Path.Combine(DirectoryPath, $"{sourcePrefix}{instrument.Market}-{instrument.Code}-{identityHash[..24]}.market-data.json");
    }

    private static string CacheIdentity(HistoricalDataDownload download) => download.Snapshot.SourceId + "\n" +
        MarketDataMerge.IdentityKey(download.Snapshot.Instrument);

    private async Task<Entry[]> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        var entries = new List<Entry>();
        foreach (var path in Directory.GetFiles(DirectoryPath, "*.json").Where(IsMarketDataFile).Order(StringComparer.Ordinal))
        {
            // A corrupt file cannot safely be attributed to another stock. Surface it instead of silently creating a new cache.
            var download = await LoadCoreAsync(path, cancellationToken).ConfigureAwait(false);
            entries.Add(new(path, download));
        }
        return entries.OrderBy(entry => SamePath(entry.Path, CanonicalPath(entry.Download)) ? 0 : 1)
            .ThenBy(entry => entry.Download.Snapshot.RetrievedAtUtc).ThenBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    private void ArchiveLegacy(IEnumerable<Entry> entries, string final)
    {
        foreach (var entry in entries.Where(entry => !SamePath(entry.Path, final)))
        {
            var backupDirectory = Path.Combine(DirectoryPath, "legacy");
            Directory.CreateDirectory(backupDirectory);
            File.Move(entry.Path, Path.Combine(backupDirectory, Path.GetFileName(entry.Path) + "." + Guid.NewGuid().ToString("N") + ".bak"));
        }
    }

    private static bool SamePath(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private string ActiveDeletePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!SamePath(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(fullPath) ?? ""),
                Path.TrimEndingDirectorySeparator(DirectoryPath)) ||
                !IsMarketDataFile(fullPath))
            throw new ArgumentException("Only an active market data file directly in this store can be deleted.", nameof(path));
        return fullPath;
    }

    // Existing files retain their original source metadata. The old suffix is read-only compatibility.
    private static bool IsMarketDataFile(string path) =>
        path.EndsWith(".market-data.json", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".twse-data.json", StringComparison.OrdinalIgnoreCase);

    private static void EnsureActiveFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("A market data deletion target cannot be a directory or symbolic link.");
    }

    private static void EnsureDirectoriesAreNotReparsePoints(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A market data deletion directory cannot be a symbolic link or junction.");
    }

    private async Task<IDisposable> LockAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = Gates.GetOrAdd(DirectoryPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var stream = new FileStream(Path.Combine(DirectoryPath, ".market-data.lock"), FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                    return new StoreLock(gate, stream);
                }
                catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33)
                { await Task.Delay(50, cancellationToken).ConfigureAwait(false); }
            }
        }
        catch { gate.Release(); throw; }
    }

    private static async Task WriteCoreAsync(string final, HistoricalDataDownload download, CancellationToken cancellationToken)
    {
        Validate(download, cancellationToken);
        var document = new Document(2, DateTimeOffset.UtcNow, download);
        using var buffer = new MemoryStream();
        using (var bounded = new ResearchStore.BoundedWriteStream(buffer))
            await JsonSerializer.SerializeAsync(bounded, document, Json, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var count = checked((int)buffer.Length);
        var checksum = Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, count)));
        var envelope = new Envelope(2, checksum, Encoding.UTF8.GetString(buffer.GetBuffer(), 0, count));
        var temporary = final + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                using var bounded = new ResearchStore.BoundedWriteStream(stream);
                await JsonSerializer.SerializeAsync(bounded, envelope, Json, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Verify the durable candidate before replacing the only active cache or archiving legacy sources.
            await LoadCoreAsync(temporary, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(final)) File.Replace(temporary, final, null);
            else File.Move(temporary, final);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task<HistoricalDataDownload> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCoreAsync(path, cancellationToken), cancellationToken);

    private static async Task<HistoricalDataDownload> LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length > ResearchStore.MaximumBytes) throw new InvalidDataException("Market data file exceeds 64 MiB.");
        try
        {
            var envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, Json, cancellationToken).ConfigureAwait(false);
            if (envelope is not { FormatVersion: 1 or 2, Payload: not null } ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))),
                    envelope.Checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Market data version or checksum is invalid.");
            cancellationToken.ThrowIfCancellationRequested();
            using var payload = new MemoryStream(Encoding.UTF8.GetBytes(envelope.Payload), false);
            var document = await JsonSerializer.DeserializeAsync<Document>(payload, Json, cancellationToken).ConfigureAwait(false);
            if (document is not { FormatVersion: 1 or 2 } || document.SavedAtUtc == default)
                throw new InvalidDataException("Unsupported or incomplete market data file.");
            Validate(document.Download, cancellationToken);
            return document.Download;
        }
        catch (JsonException error) { throw new InvalidDataException("Market data JSON is invalid.", error); }
    }

    internal static void Validate(HistoricalDataDownload download, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (download?.Snapshot is not { } snapshot || snapshot.IsSynthetic ||
            download.Diagnostics is null || download.Diagnostics.Any(issue => issue is null ||
                string.IsNullOrWhiteSpace(issue.Code) || issue.Message is null))
            throw new InvalidDataException("Missing market data or diagnostics.");
        SupportedResearchData.ValidateIdentity(snapshot);
        if (snapshot.Calendar?.TradingDates is null || snapshot.ActionCoverage?.Gaps is null ||
            snapshot.Bars is null || snapshot.CorporateActions is null ||
            snapshot.Bars.Any(bar => bar is null) || snapshot.CorporateActions.Any(action => action is null) ||
            snapshot.ActionCoverage.Gaps.Any(gap => gap is null) || snapshot.RetrievedAtUtc == default ||
            (snapshot.ComparabilityCoverage is { } comparison && (comparison.Gaps is null || comparison.Gaps.Any(gap => gap is null))) ||
            string.IsNullOrWhiteSpace(snapshot.SnapshotId) || string.IsNullOrWhiteSpace(snapshot.ContentHash))
            throw new InvalidDataException("Missing market data structure or metadata.");
        var issues = SnapshotValidator.Validate(snapshot, cancellationToken);
        // Unknown coverage is kept honestly for diagnostics; inconsistent structure never enters the cache.
        if (issues.Any(issue => issue.BlocksResearch && issue.Code is not
                ("CorporateActionCoverageUnknown" or "CalendarUnverified" or "CalendarEmpty")))
            throw new InvalidDataException("Market data structure or identity is invalid.");
        if (!string.Equals(SnapshotFingerprint.Compute(snapshot), snapshot.ContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Market data snapshot checksum does not match its content.");
        ValidateSourceEvidence(download.SourceEvidence, cancellationToken);
        if (download.CacheCoverage is { } cache)
        {
            if (cache.CheckedPriceRanges is null || cache.VerifiedCalendarRanges is null)
                throw new InvalidDataException("Market cache coverage is incomplete.");
            foreach (var ranges in new[] { cache.CheckedPriceRanges, cache.VerifiedCalendarRanges, cache.KnownActionRanges,
                         cache.ActionEvidenceGaps, cache.KnownComparisonRanges, cache.ComparisonEvidenceGaps })
                if (ranges is not null && ranges.Any(range => range is null || range.Start > range.End ||
                    range.Start < snapshot.Calendar.CoverageStart || range.End > snapshot.DataAsOf))
                    throw new InvalidDataException("Market cache coverage range is invalid.");
            var completeCalendar = MarketDataMerge.Complement(cache.VerifiedCalendarRanges,
                snapshot.Calendar.CoverageStart, snapshot.Calendar.CoverageEnd).Count == 0;
            if (completeCalendar != snapshot.Calendar.IsVerified)
                throw new InvalidDataException("Market cache calendar coverage contradicts its snapshot.");
            if (cache.ConfirmedNoPriceDates is { } noPriceDates)
            {
                var bars = snapshot.Bars.ToDictionary(bar => bar.Date);
                if (noPriceDates.Count != noPriceDates.Distinct().Count() || noPriceDates.Any(date =>
                    !MarketDataMerge.Contains(cache.CheckedPriceRanges, date) ||
                    !MarketDataMerge.Contains(cache.VerifiedCalendarRanges, date) ||
                    !snapshot.Calendar.TradingDates.Contains(date) || !bars.TryGetValue(date, out var bar) ||
                    !SnapshotValidator.GetBarReasons(bar, false).Contains(ExclusionReason.MissingPrice)))
                    throw new InvalidDataException("Confirmed no-price metadata lacks a queried trading-day source row.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ValidateSourceEvidence(IReadOnlyList<SourceResponseEvidence>? evidenceItems,
        CancellationToken cancellationToken)
    {
        if (evidenceItems is null) return;
        foreach (var evidence in evidenceItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (evidence is null || string.IsNullOrWhiteSpace(evidence.Dataset) || evidence.RetrievedAtUtc == default ||
                evidence.RowCount < 0 || string.IsNullOrWhiteSpace(evidence.RawJson) ||
                evidence.Sha256 is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigit) ||
                !Uri.TryCreate(evidence.ResourceUrl, UriKind.Absolute, out var resource) ||
                resource.Scheme != Uri.UriSchemeHttps || resource.Host != "api.finmindtrade.com" ||
                resource.AbsolutePath != "/api/v4/data" || !resource.IsDefaultPort ||
                resource.UserInfo.Length != 0 || resource.Fragment.Length != 0)
                throw new InvalidDataException("Source response evidence metadata is invalid.");
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in resource.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                if (parts.Length != 2 || key is not ("dataset" or "data_id" or "start_date" or "end_date") ||
                    !parameters.TryAdd(key, Uri.UnescapeDataString(parts[1])))
                    throw new InvalidDataException("Source evidence URL contains unsupported or sensitive parameters.");
            }
            if (!parameters.TryGetValue("dataset", out var dataset) || dataset != evidence.Dataset ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.RawJson))),
                    hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source response evidence dataset or checksum does not match.");
            try
            {
                using var response = JsonDocument.Parse(evidence.RawJson);
                if (response.RootElement.ValueKind != JsonValueKind.Object ||
                    !response.RootElement.TryGetProperty("status", out var status) ||
                    status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var code) || code != 200 ||
                    !response.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array ||
                    rows.GetArrayLength() != evidence.RowCount || rows.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object))
                    throw new InvalidDataException("Source response evidence is not a successful data response.");
            }
            catch (JsonException error) { throw new InvalidDataException("Source response evidence JSON is invalid.", error); }
        }
    }

    private sealed record Entry(string Path, HistoricalDataDownload Download);
    private sealed class StoreLock(SemaphoreSlim gate, FileStream stream) : IDisposable
    {
        public void Dispose() { try { stream.Dispose(); } finally { gate.Release(); } }
    }
    private sealed record Document(int FormatVersion, DateTimeOffset SavedAtUtc, HistoricalDataDownload Download);
    private sealed record Envelope(int FormatVersion, string Checksum, string Payload);
}
