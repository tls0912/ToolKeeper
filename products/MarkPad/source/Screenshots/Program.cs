using System.IO;
using System.Reflection;
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
        typeof(MarkPad.App).GetField("<Preferences>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, preferences);
        typeof(MarkPad.App).GetField("<Recovery>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null,
            new RecoveryService(Path.Combine(state, "recovery")));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            MainWindow? window = null;
            try
            {
                foreach (var (locale, language) in new[] { ("zh-TW", "zh-TW"), ("en-US", "en"), ("ja-JP", "ja") })
                {
                    settings.Language = language; settings.Theme = "Ink";
                    window = new MainWindow
                    {
                        WindowStartupLocation = WindowStartupLocation.Manual, WindowState = WindowState.Normal,
                        Left = -32000, Top = -32000, Width = 1600, Height = 1000, ShowActivated = false, ShowInTaskbar = false
                    };
                    window.Show();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Member<DispatcherTimer>(window, "_maintenance").Stop();
                    var samples = Path.Combine(Package, "samples", locale); Directory.CreateDirectory(samples);
                    var mainName = language switch { "zh-TW" => "竹間札記.md", "ja" => "竹のそばのメモ.md", _ => "Quiet pages.md" };
                    var otherNames = language switch
                    {
                        "zh-TW" => new[] { "閱讀清單.md", "週末計畫.md" },
                        "ja" => new[] { "読書リスト.md", "週末の予定.md" },
                        _ => new[] { "Reading list.md", "Weekend plans.md" }
                    };
                    foreach (var name in otherNames)
                    {
                        var path = Path.Combine(samples, name);
                        File.WriteAllText(path, "# " + Path.GetFileNameWithoutExtension(name) + "\n\n- [ ] " + (language == "zh-TW" ? "留下下一個想法" : language == "ja" ? "次のアイデアを書く" : "Write the next idea") + "\n");
                        window.Documents.Add(await MarkPad.App.Files.OpenAsync(path));
                    }
                    var mainPath = Path.Combine(samples, mainName); File.WriteAllText(mainPath, Sample(language));
                    var document = await MarkPad.App.Files.OpenAsync(mainPath);
                    window.AddDocument(document);
                    await Render(window);
                    var view = Member<DocumentView>(window, "_currentView");
                    Check(Browser(view).CoreWebView2 is not null, locale + " real WebView2 initialized");
                    Check(Browser(view).CoreWebView2.Environment.UserDataFolder.StartsWith(state, StringComparison.OrdinalIgnoreCase), locale + " isolated browser profile");
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
                        await Capture(window, locale, name);
                    }

                    settings.Theme = "Ink"; window.ApplyPreferences();
                    await ((Task)Call(window, "ToggleModeAsync", new object?[] { null })!).WaitAsync(TimeSpan.FromSeconds(30));
                    await Render(window); await Settle(window, view, "ink");
                    Check(!document.IsPreviewMode && Member<Border>(window, "EditorToolbarHost").IsVisible,
                        locale + " actual editor toolbar visible in split mode");
                    Check(!Member<Grid>(view, "_outlinePanel").IsVisible, locale + " editor has no chapter sidebar");
                    await Capture(window, locale, "05-edit-bamboo-light");
                    Check(File.ReadAllText(mainPath) == Sample(language) && !document.IsDirty, locale + " original demo document was not edited");
                    Close(window); window = null;
                    Console.WriteLine("Captured " + locale + " (5 screenshots)");
                }
                Results.Add("15 PNGs, captured from real MainWindow/AvalonEdit/WebView2 using original sample documents; four preview themes and Bamboo Light editing per language.");
                Results.Add("No Store authentication or purchase performed. No user settings or documents were used.");
            }
            catch (Exception ex) { Results.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally
            {
                if (window is not null) Close(window);
                var verification = Path.Combine(Package, "verification"); Directory.CreateDirectory(verification);
                File.WriteAllLines(Path.Combine(verification, "screenshots.txt"), Results);
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
        // Flush a real browser frame before WPF reads the composition surface.
        using var frame = new MemoryStream();
        await Browser(view).CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, frame);
        Check(frame.Length > 1000, "real preview frame contains rendered content");
        await Task.Delay(500); window.UpdateLayout();
    }
    static async Task Capture(MainWindow window, string locale, string name)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var width = (int)Math.Round(window.ActualWidth); var height = (int)Math.Round(window.ActualHeight);
        Check(width >= 1366 && height >= 768, locale + "/" + name + " meets desktop minimum size");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = Path.Combine(Package, "screenshots", locale); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".png");
        using (var file = File.Create(path)) encoder.Save(file);
        Check(new FileInfo(path).Length < 50 * 1024 * 1024, locale + "/" + name + " PNG below 50 MB");
        Results.Add("PNG screenshots/" + locale + "/" + name + ".png " + width + "x" + height);
    }
    static string Sample(string language) => language switch
    {
        "zh-TW" => """
            # 竹間札記

            > 把日常的片刻，寫成值得保存的文字。

            清晨，窗邊的光落在桌面上。打開一份筆記，先寫下今天最想記得的一件事。
            不急著完成長篇文章，從一個標題、一段文字開始，慢慢整理自己的想法。

            ## 今天的三件小事

            - [x] 讀完一章喜歡的書
            - [x] 記下一段散步時想到的文字
            - [ ] 留一點時間，整理週末的計畫

            ## 讓想法有自己的位置

            用 **標題** 分開主題，以 *斜體* 留下語氣，用清單把想法排整齊。
            文字可以很簡單，只要下次回來時，還能找得到當初的心情。

            ### 一週的寫作節奏

            | 時間 | 小小的安排 | 留下的記錄 |
            | --- | --- | --- |
            | 週一 | 讀書十五分鐘 | 一句喜歡的話 |
            | 週三 | 沿著河岸散步 | 三個新觀察 |
            | 週末 | 回看這週筆記 | 下一篇的開頭 |

            ## 收集生活的片段

            在筆記裡保留一段簡短的程式碼，也能讓做過的事情更容易重現。

            ```json
            {
              "notebook": "竹間札記",
              "habit": "每天留下一段文字"
            }
            ```

            ### 下次續寫

            1. 選一個值得多看一眼的細節。
            2. 寫下它為什麼讓自己停留。
            3. 把草稿讀一遍，留住最清楚的文字。

            #### 從一句話開始

            不必一次寫完，明天再接著寫。

            ##### 留給自己的提醒

            好的筆記，是日後還願意再讀的文字。

            ###### 今日收筆

            把檔案存好，讓今天的想法有一個安穩的位置。
            """,
        "ja" => """
            # 竹のそばのメモ

            > 日々の小さな出来事を、残しておきたい言葉に。

            朝の光が机の上に差し込む。メモを開き、今日いちばん覚えておきたいことを書く。
            長い文章を急いで仕上げなくてもいい。見出しと数行から、少しずつ考えを整えていこう。

            ## 今日の三つのこと

            - [x] 好きな本を一章読む
            - [x] 散歩で浮かんだことをメモする
            - [ ] 週末の予定をゆっくり考える

            ## 考えを整理する

            **見出し** で話題を分け、*斜体* で気持ちを添え、リストで順序を整える。
            短いメモでも、読み返したときにその日の気持ちを思い出せれば十分だ。

            ### 一週間の書く習慣

            | いつ | 小さな予定 | 残すもの |
            | --- | --- | --- |
            | 月曜日 | 十五分だけ読書 | 好きな一文 |
            | 水曜日 | 川沿いを散歩 | 三つの発見 |
            | 週末 | メモを読み返す | 次の文章の書き出し |

            ## 暮らしの断片を集める

            短いコードもメモに残しておくと、試したことを後から再現しやすくなる。

            ```json
            {
              "notebook": "竹のそばのメモ",
              "habit": "毎日、数行を書き残す"
            }
            ```

            ### 次に書くこと

            1. もう少し見つめたい小さなことを選ぶ。
            2. なぜ心に残ったのかを書いてみる。
            3. 下書きを読み返し、伝わる言葉を残す。

            #### 一文から始めよう

            一度に書き終えなくても、続きは明日でいい。

            ##### 自分へのひとこと

            よいメモは、いつかまた読みたくなる言葉。

            ###### 今日の終わりに

            ファイルを保存し、今日の考えに居場所を作ろう。
            """,
        _ => """
            # Quiet pages

            > Turn small moments into words worth keeping.

            Morning light falls across the desk. Open a page and write down one thing you want to remember today.
            There is no need to finish a long essay. Start with a heading and a few lines, then give your ideas room to grow.

            ## Three small things for today

            - [x] Read a chapter of a favourite book
            - [x] Write notes from a walk by the river
            - [ ] Leave a little time to plan the weekend

            ## Give every idea a place

            Use **headings** to separate topics, *italics* to add a little emphasis, and lists to put thoughts in order.
            Simple notes are enough when they help you remember how a moment felt.

            ### A weekly writing rhythm

            | When | A small plan | What to keep |
            | --- | --- | --- |
            | Monday | Read for fifteen minutes | One favourite sentence |
            | Wednesday | Walk along the river | Three observations |
            | Weekend | Revisit this week's notes | The start of a new page |

            ## Collect everyday fragments

            Keep a short code snippet in your notes so that something you tried is easier to repeat later.

            ```json
            {
              "notebook": "Quiet pages",
              "habit": "Write a few lines every day"
            }
            ```

            ### Continue next time

            1. Pick a detail that deserves a second look.
            2. Write down why it made you pause.
            3. Read the draft and keep the clearest words.

            #### Begin with one sentence

            You can always return to the page tomorrow.

            ##### A note to yourself

            Good notes are words you want to read again.

            ###### Close the day

            Save the file and give today's ideas a place to stay.
            """
    };
}
