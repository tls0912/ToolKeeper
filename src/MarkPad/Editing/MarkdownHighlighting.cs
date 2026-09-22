using System.IO;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace MarkPad.Editing;

internal static class MarkdownHighlighting
{
    public static IHighlightingDefinition Create(bool dark)
    {
        var heading = dark ? "#79C0FF" : "#0550AE";
        var code = dark ? "#A5D6FF" : "#0A3069";
        var link = dark ? "#58A6FF" : "#0969DA";
        var muted = dark ? "#8B949E" : "#6E7781";
        var marker = dark ? "#D2A8FF" : "#8250DF";
        var definition = $$"""
            <SyntaxDefinition name="Markdown" extensions=".md;.markdown" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Heading" foreground="{{heading}}" fontWeight="bold" />
              <Color name="Code" foreground="{{code}}" />
              <Color name="Link" foreground="{{link}}" />
              <Color name="Muted" foreground="{{muted}}" />
              <Color name="Marker" foreground="{{marker}}" />
              <Color name="Bold" fontWeight="bold" />
              <Color name="Italic" fontStyle="italic" />
              <RuleSet>
                <Span color="Code" multiline="true">
                  <Begin>^\s*```.*$</Begin>
                  <End>^\s*```\s*$</End>
                </Span>
                <Span color="Code" multiline="true">
                  <Begin>^\s*~~~.*$</Begin>
                  <End>^\s*~~~\s*$</End>
                </Span>
                <Rule color="Heading">^ {0,3}\#{1,6}(\s+.*|$)</Rule>
                <Rule color="Heading">^\s*(=+|-+)\s*$</Rule>
                <Span color="Code"><Begin>`+</Begin><End>`+</End></Span>
                <Rule color="Link">!?\[[^\]\r\n]*\]\([^\)\r\n]*\)</Rule>
                <Rule color="Link">https?://[^\s&lt;&gt;]+</Rule>
                <Rule color="Bold">\*\*[^*\r\n]+\*\*|__[^_\r\n]+__</Rule>
                <Rule color="Italic">(?&lt;!\*)\*[^*\r\n]+\*(?!\*)|(?&lt;!\w)_[^_\r\n]+_(?!\w)</Rule>
                <Rule color="Muted">^\s*&gt;.*$</Rule>
                <Rule color="Marker">^\s*([-+*]|\d+[.)])\s+(\[[ xX]\]\s*)?</Rule>
                <Rule color="Muted">&lt;!--.*?--&gt;</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var text = new StringReader(definition);
        using var reader = XmlReader.Create(text);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
