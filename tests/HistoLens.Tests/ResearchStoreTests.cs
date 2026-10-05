using System.IO;
using HistoLens.Core;
using Xunit;

namespace HistoLens.Tests;

public sealed class ResearchStoreTests
{
    [Fact]
    public async Task SaveRetainsExactSnapshotAndRunWithoutOverwritingOlderResults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ResearchStore(directory);
            var snapshot = DemoData.Create();
            var definition = ResearchTemplates.CreateDefinition("HL-R002", snapshot.Calendar.TradingDates[130], snapshot.DataAsOf, snapshot.DataAsOf);
            var run = new ResearchEngine().Run(snapshot, definition);
            var first = await store.SaveAsync(snapshot, run);
            var second = await store.SaveAsync(snapshot, run);
            Assert.NotEqual(first, second);
            Assert.Equal(2, store.List().Length);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            var loaded = await ResearchStore.LoadAsync(first);
            Assert.Equal(SnapshotFingerprint.Compute(snapshot), SnapshotFingerprint.Compute(loaded.Snapshot));
            Assert.Equal(run.EngineVersion, loaded.Run.EngineVersion);
            var rerun = new ResearchEngine().Run(loaded.Snapshot, loaded.Run.Definition);
            Assert.Equal(run.Funnel, rerun.Funnel);
            Assert.Equal(run.Cases.Select(c => c.EventDate), rerun.Cases.Select(c => c.EventDate));
            Assert.Equal(run.Statistics.Select(s => s.MeanPriceChange), rerun.Statistics.Select(s => s.MeanPriceChange));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(snapshot, run, new CancellationToken(true)));
            Assert.Equal(2, store.List().Length);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            var before = await File.ReadAllTextAsync(first);
            await File.WriteAllTextAsync(first, before.Replace("DEMO-006", "DEMO-999", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => ResearchStore.LoadAsync(first));
            Assert.Equal(run.Funnel, (await ResearchStore.LoadAsync(second)).Run.Funnel);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RejectsInconsistentSyntheticIdentityOrMismatchedDataWithoutCreatingAFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HistoLens.Tests", Guid.NewGuid().ToString("N"));
        var snapshot = DemoData.Create();
        var run = new ResearchEngine().Run(snapshot, ResearchTemplates.CreateDefinition("HL-R002", snapshot.Calendar.TradingDates[130], snapshot.DataAsOf, snapshot.DataAsOf));
        var store = new ResearchStore(directory);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(snapshot with { IsSynthetic = false }, run));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(snapshot with { DataVersion = "modified" }, run));
        Assert.False(Directory.Exists(directory));
    }
}
