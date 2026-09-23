using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MarkPad.Rendering;

public sealed record PreviewMessage(string Type, string? Text = null, int Line = 0, int Count = 0, int Index = 0, bool Flag = false);

/// <summary>Owns one sandboxed offline preview. Messages always originate from the current rendered document.</summary>
public sealed partial class PreviewPane : UserControl, IDisposable
{
    private readonly Grid _layout = new();
    private readonly WebView2CompositionControl _browser = new();
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560, FontSize = 16 };
    private readonly MarkdownRenderer _renderer = new();
    private Task<bool>? _initializeTask;
    private RenderedPreview? _current;
    private TaskCompletionSource<bool>? _ready;
    private long _request;
    private ulong _navigationId;
    private bool _disposed;
    private bool _browserFailed;
    private string _language = "en";
    private string? _documentPath;
    private double _restoreScroll;
    private int? _restoreLine;
    private readonly string? _browserDataFolder;
    private (string Markdown, string? Path, PreviewOptions Options)? _buildInput;
    private Task<RenderedPreview>? _buildTask;
    private (string Markdown, string? Path, PreviewOptions Options)? _displayInput;
    private Task? _displayTask;
    private bool HasReadyDocument => !_disposed && !_browserFailed && _current is not null
        && _ready?.Task.IsCompletedSuccessfully == true && _ready.Task.Result;

    public event EventHandler<PreviewMessage>? MessageReceived;

    public PreviewPane(string? browserDataFolder = null)
    {
        _browserDataFolder = browserDataFolder;
        Content = _layout;
        _layout.Children.Add(_browser);
        _layout.Children.Add(_notice);
        ShowNotice("Preparing preview…");
    }

    public async Task ShowAsync(string markdown, string? filePath, PreviewOptions options, double scroll = 0, int? sourceLine = null)
    {
        if (_disposed) return;
        var input = (markdown, filePath, options);
        if (_displayInput != input || _displayTask is null || _displayTask.IsCompleted && !HasReadyDocument)
        {
            _displayInput = input;
            _displayTask = LoadAsync(markdown, filePath, options, scroll, sourceLine);
            await _displayTask;
            return;
        }

        // Selecting an unchanged tab reuses its page, including an in-flight first load.
        var version = _request;
        await _displayTask;
        if (sourceLine is not null && version == _request && HasReadyDocument)
            await ExecuteAsync($"window.markpad?.restore(0,{sourceLine.Value.ToString(CultureInfo.InvariantCulture)})");
    }

    private async Task LoadAsync(string markdown, string? filePath, PreviewOptions options, double scroll, int? sourceLine)
    {
        if (_disposed) return;
        var version = ++_request;
        _language = options.Language;
        ApplyThemeColors(options.Dark);
        try
        {
            // Parsing and sanitizing larger documents must not block typing or tab switching.
            var build = BuildAsync(markdown, filePath, options);
            var initialized = await (_initializeTask ??= InitializeAsync());
            var rendered = await build;
            if (_disposed || version != _request) return;
            if (!initialized) return;
            // Cached HTML is reusable, but each navigation needs a fresh message identity so two
            // identical untitled tabs cannot accept one another's queued messages.
            rendered = ForNavigation(rendered);
            _current = rendered;
            _documentPath = filePath;
            _restoreScroll = double.IsFinite(scroll) ? Math.Max(0, scroll) : 0;
            _restoreLine = sourceLine;
            _ready?.TrySetResult(false);
            var ready = _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _notice.Visibility = Visibility.Collapsed;
            _browser.Visibility = Visibility.Visible;
            ApplyThemeColors(_displayInput?.Options.Dark ?? options.Dark);
            // Ignore a superseded navigation's completion before the new NavigationStarting event.
            _navigationId = 0;
            _browser.CoreWebView2.Navigate(DocumentUrl(rendered));
            // Bound this wait: a failed navigation should not keep the caller suspended indefinitely.
            var completed = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            if (completed != ready.Task && version == _request && !_disposed)
                ShowNotice(MarkdownRenderer.Translate(_language, "Preview took too long to load. Switch to Edit and try again.", "預覽載入逾時，請切換至編輯模式後再試。", "プレビューの読み込みがタイムアウトしました。編集モードに切り替えて再試行してください。"));
        }
        catch (Exception ex)
        {
            if (_disposed || version != _request) return;
            ShowNotice(MarkdownRenderer.Translate(_language, "Preview could not be displayed. You can still use Edit mode.\n", "無法顯示預覽，您仍可使用編輯模式。\n", "プレビューを表示できません。編集モードは引き続き使えます。\n") + ex.Message);
            MessageReceived?.Invoke(this, new PreviewMessage("error", ex.Message));
        }
    }

    /// <summary>Synchronize retained pages and their native canvas without navigating again.</summary>
    public async Task SetThemeAsync(bool dark)
    {
        if (_disposed) return;
        ApplyThemeColors(dark);
        if (_displayInput is { } input)
            _displayInput = (input.Markdown, input.Path, input.Options with { Dark = dark });
        await ExecuteAsync($"window.markpad?.theme({JsonSerializer.Serialize(dark)})");
    }

    private void ApplyThemeColors(bool dark)
    {
        var color = dark ? Color.FromRgb(13, 17, 23) : Color.FromRgb(232, 235, 239);
        Background = _layout.Background = new SolidColorBrush(color);
        _notice.Foreground = new SolidColorBrush(dark ? Color.FromRgb(230, 237, 243) : Color.FromRgb(36, 41, 47));
        _browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
    }

    /// <summary>Update text size in place so wheel gestures preserve the page and its state.</summary>
    public async Task SetFontSizeAsync(double size)
    {
        var fontSize = Math.Clamp(double.IsFinite(size) ? size : 16, 8, 72);
        if (_displayInput is { } input)
            _displayInput = (input.Markdown, input.Path, input.Options with { FontSize = fontSize });
        var pixels = fontSize.ToString(CultureInfo.InvariantCulture);
        await ExecuteAsync($"document.documentElement.style.setProperty('--reading-size', '{pixels}px')");
    }

    public async Task<double> GetScrollAsync()
    {
        var value = await ExecuteAsync("window.markpad?.scroll() ?? 0");
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scroll) && double.IsFinite(scroll) ? scroll : 0;
    }

    public async Task<string> GetSelectionAsync()
    {
        var value = await ExecuteAsync("window.markpad?.selection() ?? ''");
        try { return JsonSerializer.Deserialize<string>(value) ?? ""; } catch (JsonException) { return ""; }
    }

    public async Task FindAsync(string text, bool matchCase, bool backwards = false, bool restart = false) =>
        await ExecuteAsync($"window.markpad?.find({JsonSerializer.Serialize(text)},{JsonSerializer.Serialize(matchCase)},{JsonSerializer.Serialize(backwards)},{JsonSerializer.Serialize(restart)})");

    public async Task<bool> CloseOverlayAsync() => await ExecuteAsync("window.markpad?.closeOverlay() ?? false") == "true";

    public async Task GoToAnchorAsync(string anchor) =>
        await ExecuteAsync($"window.markpad?.anchor({JsonSerializer.Serialize(anchor.StartsWith('#') ? anchor : "#" + anchor)})");

    /// <summary>Warm the HTML cache after the editor debounce without creating or navigating WebView2.</summary>
    public async Task PrepareAsync(string markdown, string? filePath, PreviewOptions options)
    {
        if (!_disposed) await BuildAsync(markdown, filePath, options);
    }

    private Task<RenderedPreview> BuildAsync(string markdown, string? filePath, PreviewOptions options)
    {
        var input = (markdown, filePath, options);
        if (_buildTask is null || _buildTask.IsFaulted || _buildInput != input)
        {
            _buildInput = input;
            _buildTask = Task.Run(() => _renderer.Build(markdown, options, filePath));
        }
        return _buildTask;
    }


    private async Task<bool> InitializeAsync()
    {
        try
        {
            var data = _browserDataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolKeeper", "MarkPad", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            if (_disposed) return false;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_disposed) return false;
            var core = _browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnResourceRequested;
            core.NavigationStarting += (_, args) =>
            {
                // Only our current synthetic document can become the top-level page.
                args.Cancel = _disposed || _current is null || !string.Equals(args.Uri, DocumentUrl(_current), StringComparison.Ordinal);
                if (!args.Cancel) _navigationId = args.NavigationId;
            };
            core.FrameNavigationStarting += (_, args) => args.Cancel = true;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnMessageReceived;
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && !_disposed && _current is not null && args.NavigationId == _navigationId)
                {
                    _ready?.TrySetResult(false);
                    ShowNotice(MarkdownRenderer.Translate(_language, "Preview could not be loaded. Switch to Edit and try again.", "預覽載入失敗，請切換至編輯模式後再試。", "プレビューを読み込めませんでした。編集モードに切り替えて再試行してください。"));
                }
            };
            core.ProcessFailed += (_, _) =>
            {
                _browserFailed = true;
                _ready?.TrySetResult(false);
                if (!_disposed) ShowNotice(MarkdownRenderer.Translate(_language, "The preview process stopped. Reopen this window to restore it; your document is still available in Edit mode.", "預覽程序已停止。請重新開啟視窗以恢復預覽；您的文件仍可在編輯模式中使用。", "プレビュープロセスが停止しました。ウィンドウを開き直してください。文書は編集モードで使用できます。"));
            };
            return true;
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                ShowNotice(MarkdownRenderer.Translate(_language,
                    "Preview needs the Microsoft Edge WebView2 Runtime. Install or repair it, then restart MarkPad. Edit mode remains available.\n",
                    "預覽需要 Microsoft Edge WebView2 Runtime。安裝或修復後請重新啟動 MarkPad；您仍可使用編輯模式。\n",
                    "プレビューには Microsoft Edge WebView2 Runtime が必要です。インストールまたは修復後、MarkPad を再起動してください。編集モードは使用できます。\n") + ex.Message);
                MessageReceived?.Invoke(this, new PreviewMessage("error", ex.Message));
            }
            return false;
        }
    }

    private async void OnResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_disposed) return;
        var current = _current;
        var environment = _browser.CoreWebView2.Environment;
        if (current is not null && args.Request.Method == "GET" && args.Request.Uri == DocumentUrl(current))
        {
            args.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(current.Html)), 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
            return;
        }
        if (current is not null && args.Request.Method == "GET" && current.Images.TryGetValue(args.Request.Uri, out var path))
        {
            try
            {
                using var deferral = args.GetDeferral();
                try
                {
                    // Re-check at read time in case a directory changed after the Markdown was rendered.
                    var relative = Path.GetRelativePath(Path.GetDirectoryName(_documentPath!)!, path);
                    if (!MarkdownRenderer.TryResolveImagePath(_documentPath, relative, out var verified)) throw new IOException("Image unavailable.");
                    var bytes = await File.ReadAllBytesAsync(verified);
                    if (_disposed || !ReferenceEquals(current, _current)) return;
                    if (bytes.Length > 20 * 1024 * 1024) throw new IOException("Image too large.");
                    args.Response = environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", "Content-Type: " + ImageMime(path) + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'none'; sandbox");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    if (!_disposed && ReferenceEquals(current, _current))
                        args.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "Content-Type: text/plain");
                }
            }
            catch (Exception ex) when (_disposed && ex is (InvalidOperationException or System.Runtime.InteropServices.COMException))
            { /* A tab switch can dispose the browser while an image deferral is pending. */ }
            return;
        }
        // Deny every other URL, including network requests initiated by embedded SVGs.
        args.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
    }

    private async void OnMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            if (_disposed || _current is null || args.Source != DocumentUrl(_current)) return;
            using var json = JsonDocument.Parse(args.WebMessageAsJson);
            var root = json.RootElement;
            if (root.GetProperty("token").GetString() != _current.Token) return;
            var type = root.GetProperty("type").GetString() ?? "";
            if (type is not ("ready" or "copy" or "copy-markdown" or "copy-link" or "link" or "edit" or "task" or "search" or "scroll" or "shortcut" or "overlay")) return;
            string? text = root.TryGetProperty("text", out var t) ? t.GetString() : null;
            int Number(string name) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? Math.Max(0, number) : 0;
            var message = new PreviewMessage(type, text, Number("line"), Number("count"), Number("index"), root.TryGetProperty("flag", out var flag) && flag.ValueKind == JsonValueKind.True);
            if (type == "ready")
            {
                var ready = _ready;
                var token = _current.Token;
                await ExecuteAsync($"window.markpad?.restore({_restoreScroll.ToString(CultureInfo.InvariantCulture)},{(_restoreLine?.ToString(CultureInfo.InvariantCulture) ?? "null")})");
                if (_disposed || token != _current?.Token) return;
                ready?.TrySetResult(true);
            }
            if (type == "link" && (text is null || !MarkdownRenderer.IsSafeLink(text))) return;
            MessageReceived?.Invoke(this, message);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { /* Ignore malformed or stale page messages. */ }
    }

    private async Task<string> ExecuteAsync(string script)
    {
        if (_disposed || _browserFailed || _current is null) return "null";
        try { return _browser.CoreWebView2 is null ? "null" : await _browser.ExecuteScriptAsync(script); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return "null"; }
    }

    private void ShowNotice(string text) { _notice.Text = text; _notice.Visibility = Visibility.Visible; _browser.Visibility = Visibility.Hidden; }
    private static string DocumentUrl(RenderedPreview preview) => $"{MarkdownRenderer.Origin}/{preview.Token}/document";
    private static RenderedPreview ForNavigation(RenderedPreview preview)
    {
        var token = Guid.NewGuid().ToString("N");
        var oldImages = $"{MarkdownRenderer.Origin}/{preview.Token}/image/";
        var newImages = $"{MarkdownRenderer.Origin}/{token}/image/";
        return new RenderedPreview(preview.Html
            .Replace($"\"token\":\"{preview.Token}\"", $"\"token\":\"{token}\"", StringComparison.Ordinal)
            .Replace(oldImages, newImages, StringComparison.Ordinal), token,
            preview.Images.ToDictionary(pair => pair.Key.Replace(oldImages, newImages, StringComparison.Ordinal), pair => pair.Value));
    }
    private static string ImageMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp", ".ico" => "image/x-icon", ".avif" => "image/avif", ".svg" => "image/svg+xml", _ => "application/octet-stream" };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_request;
        _current = null;
        _documentPath = null;
        _ready?.TrySetResult(false);
        _ready = null;
        MessageReceived = null;
        _buildTask = null;
        _buildInput = null;
        _displayTask = null;
        _displayInput = null;
        _browser.Dispose();
    }
}
