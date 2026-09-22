using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MarkPad.Rendering;

public sealed record PreviewOptions(bool Dark = false, string FontFamily = "Segoe UI", double FontSize = 16,
    bool CodeLineNumbers = false, bool EmojiShortcodes = false, string Language = "en", bool ReadOnly = false);

internal sealed record RenderedPreview(string Html, string Token, IReadOnlyDictionary<string, string> Images);

/// <summary>Produces an offline document. Only the trusted, bundled preview script may execute.</summary>
public sealed class MarkdownRenderer
{
    internal const string Origin = "https://markpad.local";
    private static readonly Lazy<string> Styles = new(() => ReadResource("Preview.css"));
    private static readonly Lazy<string> Script = new(() => ReadResource("Preview.js"));
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".avif", ".svg" };

    public string Render(string markdown, PreviewOptions options, string? documentPath = null) =>
        Build(markdown, options, documentPath).Html;

    internal RenderedPreview Build(string markdown, PreviewOptions options, string? documentPath)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var builder = new MarkdownPipelineBuilder().UsePreciseSourceLocation().UsePipeTables().UseTaskLists().UseAutoLinks()
            .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
            .UseFootnotes().UseAutoIdentifiers(AutoIdentifierOptions.GitHub);
        if (options.EmojiShortcodes) builder.UseEmojiAndSmiley();
        var pipeline = builder.Build();
        var document = Markdown.Parse(markdown, pipeline);
        foreach (var block in document.Descendants<Block>())
        {
            block.GetAttributes().AddProperty("data-source-line", (block.Line + 1).ToString(CultureInfo.InvariantCulture));
            // Span offsets let Copy as Markdown preserve the actual source instead of round-tripping HTML.
            block.GetAttributes().AddProperty("data-source-start", block.Span.Start.ToString(CultureInfo.InvariantCulture));
            block.GetAttributes().AddProperty("data-source-end", block.Span.End.ToString(CultureInfo.InvariantCulture));
        }
        var tasks = new Dictionary<string, int>();
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.ObjectRenderers.Insert(0, new TaskRenderer(token, tasks));
        renderer.Render(document);

        var sanitizer = CreateSanitizer();
        var body = new HtmlParser().ParseDocument(sanitizer.Sanitize(writer.ToString())).Body!;
        foreach (var input in body.QuerySelectorAll("span[data-task-token]").ToArray())
        {
            var key = input.GetAttribute("data-task-token")!;
            if (!tasks.TryGetValue(key, out var line)) { input.Remove(); continue; }
            var checkbox = body.Owner!.CreateElement("input");
            checkbox.SetAttribute("type", "checkbox");
            if (options.ReadOnly) checkbox.SetAttribute("disabled", "disabled");
            checkbox.SetAttribute("data-task-line", line.ToString(CultureInfo.InvariantCulture));
            checkbox.SetAttribute("aria-label", Translate(options.Language, "Toggle task", "勾選待辦事項", "タスクを切り替え"));
            if (input.TextContent == "x") checkbox.SetAttribute("checked", "checked");
            input.Replace(checkbox);
        }
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var image in body.QuerySelectorAll("img").ToArray())
        {
            var source = image.GetAttribute("src") ?? "";
            if (TryResolveImagePath(documentPath, source, out var path))
            {
                var key = $"{Origin}/{token}/image/{images.Count}";
                images[key] = path;
                image.SetAttribute("data-original-source", source);
                image.SetAttribute("src", key);
                image.SetAttribute("loading", "lazy");
                image.SetAttribute("decoding", "async");
            }
            else
            {
                // Never contact remote image servers, including tracking pixels, when opening a document.
                var placeholder = body.Owner!.CreateElement("span");
                placeholder.ClassName = "image-unavailable";
                placeholder.TextContent = image.GetAttribute("alt") is { Length: > 0 } alt ? $"▧ {alt}" : "▧";
                placeholder.SetAttribute("title", Translate(options.Language, "Image unavailable offline", "離線無法顯示此圖片", "オフラインでは画像を表示できません"));
                image.Replace(placeholder);
            }
        }
        foreach (var link in body.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href")!;
            if (!IsSafeLink(href)) link.RemoveAttribute("href");
            link.RemoveAttribute("target");
        }

        var font = new string(options.FontFamily.Where(c => char.IsLetterOrDigit(c) || " -_,".Contains(c)).Take(120).ToArray());
        if (string.IsNullOrWhiteSpace(font)) font = "Segoe UI";
        var size = double.IsFinite(options.FontSize) ? Math.Clamp(options.FontSize, 8, 72) : 16;
        var config = JsonSerializer.Serialize(new { token, markdown, language = options.Language, codeLineNumbers = options.CodeLineNumbers, readOnly = options.ReadOnly });
        var html = $$"""
            <!doctype html><html lang="{{WebUtility.HtmlEncode(options.Language)}}" class="{{(options.Dark ? "dark" : "light")}}">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'nonce-{{nonce}}'; style-src 'nonce-{{nonce}}'; img-src {{Origin}}; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'">
            <title>MarkPad</title><style nonce="{{nonce}}">{{Styles.Value}}
            :root { --reading-font: '{{font}}', 'Segoe UI', sans-serif; --reading-size: {{size.ToString(CultureInfo.InvariantCulture)}}px; }
            </style></head><body><main id="document" aria-label="Markdown">{{body.InnerHtml}}</main>
            <script nonce="{{nonce}}">window.markpadConfig={{config}};{{Script.Value}}</script></body></html>
            """;
        return new RenderedPreview(html, token, images);
    }

    public static bool IsSafeLink(string href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.Any(char.IsControl)) return false;
        if (href.StartsWith('#')) return true;
        if (href.StartsWith("//", StringComparison.Ordinal) || href.StartsWith('\\')) return false;
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" or "mailto";
        return !href.Contains(':');
    }

    /// <summary>Restricts preview images to the document directory tree; network paths and reparse points are rejected.</summary>
    public static bool TryResolveImagePath(string? documentPath, string source, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(documentPath) || string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            source = Uri.UnescapeDataString(source.Split('?', '#')[0]);
            if (Path.IsPathRooted(source) || source.Contains(':') || source.Any(char.IsControl)) return false;
            var root = Path.GetFullPath(Path.GetDirectoryName(documentPath)!);
            if (root.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            var candidate = Path.GetFullPath(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !ImageExtensions.Contains(Path.GetExtension(candidate))) return false;
            // Reject links at every level so an images junction cannot expose another folder.
            for (var current = candidate; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)!)
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            var info = new FileInfo(candidate);
            if (!info.Exists || info.Length > 20 * 1024 * 1024) return false;
            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return false; }
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith("a abbr b blockquote br caption code col colgroup dd del details div dl dt em figcaption figure h1 h2 h3 h4 h5 h6 hr i img kbd li mark ol p pre s samp small span strong sub summary sup table tbody td th thead tr ul var".Split(' '));
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith("href src alt title id class width height colspan rowspan align start reversed open data-source-line data-source-start data-source-end data-task-token".Split(' '));
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(new[] { "http", "https", "mailto" });
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();
        sanitizer.AllowDataAttributes = false;
        sanitizer.KeepChildNodes = false;
        return sanitizer;
    }

    internal static string Translate(string language, string english, string chinese, string japanese) =>
        language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? chinese :
        language.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? japanese : english;

    private static string ReadResource(string name)
    {
        using var stream = typeof(MarkdownRenderer).Assembly.GetManifestResourceStream("MarkPad.Resources." + name)
            ?? throw new InvalidOperationException($"Missing bundled preview resource: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class TaskRenderer(string token, Dictionary<string, int> tasks) : HtmlObjectRenderer<TaskList>
    {
        protected override void Write(HtmlRenderer renderer, TaskList task)
        {
            var key = token + "-" + tasks.Count;
            var line = task.Line + 1;
            // Inline positions are tracked by Markdig; fallback to the enclosing list item's source line.
            if (line <= 1)
                for (var parent = task.Parent; parent is not null; parent = parent.Parent)
                    if (parent.ParentBlock is { } block) { line = block.Line + 1; break; }
            tasks[key] = line;
            renderer.Write("<span data-task-token=\"").Write(key).Write("\">").Write(task.Checked ? "x" : " ").Write("</span>");
        }
    }
}
