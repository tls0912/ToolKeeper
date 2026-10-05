using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ToolKeeper.UI;
using Xunit;

namespace MarkPad.Tests;

public sealed class LocalizationCatalogTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("ko")]
    public void EveryCatalogCoversTheSourceAndPreservesRuntimeParameters(string language)
    {
        var source = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LocalizationSource.json")))!;
        using var resource = typeof(UiLanguage).Assembly.GetManifestResourceStream($"ToolKeeper.UI.Localization.{language}.json");
        Assert.NotNull(resource);
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
        Assert.Equal(source.Keys.Order(StringComparer.Ordinal), catalog.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, value) in catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.Equal(CompositeFormat.Parse(key).MinimumArgumentCount, CompositeFormat.Parse(value).MinimumArgumentCount);
            Assert.Equal(Regex.Matches(key, @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order(),
                Regex.Matches(value, @"\{\d+(?:[^}]*)\}").Select(match => match.Value).Order());
            Assert.Equal(key.Count(character => character == '\n'), value.Count(character => character == '\n'));
        }
    }
}
