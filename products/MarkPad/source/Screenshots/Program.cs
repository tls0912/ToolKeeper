using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarkPad;
using MarkPad.Models;
using MarkPad.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string Root = FindRoot();
    static readonly string Package = Path.Combine(Root, "products", "MarkPad");
    static readonly List<string> Results = [];
    static readonly List<object> Captures = [];
    static readonly (string Locale, string Language, string Main, string[] Others)[] Languages =
    [
        ("zh-TW", "zh-TW", "竹間札記.md", ["閱讀清單.md", "週末計畫.md"]),
        ("en-US", "en", "Quiet pages.md", ["Reading list.md", "Weekend plans.md"]),
        ("ja-JP", "ja", "竹のそばのメモ.md", ["読書リスト.md", "週末の予定.md"]),
        ("zh-CN", "zh-CN", "竹间札记.md", ["阅读清单.md", "周末计划.md"]),
        ("es-ES", "es", "Páginas tranquilas.md", ["Lista de lectura.md", "Planes para el fin de semana.md"]),
        ("ar-SA", "ar", "صفحات هادئة.md", ["قائمة القراءة.md", "خطط عطلة نهاية الأسبوع.md"]),
        ("fr-FR", "fr", "Pages paisibles.md", ["Liste de lecture.md", "Projets du week-end.md"]),
        ("ko-KR", "ko", "대나무 사이의 기록.md", ["독서 목록.md", "주말 계획.md"])
    ];
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static T Member<T>(object target, string name) => (T)(target.GetType().GetField(name, Flags)?.GetValue(target)
        ?? target.GetType().GetProperty(name, Flags)!.GetValue(target))!;
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    static WebView2CompositionControl Browser(DocumentView view) => Member<WebView2CompositionControl>(view.Preview, "_browser");
    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ToolKeeper.sln"))) return dir.FullName;
        throw new InvalidOperationException("Run this capture project inside the ToolKeeper repository.");
    }
    static void Check(bool pass, string text)
    {
        Results.Add((pass ? "PASS " : "FAIL ") + text);
        if (!pass) throw new InvalidOperationException(text);
    }
    [STAThread] static void Main()
    {
        var state = Path.Combine(Root, "artifacts", "markpad-store-capture", "state", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        var preferences = new SettingsService(state);
        var settings = preferences.Settings;
        settings.AutoSave = false; settings.ToolbarPinned = true; settings.RememberWindowSize = true;
        settings.WindowWidth = 1600; settings.WindowHeight = 1000; settings.WindowMaximized = false;
        settings.UiFontSize = 16; settings.EditorFontSize = 17; settings.PreviewFontSize = 18;
        settings.UiFontFamily = settings.PreviewFontFamily = ""; settings.EditorFontFamily = "Cascadia Mono";
        settings.RecentFiles.Clear();
        settings.RememberOpenFiles = false; settings.SessionFiles.Clear(); settings.SessionActiveFile = null;
        var session = (DocumentSessionService)typeof(MarkPad.App).GetProperty("Session", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        session.Reset();
        Check(preferences.DataDirectory == state, "settings use an isolated capture directory");
        typeof(MarkPad.App).GetField("<Preferences>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, preferences);
        typeof(MarkPad.App).GetField("<Recovery>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            new RecoveryService(Path.Combine(state, "recovery")));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            MainWindow? window = null;
            try
            {
                foreach (var (locale, language, mainName, otherNames) in Languages)
                {
                    settings.Language = language; settings.Theme = "Ink";
                    window = new MainWindow(deferEmptyState: true)
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual, WindowState = WindowState.Normal,
                        Left = -32000, Top = -32000, Width = 1600, Height = 1000, ShowActivated = false, ShowInTaskbar = false
                    };
                    window.Show();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Member<DispatcherTimer>(window, "_maintenance").Stop();
                    var source = Path.Combine(Package, "samples", locale);
                    var sampleNames = otherNames.Append(mainName).ToArray();
                    var hashes = sampleNames.ToDictionary(name => name, name => Hash(Path.Combine(source, name)));
                    var samples = Path.Combine(state, "samples", locale); Directory.CreateDirectory(samples);
                    foreach (var name in sampleNames) File.Copy(Path.Combine(source, name), Path.Combine(samples, name));
                    foreach (var name in otherNames) window.Documents.Add(await MarkPad.App.Files.OpenAsync(Path.Combine(samples, name)));
                    var mainPath = Path.Combine(samples, mainName);
                    var sample = File.ReadAllText(mainPath);
                    var document = await MarkPad.App.Files.OpenAsync(mainPath);
                    window.AddDocument(document);
                    await Render(window);
                    var view = Member<DocumentView>(window, "_currentView");
                    Check(Browser(view).CoreWebView2 is not null, locale + " real WebView2 initialized");
                    Check(Browser(view).CoreWebView2.Environment.UserDataFolder.StartsWith(state + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), locale + " isolated browser profile");
                    Check(Member<string>(view, "_language") == language && settings.ResolveLanguage("en-US") == language, locale + " actual interface language");
                    Check(window.Documents.Select(item => item.DisplayName).SequenceEqual(sampleNames), locale + " three localized document tabs");
                    Check(window.Documents.All(item => item.FilePath!.StartsWith(samples + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), locale + " only isolated sample copies opened");
                    Check(view.Preview.Headings.Count >= 7, locale + " document has an actual chapter list");
                    foreach (var (theme, previewTheme, name) in new[]
                    {
                        ("Ink", "ink", "01-preview-bamboo-light"),
                        ("InkDark", "ink-dark", "02-preview-bamboo-dark"),
                        ("Light", "light", "03-preview-light"),
                        ("Dark", "dark", "04-preview-dark")
                    })
                    {
                        settings.Theme = theme; window.ApplyPreferences();
                        await Render(window); await Settle(window, view, previewTheme);
                        Check(document.IsPreviewMode && Member<Grid>(view, "_outlinePanel").IsVisible,
                            locale + " " + theme + " preview has chapter navigation");
                        await Capture(window, view, locale, name);
                    }

                    settings.Theme = "Ink"; window.ApplyPreferences();
                    await ((Task)Call(window, "ToggleModeAsync", new object?[] { null })!).WaitAsync(TimeSpan.FromSeconds(30));
                    await Render(window); await Settle(window, view, "ink");
                    Check(!document.IsPreviewMode && Member<Border>(window, "EditorToolbarHost").IsVisible,
                        locale + " actual editor toolbar visible in split mode");
                    Check(!Member<Grid>(view, "_outlinePanel").IsVisible, locale + " editor has no chapter sidebar");
                    Check(view.Editor.TransformToAncestor(window).Transform(new Point()).X < view.Preview.TransformToAncestor(window).Transform(new Point()).X,
                        locale + " split view places editor left and preview right");
                    await Capture(window, view, locale, "05-edit-bamboo-light");
                    Check(File.ReadAllText(mainPath) == sample && !document.IsDirty, locale + " original demo document was not edited");
                    Check(sampleNames.All(name => Hash(Path.Combine(source, name)) == hashes[name]), locale + " source samples were not rewritten");
                    Close(window); window = null;
                    Check(!settings.RememberOpenFiles && settings.SessionFiles.Count == 0 && settings.SessionActiveFile is null, locale + " session state remains isolated and empty");
                    Console.WriteLine("Captured " + locale + " (5 screenshots)");
                }
                Check(Captures.Count == 40, "all eight languages have five screenshots");
                Results.Add("40 PNGs, captured from real MainWindow/AvalonEdit/WebView2 using isolated copies of repository sample documents; four preview themes and Bamboo Light editing per language.");
                Results.Add("No Store authentication or purchase performed. No user settings or documents were used.");
            }
            catch (Exception ex) { Results.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally
            {
                if (window is not null) Close(window);
                var verification = Path.Combine(Package, "verification"); Directory.CreateDirectory(verification);
                File.WriteAllLines(Path.Combine(verification, "screenshots.txt"), Results);
                File.WriteAllText(Path.Combine(verification, "screenshots-1.0.3.0.json"), JsonSerializer.Serialize(new
                {
                    storeVersion = "1.0.3.0", generatedAtUtc = DateTime.UtcNow,
                    status = Environment.ExitCode == 0 ? "passed" : "failed", count = Captures.Count,
                    captureStateDirectory = Path.GetRelativePath(Root, state).Replace('\\', '/'),
                    method = "Real WPF MainWindow, AvalonEdit and WebView2CompositionControl rendered at 96 DPI; no synthetic UI.",
                    checks = Results, captures = Captures
                }, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var line in Results) Console.WriteLine(line);
                app.Shutdown();
            }
        };
        app.Run();
    }
    static void Close(MainWindow window) { window.GetType().GetField("_allowClose", Flags)!.SetValue(window, true); window.Close(); }
    static async Task Render(MainWindow window)
    {
        await ((Task)Call(window, "RenderAsync", new object?[] { null })!).WaitAsync(TimeSpan.FromSeconds(35));
        await Task.Delay(300); window.UpdateLayout();
    }
    static async Task Settle(MainWindow window, DocumentView view, string expectedTheme)
    {
        view.Editor.GoToLine(1); view.Editor.Editor.ScrollToVerticalOffset(0);
        await Browser(view).ExecuteScriptAsync("window.scrollTo({top:0,behavior:'instant'})");
        await Task.Delay(450);
        var theme = JsonSerializer.Deserialize<string>(await Browser(view).ExecuteScriptAsync("document.documentElement.dataset.theme"));
        Check(theme == expectedTheme, "live preview theme " + expectedTheme);
        var preview = await InspectPreview(view);
        var language = Member<string>(view, "_language");
        var title = view.Document.Content.Split('\n')[0].Trim().TrimStart('#').Trim();
        Check(preview.GetProperty("language").GetString() == language, "live preview language " + language);
        Check(preview.GetProperty("title").GetString() == title, "live preview localized title " + title);
        Check(preview.GetProperty("direction").GetString() == (language == "ar" ? "rtl" : "ltr"), "live preview paragraph direction " + language);
        Check(preview.GetProperty("textLength").GetInt32() > 400 && preview.GetProperty("headings").GetInt32() >= 7, "live preview contains sample text and headings");
        Check(view.Preview.IsShowing(view.Document.Content, view.Document.FilePath), "live preview represents the current document");
        // Flush a real browser frame before WPF reads the composition surface.
        using var frame = new MemoryStream();
        await Browser(view).CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, frame);
        Check(frame.Length > 1000, "real preview frame contains rendered content");
        frame.Position = 0;
        var decoded = BitmapFrame.Create(frame, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Check(PixelVariation(decoded, new Int32Rect(24, 24, decoded.PixelWidth - 48, decoded.PixelHeight - 48)).DistinctColors > 24,
            "real preview frame has visible text rather than a blank background");
        await Task.Delay(500); window.UpdateLayout();
    }
    static async Task Capture(MainWindow window, DocumentView view, string locale, string name)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var width = (int)Math.Round(window.ActualWidth); var height = (int)Math.Round(window.ActualHeight);
        Check(width == 1600 && height == 1000, locale + "/" + name + " is 1600x1000 at 96 DPI");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var origin = view.Preview.TransformToAncestor(window).Transform(new Point(24, 24));
        var area = new Int32Rect((int)origin.X, (int)origin.Y, (int)view.Preview.ActualWidth - 48, (int)view.Preview.ActualHeight - 48);
        var variation = PixelVariation(bitmap, area);
        Check(variation.DistinctColors > 24 && variation.LuminanceRange > 50,
            locale + "/" + name + " composed preview has visible text and contrast");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = Path.Combine(Package, "screenshots", locale); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".png");
        using (var file = File.Create(path)) encoder.Save(file);
        Check(new FileInfo(path).Length > 25000 && new FileInfo(path).Length < 50 * 1024 * 1024, locale + "/" + name + " PNG below 50 MB");
        Captures.Add(new { path = "screenshots/" + locale + "/" + name + ".png", locale, width, height,
            bytes = new FileInfo(path).Length, sha256 = Hash(path), preview = await InspectPreview(view),
            composedPreviewDistinctColors = variation.DistinctColors, composedPreviewLuminanceRange = variation.LuminanceRange });
        Results.Add("PNG screenshots/" + locale + "/" + name + ".png " + width + "x" + height + " " + new FileInfo(path).Length + " bytes");
    }
    static async Task<JsonElement> InspectPreview(DocumentView view) =>
        JsonSerializer.Deserialize<JsonElement>(await Browser(view).ExecuteScriptAsync("(() => { const main=document.querySelector('main'); const heading=main?.querySelector('h1')?.cloneNode(true); heading?.querySelectorAll('button,.heading-anchor').forEach(node=>node.remove()); return {theme:document.documentElement.dataset.theme,language:document.documentElement.lang,title:heading?.textContent?.trim(),direction:main?getComputedStyle(main).direction:null,textLength:main?.innerText.length,headings:main?.querySelectorAll('h1,h2,h3,h4,h5,h6').length,fontFamily:main?getComputedStyle(main).fontFamily:null}; })()"));
    static (int DistinctColors, int LuminanceRange) PixelVariation(BitmapSource bitmap, Int32Rect area)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = area.Width * 4; var pixels = new byte[stride * area.Height];
        converted.CopyPixels(area, pixels, stride, 0);
        var colors = new HashSet<int>(); var min = 255; var max = 0;
        for (var y = 0; y < area.Height; y += 2)
            for (var x = 0; x < area.Width; x += 2)
            {
                var index = y * stride + x * 4;
                var b = pixels[index]; var g = pixels[index + 1]; var r = pixels[index + 2];
                colors.Add((r >> 3) << 10 | (g >> 3) << 5 | b >> 3);
                var luminance = (r * 2126 + g * 7152 + b * 722) / 10000;
                min = Math.Min(min, luminance); max = Math.Max(max, luminance);
            }
        return (colors.Count, max - min);
    }
}
