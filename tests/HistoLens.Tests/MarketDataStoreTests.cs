using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HistoLens.Core;
using HistoLens.Data;
using Xunit;

namespace HistoLens.Tests;

public sealed class MarketDataStoreTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetainsExactDownloadAndUnknownActionCoverageForOfflineInspection(bool verified)
    {
        using var directory = new TestDirectory();
        var download = Fixture(verified);
        var store = new MarketDataStore(directory.Path);
        var first = await store.SaveAsync(download);
        var second = await store.SaveAsync(download);
        Assert.Equal(first, second);
        Assert.Single(store.List());
        var loaded = await MarketDataStore.LoadAsync(first);
        Assert.Equal(JsonSerializer.Serialize(download), JsonSerializer.Serialize(loaded));
        Assert.Equal(verified, loaded.Snapshot.ActionCoverage.IsVerified);
        var run = Run(loaded.Snapshot);
        Assert.Equal(verified, run.IsResearchAllowed);
        Assert.False(run.IsSynthetic);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task CompletedTwseResearchCanBeSavedAndReopenedWithItsIdentity()
    {
        using var directory = new TestDirectory();
        var snapshot = Fixture(true).Snapshot;
        var run = Run(snapshot);
        Assert.True(run.IsResearchAllowed);
        var path = await new ResearchStore(directory.Path).SaveAsync(snapshot, run);
        var loaded = await ResearchStore.LoadAsync(path);
        Assert.False(loaded.Snapshot.IsSynthetic);
        Assert.False(loaded.Run.IsSynthetic);
        Assert.Equal("TWSE", loaded.Run.SourceId);
        Assert.Equal(JsonSerializer.Serialize(run), JsonSerializer.Serialize(loaded.Run));
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResearchStore(directory.Path)
            .SaveAsync(snapshot, run with { IsSynthetic = true }));
    }

    [Fact]
    public async Task UnknownActionCoverageCannotBeSavedAsCompletedResearch()
    {
        using var directory = new TestDirectory();
        var snapshot = Fixture(false).Snapshot;
        await Assert.ThrowsAsync<InvalidDataException>(() => new ResearchStore(directory.Path).SaveAsync(snapshot, Run(snapshot)));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("synthetic")]
    [InlineData("instrument-code")]
    [InlineData("null-bars")]
    [InlineData("null-bar")]
    [InlineData("null-diagnostics")]
    [InlineData("hash")]
    [InlineData("duplicate-date")]
    public async Task RejectsUnsupportedOrMalformedDownloadsBeforeWriting(string mutation)
    {
        using var directory = new TestDirectory();
        var download = Fixture(true);
        download = mutation switch
        {
            "source" => download with { Snapshot = download.Snapshot with { SourceId = "UNSUPPORTED" } },
            "synthetic" => download with { Snapshot = download.Snapshot with { IsSynthetic = true } },
            "instrument-code" => download with { Snapshot = download.Snapshot with { Instrument = download.Snapshot.Instrument with { Code = null! } } },
            "null-bars" => download with { Snapshot = download.Snapshot with { Bars = null! } },
            "null-bar" => download with { Snapshot = download.Snapshot with { Bars = [null!] } },
            "null-diagnostics" => download with { Diagnostics = null! },
            "hash" => download with { Snapshot = download.Snapshot with { ContentHash = new string('0', 64) } },
            "duplicate-date" => download with { Snapshot = download.Snapshot with { Bars = download.Snapshot.Bars.Append(download.Snapshot.Bars[0]).ToArray() } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => new MarketDataStore(directory.Path).SaveAsync(download));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task DetectsCorruptionAndRejectsInvalidStructureEvenWhenEnvelopeChecksumWasRecomputed()
    {
        using var directory = new TestDirectory();
        var path = await new MarketDataStore(directory.Path).SaveAsync(Fixture(true));
        using var original = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var payload = original.RootElement.GetProperty("Payload").GetString()!;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { FormatVersion = 1, Checksum = "wrong", Payload = payload }));
        await Assert.ThrowsAsync<InvalidDataException>(() => MarketDataStore.LoadAsync(path));
        var node = System.Text.Json.Nodes.JsonNode.Parse(payload)!;
        node["Download"]!["Snapshot"]!["Bars"] = null;
        payload = node.ToJsonString();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { FormatVersion = 1, Checksum = checksum, Payload = payload }));
        await Assert.ThrowsAsync<InvalidDataException>(() => MarketDataStore.LoadAsync(path));
    }

    [Fact]
    public async Task CancellationAndOversizedInputLeaveNoNewDownload()
    {
        using var directory = new TestDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new MarketDataStore(directory.Path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Fixture(true), cancellation.Token));
        Assert.Empty(store.List());
        var oversized = System.IO.Path.Combine(directory.Path, "too-large.twse-data.json");
        using (var stream = File.Create(oversized)) stream.SetLength((long)ResearchStore.MaximumBytes + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => MarketDataStore.LoadAsync(oversized));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MarketDataStore.LoadAsync(oversized, cancellation.Token));
    }

    internal static HistoricalDataDownload Fixture(bool verified)
    {
        // Generated offline test data; never packaged as a real market download.
        var demo = DemoData.Create();
        var snapshot = demo with
        {
            IsSynthetic = false, SourceId = "TWSE", DataVersion = "unit-test-fixture", SnapshotId = "", ContentHash = "",
            Instrument = demo.Instrument with { InstrumentId = "TWSE:TEST", Code = "2330", Name = "Unit test fixture; not market data", Market = "TWSE", SecurityType = SecurityType.CommonStock },
            Calendar = demo.Calendar with { Market = "TWSE", Version = "unit-test-calendar" },
            ActionCoverage = demo.ActionCoverage with { IsVerified = verified, SourceId = "TWSE", Version = "unit-test-actions" },
            Bars = demo.Bars.Select(bar => bar with { SourceId = "TWSE" }).ToArray(),
            CorporateActions = demo.CorporateActions.Select(action => action with { SourceId = "TWSE" }).ToArray()
        };
        var hash = SnapshotFingerprint.Compute(snapshot);
        snapshot = snapshot with { SnapshotId = hash, ContentHash = hash };
        return new(snapshot, SnapshotValidator.Validate(snapshot));
    }

    private static ResearchRun Run(DataSnapshot snapshot) => new ResearchEngine().Run(snapshot,
        ResearchTemplates.CreateDefinition("HL-R002", snapshot.Calendar.TradingDates[130], snapshot.DataAsOf, snapshot.DataAsOf));

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoLens-market-tests-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
