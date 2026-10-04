using System.IO;
using System.Reflection;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MarkPad.Rendering;
using Xunit;

namespace MarkPad.Tests;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void ExecutableHtmlAndEventHandlersAreRemovedFromDocumentContent()
    {
        var document = Render("""
            <script>alert('script')</script>
            <iframe src="https://example.invalid/embed"></iframe>
            <object data="file:///C:/private.txt"></object>
            <svg onload="alert('svg')"><script>alert('nested')</script></svg>
            <style>body { background: url(https://example.invalid/pixel); }</style>
            <form action="https://example.invalid/upload"><input name="content"></form>
            <p onclick="alert('event')" style="background:url(https://example.invalid/pixel)">Safe text</p>
            <a href="javascript:alert('link')">Unsafe link</a>
            """);
        var content = document.QuerySelector("#document")!;
        Assert.Empty(content.QuerySelectorAll("script,iframe,object,embed,svg,style,form,input"));
        Assert.Contains("Safe text", content.TextContent);
        Assert.Null(content.QuerySelector("a")!.GetAttribute("href"));
        Assert.All(content.QuerySelectorAll("*"), element =>
            Assert.DoesNotContain(element.Attributes, attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || attribute.Name == "style"));
    }

    [Fact]
    public void SafeInlineAndBlockHtmlRemainReadable()
    {
        var document = Render("""
            <details open><summary>More information</summary><p><strong>Strong</strong> and <em>emphasis</em>, <kbd>Ctrl</kbd>.</p></details>

            <abbr title="Markdown">MD</abbr> and <mark>highlight</mark>

            <table><thead><tr><th>Title</th></tr></thead><tbody><tr><td>Cell</td></tr></tbody></table>
            """);
        Assert.NotNull(document.QuerySelector("#document details[open] summary"));
        Assert.Equal("Strong", document.QuerySelector("#document strong")!.TextContent);
        Assert.Equal("Markdown", document.QuerySelector("#document abbr")!.GetAttribute("title"));
        Assert.Equal("Cell", document.QuerySelector("#document table tbody td")!.TextContent);
    }

    [Fact]
    public void GfmTasksCarryTheirOriginalSourceLinesAndKeepCheckedState()
    {
        var document = Render("# Tasks\n\n- [ ] first\n- [x] done\n\n> - [ ] quoted\n");
        var tasks = document.QuerySelectorAll("#document input[type=checkbox]");
        Assert.Equal(3, tasks.Length);
        Assert.Equal(new[] { "3", "4", "6" }, tasks.Select(task => task.GetAttribute("data-task-line")));
        Assert.False(tasks[0].HasAttribute("checked"));
        Assert.True(tasks[1].HasAttribute("checked"));
        Assert.False(tasks[2].HasAttribute("checked"));
        Assert.NotNull(document.QuerySelector("#document h1[id=tasks][data-source-line='1']"));
    }

    [Fact]
    public void MarkdownCannotForgeInteractiveCheckboxesAndReadOnlyTasksAreDisabled()
    {
        const string markdown = "- [x] real\n\n<input type=checkbox data-task-line=999>\n<span data-task-token=fake>x</span>";
        var document = Render(markdown, new PreviewOptions(ReadOnly: true));
        var task = Assert.Single(document.QuerySelectorAll("#document input"));
        Assert.Equal("1", task.GetAttribute("data-task-line"));
        Assert.True(task.HasAttribute("checked"));
        Assert.True(task.HasAttribute("disabled"));
        Assert.Empty(document.QuerySelectorAll("#document [data-task-token]"));
    }

    [Fact]
    public void PreviewDoesNotLoadRemoteImagesStylesFramesOrScripts()
    {
        var document = Render("""
            ![tracking pixel](https://example.invalid/pixel.png)
            ![network image](//example.invalid/pixel.png)
            <img src="data:image/png;base64,AAAA" alt="embedded">
            <link rel="stylesheet" href="https://example.invalid/style.css">
            <script src="https://example.invalid/script.js"></script>
            """);
        Assert.Empty(document.QuerySelector("#document")!.QuerySelectorAll("img,link,iframe,script"));
        Assert.Equal(3, document.QuerySelectorAll("#document .image-unavailable").Length);
        var policy = document.QuerySelector("meta[http-equiv='Content-Security-Policy']")!.GetAttribute("content")!;
        Assert.Contains("default-src 'none'", policy);
        Assert.Contains("connect-src 'none'", policy);
        Assert.Contains("frame-src 'none'", policy);
        Assert.Contains("img-src https://markpad.local", policy);
        var script = Assert.Single(document.QuerySelectorAll("script"));
        Assert.False(script.HasAttribute("src"));
        Assert.Contains($"script-src 'nonce-{script.GetAttribute("nonce")}'", policy);
    }

    [Fact]
    public void LocalImagesStayInsideDocumentDirectoryAndAreMappedToPrivatePreviewUrls()
    {
        using var directory = new TestDirectory();
        var documents = directory.FilePath("documents");
        var images = Path.Combine(documents, "images");
        Directory.CreateDirectory(images);
        var imagePath = Path.Combine(images, "my photo.png");
        File.WriteAllBytes(imagePath, [137, 80, 78, 71]);
        File.WriteAllBytes(directory.FilePath("outside.png"), [137, 80, 78, 71]);
        var markdownPath = Path.Combine(documents, "notes.md");

        Assert.True(MarkdownRenderer.TryResolveImagePath(markdownPath, "images/my%20photo.png", out var resolved));
        Assert.Equal(imagePath, resolved);
        foreach (var unsafeSource in new[]
        {
            "../outside.png", "%2e%2e/outside.png", "images/../../outside.png",
            "https://example.invalid/image.png", "//example.invalid/image.png", @"\\server\share\image.png",
            "file:///C:/private.png", imagePath, "images/not-found.png", "images/secrets.txt"
        }) Assert.False(MarkdownRenderer.TryResolveImagePath(markdownPath, unsafeSource, out _), unsafeSource);

        var document = Render("![photo](images/my%20photo.png)", documentPath: markdownPath);
        var image = Assert.Single(document.QuerySelectorAll("#document img"));
        Assert.Matches(@"^https://markpad\.local/[0-9a-f]{32}/image/0$", image.GetAttribute("src")!);
        Assert.Equal("photo", image.GetAttribute("alt"));
        Assert.Equal("lazy", image.GetAttribute("loading"));
    }

    [Fact]
    public void TablesStrikethroughFootnotesAndFencedCodeRenderWithoutExternalAssets()
    {
        var document = Render("""
            | Name | Value |
            | --- | --- |
            | Test | `42` |

            ~~removed~~ and a note[^1].

            [^1]: A footnote.

            ```csharp
            var text = "<script>";
            ```
            """);
        Assert.Equal("42", document.QuerySelector("#document table td code")!.TextContent);
        Assert.Equal("removed", document.QuerySelector("#document del")!.TextContent);
        Assert.NotEmpty(document.QuerySelectorAll("#document a[href^='#fn']"));
        Assert.Contains("<script>", document.QuerySelector("#document pre code.language-csharp")!.TextContent);
        Assert.Empty(document.QuerySelectorAll("#document script"));
    }

    [Fact]
    public void LinksAllowWebMailAndRelativeMarkdownButRejectExecutableAndNetworkPaths()
    {
        foreach (var allowed in new[] { "https://example.com/page", "http://example.com", "mailto:hello@example.com", "../README.md", "#heading" })
            Assert.True(MarkdownRenderer.IsSafeLink(allowed), allowed);
        foreach (var blocked in new[] { "javascript:alert(1)", "data:text/html,x", "file:///C:/private.md", "cmd:calc", "//server/path", @"\\server\share", "https://example.com/\nmalformed" })
            Assert.False(MarkdownRenderer.IsSafeLink(blocked), blocked);
    }

    [Theory]
    [InlineData("%5C%5Cserver%5Cshare%5Cdocument.md")]
    [InlineData("%2F%2Fserver/share/document.md")]
    [InlineData("file%3A///C:/private.md")]
    [InlineData("C%3A/private.md")]
    [InlineData("%5Crooted.md")]
    [InlineData("notes%00.md")]
    public void EncodedLocalLinksCannotBypassPathRestrictions(string href)
    {
        Assert.False(MarkdownRenderer.IsSafeLink(href));
        var document = Render($"<a href=\"{href}\">blocked</a>");
        Assert.Null(document.QuerySelector("#document a")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData("../notes%20and%20ideas.md")]
    [InlineData("%E7%AD%86%E8%A8%98.md#%E7%AB%A0%E7%AF%80")]
    [InlineData("images/../notes.md")]
    [InlineData("https://example.com/%2F%2Fpage")]
    [InlineData("mailto:hello@example.com?subject=notes%20today")]
    [InlineData("#%E7%AB%A0%E7%AF%80")]
    public void EncodedOrdinaryLinksRemainAllowed(string href)
    {
        Assert.True(MarkdownRenderer.IsSafeLink(href));
        var document = Render($"<a href=\"{href}\">allowed</a>");
        Assert.Equal(href, document.QuerySelector("#document a")!.GetAttribute("href"));
    }

    [Fact]
    public void DocumentTextAndPreferenceValuesCannotEscapeTrustedHtmlShell()
    {
        var document = Render("</script><script>alert('escape')</script>",
            new PreviewOptions(FontFamily: "';}</style><script>alert(1)</script>", FontSize: double.NaN, Language: "en\"><script>alert(1)</script>"));
        Assert.Single(document.QuerySelectorAll("script"));
        Assert.Single(document.QuerySelectorAll("style"));
        Assert.Empty(document.QuerySelectorAll("#document script"));
        Assert.Contains("--reading-size: 16px", document.QuerySelector("style")!.TextContent);
    }

    [Fact]
    public void DocumentTitleUsesExportNameAndEscapesHtml()
    {
        var html = new MarkdownRenderer().Render("# Heading", new PreviewOptions(DocumentTitle: "Report & Notes.pdf"));
        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Report & Notes.pdf", document.Title);
        Assert.Contains("<title>Report &amp; Notes.pdf</title>", html);
        Assert.Equal("汗青", Render("# Heading").Title);
    }

    [Theory]
    [InlineData("zh-CN", "ltr")]
    [InlineData("es", "ltr")]
    [InlineData("ar", "rtl")]
    [InlineData("fr", "ltr")]
    [InlineData("ko", "ltr")]
    public void NewPreviewLanguagesTranslateControlsWithoutForcingDocumentDirection(string language, string uiDirection)
    {
        var document = Render("# English document\n\n- [ ] task\n\n![offline](https://example.invalid/image.png)",
            new PreviewOptions(Language: language));
        Assert.Equal(language, document.DocumentElement.GetAttribute("lang"));
        Assert.Equal(uiDirection, document.DocumentElement.GetAttribute("dir"));
        Assert.Equal("auto", document.GetElementById("document")!.GetAttribute("dir"));
        Assert.Equal("English document", document.QuerySelector("#document h1")!.TextContent);
        Assert.NotEqual("Toggle task", document.QuerySelector("#document input")!.GetAttribute("aria-label"));
        Assert.NotEqual("Image unavailable offline", document.QuerySelector("#document .image-unavailable")!.GetAttribute("title"));
        using var config = PreviewConfig(document);
        Assert.Equal(uiDirection, config.RootElement.GetProperty("uiDirection").GetString());
        Assert.False(config.RootElement.GetProperty("readOnly").GetBoolean());
        var englishLabels = new Dictionary<string, string>
        {
            ["copy"] = "Copy", ["copied"] = "Copied", ["copyMarkdown"] = "Copy as Markdown", ["selectAll"] = "Select All",
            ["open"] = "Open Link", ["copyLink"] = "Copy Link", ["edit"] = "Edit Here", ["fold"] = "Fold section", ["close"] = "Close image"
        };
        foreach (var pair in englishLabels)
        {
            var label = config.RootElement.GetProperty("labels").GetProperty(pair.Key).GetString();
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(pair.Value, label);
        }
    }

    [Theory]
    [InlineData(true, "rtl")]
    [InlineData(false, "ltr")]
    public void ReadOnlyArticleHonorsExplicitDirectionAndPreservesSourceMetadata(bool rightToLeft, string direction)
    {
        const string markdown = "# مقدمة\n\n- [x] مهمة\n\n```csharp\nvar value = 42;\n```";
        var document = Render(markdown, new PreviewOptions(Language: "ar", ReadOnly: true, RightToLeft: rightToLeft));
        Assert.Equal(direction, document.GetElementById("document")!.GetAttribute("dir"));
        var task = Assert.Single(document.QuerySelectorAll("#document input"));
        Assert.True(task.HasAttribute("disabled"));
        Assert.Equal("3", task.GetAttribute("data-task-line"));
        Assert.Equal("1", document.QuerySelector("#document h1")!.GetAttribute("data-source-line"));
        Assert.Equal("var value = 42;\n", document.QuerySelector("#document pre code")!.TextContent);
        using var config = PreviewConfig(document);
        Assert.Equal(markdown, config.RootElement.GetProperty("markdown").GetString());
        Assert.True(config.RootElement.GetProperty("readOnly").GetBoolean());
        Assert.Contains("direction:ltr;unicode-bidi:isolate", document.QuerySelector("style")!.TextContent);
    }

    [Fact]
    public void OutlineUsesRenderedAtxAndSetextHeadingsWithPlainInlineLabelsAndExactLines()
    {
        var (document, headings) = RenderWithHeadings("# *First* &amp; `code`\n\nSecond [link](#first-code)\n---\n\n### Third\n#### Fourth\n##### Fifth\n###### Sixth\n");
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, headings.Select(heading => heading.Level));
        Assert.Equal(new[] { 1, 3, 6, 7, 8, 9 }, headings.Select(heading => heading.Line));
        Assert.Equal(new[] { "First & code", "Second link", "Third", "Fourth", "Fifth", "Sixth" }, headings.Select(heading => heading.Title));
        Assert.All(headings, heading =>
        {
            var element = document.GetElementById(heading.Id);
            Assert.NotNull(element);
            Assert.Equal("h" + heading.Level, element.LocalName);
            Assert.Equal(heading.Line.ToString(), element.GetAttribute("data-source-line"));
        });
    }

    [Fact]
    public void OutlineIdsStayUniqueAndStableAcrossRendersIncludingChineseAndRepeatedHeadings()
    {
        const string markdown = "# 重複 標題\n\n## 重複 標題\n\n# Duplicate\n\n# Duplicate\n\n<h3>自訂 <strong>標題</strong></h3>";
        var (_, first) = RenderWithHeadings(markdown);
        var (_, second) = RenderWithHeadings(markdown, new PreviewOptions(Dark: true, Ink: true, ReadOnly: true));
        Assert.Equal(5, first.Count);
        Assert.All(first, heading => Assert.False(string.IsNullOrWhiteSpace(heading.Id)));
        Assert.Equal(first.Count, first.Select(heading => heading.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal("自訂 標題", first[^1].Title);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void MultilineSetextOutlinePointsAtItsFirstTextLine(string newline)
    {
        var markdown = string.Join(newline, "Intro", "", "First title line", "second title line", "===", "", "After");
        var (_, headings) = RenderWithHeadings(markdown);
        var heading = Assert.Single(headings);
        Assert.Equal("First title line second title line", heading.Title);
        Assert.Equal(1, heading.Level);
        Assert.Equal(3, heading.Line);
    }

    [Fact]
    public void OutlineCannotPointAtShellOrNonHeadingWithDuplicateAuthoredIds()
    {
        var (document, headings) = RenderWithHeadings("""
            <div id="document"></div>
            <div id="duplicate"></div>
            <h1 id="document">Shell collision</h1>
            <h2 id="duplicate">First duplicate</h2>
            <h2 id="duplicate">Second duplicate</h2>
            <h3 id="percent%20中文">Encoded-looking ID</h3>
            """);
        Assert.Equal("main", document.GetElementById("document")!.LocalName);
        var ids = document.QuerySelectorAll("[id]").Select(element => element.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(4, headings.Count);
        Assert.Equal("percent%20中文", headings[^1].Id);
        Assert.All(headings, heading => Assert.Equal("h" + heading.Level, document.GetElementById(heading.Id)!.LocalName));
    }

    [Fact]
    public void OutlineIgnoresFencedEscapedAndUnsafeHeadingsAndKeepsImageAltAsPlainText()
    {
        var (document, headings) = RenderWithHeadings("""
            ```markdown
            # Fenced example
            <h1>Fenced HTML</h1>
            ```

            \# Escaped example

            <script><h1>Unsafe heading</h1></script>

            ## Real **heading** ![picture](https://example.invalid/picture.png) &lt;tag&gt;
            """);
        var heading = Assert.Single(headings);
        Assert.Equal("Real heading picture <tag>", heading.Title);
        Assert.Equal(2, heading.Level);
        Assert.Equal(10, heading.Line);
        Assert.Empty(document.QuerySelectorAll("#document script"));
    }

    private static (IDocument Document, IReadOnlyList<PreviewHeading> Headings) RenderWithHeadings(string markdown, PreviewOptions? options = null)
    {
        // The public pane exposes metadata after navigation; inspect its internal render
        // snapshot here without requiring WebView2 or widening the renderer's public API.
        var build = typeof(MarkdownRenderer).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var rendered = build.Invoke(new MarkdownRenderer(), [markdown, options ?? new PreviewOptions(), null])!;
        var html = (string)rendered.GetType().GetProperty("Html")!.GetValue(rendered)!;
        var headings = Assert.IsAssignableFrom<IReadOnlyList<PreviewHeading>>(rendered.GetType().GetProperty("Headings")!.GetValue(rendered));
        return (new HtmlParser().ParseDocument(html), headings);
    }

    private static IDocument Render(string markdown, PreviewOptions? options = null, string? documentPath = null) =>
        new HtmlParser().ParseDocument(new MarkdownRenderer().Render(markdown, options ?? new PreviewOptions(), documentPath));

    private static JsonDocument PreviewConfig(IDocument document)
    {
        var script = Assert.Single(document.QuerySelectorAll("script")).TextContent;
        const string prefix = "window.markpadConfig=";
        var end = script.IndexOf(";(() =>", StringComparison.Ordinal);
        Assert.True(end > prefix.Length);
        return JsonDocument.Parse(script[prefix.Length..end]);
    }
}
