using System.IO;
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

    private static IDocument Render(string markdown, PreviewOptions? options = null, string? documentPath = null) =>
        new HtmlParser().ParseDocument(new MarkdownRenderer().Render(markdown, options ?? new PreviewOptions(), documentPath));
}
