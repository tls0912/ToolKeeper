using System.IO;
using HistoLens.Core;
using Xunit;

namespace HistoLens.Tests;

public sealed class IndicatorPreferencesStoreTests
{
    [Theory]
    [InlineData(SimilarityIndicator.None)]
    [InlineData(SimilarityIndicator.Default)]
    [InlineData(SimilarityIndicator.All)]
    [InlineData(SimilarityIndicator.Rsi | SimilarityIndicator.VolumePath)]
    public void ValidMasksSurviveReplacementWithoutChangingUiPreferences(SimilarityIndicator selected)
    {
        var directory = NewDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var ui = Path.Combine(directory, "ui.json"); File.WriteAllText(ui, "existing UI preferences");
            var store = new IndicatorPreferencesStore(directory);
            store.Save(SimilarityIndicator.All); store.Save(selected);
            Assert.Equal(selected, new IndicatorPreferencesStore(directory).Load());
            Assert.Equal("existing UI preferences", File.ReadAllText(ui));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("null")]
    [InlineData("{\"Version\":1}")]
    [InlineData("{\"Version\":2,\"SelectedIndicators\":32}")]
    [InlineData("{\"Version\":1,\"SelectedIndicators\":1024}")]
    [InlineData("{\"Version\":1,\"SelectedIndicators\":-1}")]
    [InlineData("{\"Version\":1,\"SelectedIndicators\":\"Rsi\"}")]
    public void InvalidDocumentsFallBackWithoutOverwritingTheSource(string content)
    {
        var directory = NewDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "indicator-preferences.json"); File.WriteAllText(path, content);
            Assert.Equal(SimilarityIndicator.Default, new IndicatorPreferencesStore(directory).Load());
            Assert.Equal(content, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void MissingPreferencesUseTheOriginalFiveWithoutCreatingAFile()
    {
        var directory = NewDirectory();
        Assert.Equal(SimilarityIndicator.Default, new IndicatorPreferencesStore(directory).Load());
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void UnknownMasksCannotReplacePreviouslySavedIndicators()
    {
        var directory = NewDirectory();
        try
        {
            var store = new IndicatorPreferencesStore(directory); store.Save(SimilarityIndicator.Rsi);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Save((SimilarityIndicator)1024));
            Assert.Equal(SimilarityIndicator.Rsi, store.Load());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), "HistoLens.IndicatorStore-" + Guid.NewGuid().ToString("N"));
}
