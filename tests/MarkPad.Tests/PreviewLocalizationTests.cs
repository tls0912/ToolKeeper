using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MarkPad.Editing;
using Xunit;

namespace MarkPad.Tests;

public sealed class PreviewLocalizationTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("ko")]
    public Task FloatingEditorToolbarUsesSelectedLanguageAndKeepsSourceDirection(string language) => StaTest.Run(() =>
    {
        var pane = new EditorPane { FlowDirection = FlowDirection.RightToLeft };
        pane.ApplyLanguage(language);
        Assert.Equal(FlowDirection.LeftToRight, pane.Editor.FlowDirection);
        var border = Assert.IsType<Border>(typeof(EditorPane).GetField("_popupBorder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pane));
        Assert.Equal(language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, border.FlowDirection);
        var buttons = Assert.IsType<StackPanel>(border.Child).Children.OfType<Button>().ToArray();
        string[] english = ["Bold · Ctrl+B", "Italic · Ctrl+I", "Inline code", "Link"];
        Assert.Equal(english.Length, buttons.Length);
        for (var index = 0; index < buttons.Length; index++)
        {
            var tooltip = Assert.IsType<string>(buttons[index].ToolTip);
            Assert.False(string.IsNullOrWhiteSpace(tooltip));
            Assert.NotEqual(english[index], tooltip);
        }
        pane.ApplyLanguage("en");
        Assert.Equal(FlowDirection.LeftToRight, border.FlowDirection);
        Assert.Equal(english, buttons.Select(button => Assert.IsType<string>(button.ToolTip)).ToArray());
        return Task.CompletedTask;
    });
}
