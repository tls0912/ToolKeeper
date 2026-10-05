using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TransLamp.Core;
using Xunit;

namespace TransLamp.Tests;

public sealed class LiveTranslationTests
{
    [Fact]
    public Task TypingAndPastingAutomaticallyTranslateOnlyTheLatestPausedInput() => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "first keystrokes";
            await Task.Delay(250);
            Assert.Empty(fixture.Requests);
            source.AppendText(" still typing");
            await Task.Delay(250);
            Assert.Empty(fixture.Requests);
            // Replacing the input exercises the same TextChanged route used by paste.
            source.Text = "Final pasted input D100 3000 ms.";
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0);
            var request = Assert.Single(fixture.Requests);
            Assert.Equal(source.Text, request.Text);
            Assert.Equal("en → zh: Final pasted input D100 3000 ms.", Get<TextBox>(window, "TargetText").Text);
            Assert.Contains("Translation complete", Get<TextBlock>(window, "StatusText").Text);
            Assert.True(Get<Button>(window, "CopyButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditingDuringTranslationKeepsInputEditableAndNeverPublishesTheOldResult() => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "slow previous input";
            await WaitUntil(() => fixture.Requests.Count == 1);
            Assert.False(source.IsReadOnly);
            Assert.True(Get<Button>(window, "ClearButton").IsEnabled);
            Assert.True(Get<Button>(window, "PasteButton").IsEnabled);
            Assert.True(Get<Button>(window, "SwapButton").IsEnabled);
            Assert.True(Get<ComboBox>(window, "SourceLanguage").IsEnabled);
            Assert.True(Get<ComboBox>(window, "TargetLanguage").IsEnabled);
            source.Text = "The latest input";
            source.Select(4, 6);
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0, duringWait: () =>
                Assert.DoesNotContain("previous", Get<TextBox>(window, "TargetText").Text));
            Assert.Equal("en → zh: The latest input", Get<TextBox>(window, "TargetText").Text);
            Assert.Equal("The latest input", source.Text);
            Assert.Equal((4, 6), (source.SelectionStart, source.SelectionLength));
            Assert.Equal(new[] { "slow previous input", "The latest input" }, fixture.Requests.Select(request => request.Text).ToArray());
            Assert.Equal(new[] { "The latest input" }, fixture.CompletedTexts);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DownloadingMissingPivotPacksAutomaticallyTranslatesThePreservedOriginalOnTheDataPage() => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync(directions: []);
        var prompts = new List<string[]>();
        var downloads = new List<string>();
        MainWindow? window = null;
        window = fixture.CreateWindow(confirmMissingPacks: packs =>
        {
            prompts.Add(packs.Select(pack => pack.Id).ToArray());
            return true;
        }, downloadPack: async (pack, _, token) =>
        {
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(window!, "DataPage").Visibility);
            Assert.Equal(pack.SourceLanguage, Get<ComboBox>(window!, "DownloadSourceLanguage").SelectedValue);
            Assert.Equal(pack.TargetLanguage, Get<ComboBox>(window!, "DownloadTargetLanguage").SelectedValue);
            downloads.Add(pack.Id);
            await fixture.ImportPackAsync(pack, token);
        });
        try
        {
            const string original = "原文 D100 3000 ms";
            var source = Get<TextBox>(window, "SourceText");
            source.Text = original;
            source.Select(3, 4);
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0, duringWait: () =>
            {
                var output = Get<TextBox>(window, "TargetText").Text;
                Assert.True(output.Length == 0 || output.StartsWith("en → fr: ", StringComparison.Ordinal), "The intermediate English translation must not be published.");
            });
            Assert.Equal(new[] { "zh-en", "en-fr" }, Assert.Single(prompts));
            Assert.Equal(new[] { "zh-en", "en-fr" }, downloads);
            Assert.Equal(original, source.Text);
            Assert.Equal((3, 4), (source.SelectionStart, source.SelectionLength));
            Assert.Equal("en → fr: zh → en: " + original, Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(new[] { original, "zh → en: " + original }, fixture.Requests.Select(request => request.Text).ToArray());
            Assert.Equal(new[] { "zh", "en" }, fixture.Requests.Select(request => request.SourceLanguage).ToArray());
            Assert.Equal(new[] { "en", "fr" }, fixture.Requests.Select(request => request.TargetLanguage).ToArray());
            Assert.Equal(Visibility.Visible, Get<ScrollViewer>(window, "DataPage").Visibility);
            Assert.Equal(Visibility.Collapsed, Get<Grid>(window, "TranslationPage").Visibility);
            Assert.Contains("Translation complete via English", Get<TextBlock>(window, "StatusText").Text);
            Assert.True(Get<Button>(window, "CopyButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EditingDuringTheSecondPivotStageCancelsTheOldResultAndRetranslatesOnlyTheNewOriginal() => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync(directions: [("zh", "en"), ("en", "fr")]);
        var window = fixture.CreateWindow();
        try
        {
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            var source = Get<TextBox>(window, "SourceText");
            const string previous = "slow previous pivot input";
            source.Text = previous;
            await WaitUntil(() => fixture.Requests.Count == 2);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.False(source.IsReadOnly);
            Assert.Equal(new[] { previous }, fixture.CompletedTexts);
            source.Text = "Latest pivot source D100";
            source.Select(7, 5);
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0, duringWait: () =>
            {
                var output = Get<TextBox>(window, "TargetText").Text;
                Assert.DoesNotContain("previous", output);
                Assert.True(output.Length == 0 || output.StartsWith("en → fr: ", StringComparison.Ordinal), "The intermediate English translation must not be published.");
            });
            Assert.Equal("Latest pivot source D100", source.Text);
            Assert.Equal((7, 5), (source.SelectionStart, source.SelectionLength));
            Assert.Equal("en → fr: zh → en: Latest pivot source D100", Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(new[] { previous, "zh → en: " + previous, source.Text, "zh → en: " + source.Text }, fixture.Requests.Select(request => request.Text).ToArray());
            Assert.Equal(new[] { previous, source.Text, "zh → en: " + source.Text }, fixture.CompletedTexts);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SecondPivotStageFailureKeepsTheOriginalAndNeverPublishesTheIntermediateResult() => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync(directions: [("zh", "en"), ("en", "fr")]);
        var window = fixture.CreateWindow();
        try
        {
            Get<ComboBox>(window, "SourceLanguage").SelectedValue = "zh";
            Get<ComboBox>(window, "TargetLanguage").SelectedValue = "fr";
            const string original = "fail second stage input D100";
            Get<TextBox>(window, "SourceText").Text = original;
            await WaitUntil(() => fixture.Requests.Count == 2 && Get<ProgressBar>(window, "OperationProgress").Visibility == Visibility.Collapsed,
                duringWait: () => Assert.Empty(Get<TextBox>(window, "TargetText").Text));
            Assert.Equal(original, Get<TextBox>(window, "SourceText").Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(new[] { original }, fixture.CompletedTexts);
            Assert.Contains("could not translate", Get<TextBlock>(window, "StatusText").Text);
            Assert.DoesNotContain("fixture failure", Get<TextBlock>(window, "StatusText").Text);
            Assert.False(Get<Button>(window, "CopyButton").IsEnabled);
            Assert.False(Get<Button>(window, "CancelButton").IsEnabled);
            Assert.True(Get<Button>(window, "TranslateButton").IsEnabled);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClearingPendingOrRunningTranslationDoesNotRestartIt(bool waitForWorker) => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var window = fixture.CreateWindow();
        try
        {
            Get<TextBox>(window, "SourceText").Text = "slow clear input";
            if (waitForWorker) await WaitUntil(() => fixture.Requests.Count == 1);
            Click(window, "ClearButton");
            await WaitUntil(() => !fixture.HasRunningWorker && Get<ProgressBar>(window, "OperationProgress").Visibility == Visibility.Collapsed);
            await Task.Delay(800);
            Assert.Empty(Get<TextBox>(window, "SourceText").Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(waitForWorker ? 1 : 0, fixture.Requests.Count);
            Assert.Empty(fixture.CompletedTexts);
            Assert.False(Get<Button>(window, "CancelButton").IsEnabled);
            Assert.False(Get<Button>(window, "CopyButton").IsEnabled);
            Assert.Contains("cleared", Get<TextBlock>(window, "StatusText").Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CancellingPendingOrRunningTranslationWaitsForANewEditBeforeRestarting(bool waitForWorker) => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "slow cancel input";
            if (waitForWorker) await WaitUntil(() => fixture.Requests.Count == 1);
            Assert.True(Get<Button>(window, "CancelButton").IsEnabled);
            Click(window, "CancelButton");
            await WaitUntil(() => !fixture.HasRunningWorker && Get<ProgressBar>(window, "OperationProgress").Visibility == Visibility.Collapsed);
            await Task.Delay(800);
            Assert.Equal("slow cancel input", source.Text);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(waitForWorker ? 1 : 0, fixture.Requests.Count);
            Assert.Empty(fixture.CompletedTexts);
            Assert.Contains("cancelled", Get<TextBlock>(window, "StatusText").Text);
            Assert.False(Get<Button>(window, "CancelButton").IsEnabled);
            source.Text = "A new edit after cancellation";
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0);
            Assert.Equal("en → zh: A new edit after cancellation", Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(waitForWorker ? 2 : 1, fixture.Requests.Count);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SwappingDirectionAutomaticallyTranslatesThePreservedSource(bool whileRunning) => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync(bothDirections: true);
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            source.Text = whileRunning ? "slow direction input" : "direction input";
            if (whileRunning) await WaitUntil(() => fixture.Requests.Count == 1);
            else await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0);
            var original = source.Text;
            Click(window, "SwapButton");
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(original, source.Text);
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0);
            Assert.Equal("zh → en: " + original, Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(new[] { "en", "zh" }, fixture.Requests.Select(request => request.SourceLanguage).ToArray());
            Assert.Equal(new[] { "zh", "en" }, fixture.Requests.Select(request => request.TargetLanguage).ToArray());
            Assert.All(fixture.Requests, request => Assert.Equal(original, request.Text));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ImeCompositionDefersAutomaticAndManualTranslationUntilCommit() => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var window = fixture.CreateWindow();
        try
        {
            var source = Get<TextBox>(window, "SourceText");
            RaiseComposition(source, TextCompositionManager.PreviewTextInputStartEvent);
            source.Text = "に";
            await Task.Delay(700);
            Assert.Empty(fixture.Requests);
            RaiseComposition(source, TextCompositionManager.PreviewTextInputUpdateEvent);
            source.Text = "日本語";
            await window.TranslateAsync();
            await Task.Delay(700);
            Assert.Empty(fixture.Requests);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            RaiseComposition(source, TextCompositionManager.PreviewTextInputEvent);
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0);
            Assert.Equal("日本語", Assert.Single(fixture.Requests).Text);
            Assert.Equal("en → zh: 日本語", Get<TextBox>(window, "TargetText").Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingStopsPendingAndRunningTranslationAndReleasesThePack(bool waitForWorker) => OnSta(async () =>
    {
        await using var fixture = await WorkerFixture.CreateAsync();
        var window = fixture.CreateWindow();
        try
        {
            Get<TextBox>(window, "SourceText").Text = "slow closing input";
            if (waitForWorker) await WaitUntil(() => fixture.Requests.Count == 1);
            var status = Get<TextBlock>(window, "StatusText").Text;
            window.Close();
            await WaitUntil(() => !fixture.HasRunningWorker);
            // A worker can exit before the engine finishes draining it and releases its lease.
            await WaitUntil(() =>
            {
                try { fixture.Service.Remove("en-zh"); return true; }
                catch (TransLampException error) when (error.Code == "pack-busy") { return false; }
            });
            await Task.Delay(800);
            Assert.Null(fixture.Service.Find("en", "zh"));
            Assert.Equal(waitForWorker ? 1 : 0, fixture.Requests.Count);
            Assert.Empty(fixture.CompletedTexts);
            Assert.Empty(Get<TextBox>(window, "TargetText").Text);
            Assert.Equal(status, Get<TextBlock>(window, "StatusText").Text);
        }
        finally { window.Close(); }
    });

    [PreparedModelsFact]
    public Task ActualModelsAutomaticallyTranslateTypedInputInBothDirections() => OnSta(async () =>
    {
        var resources = Environment.GetEnvironmentVariable("TRANSLAMP_RESOURCES")!;
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TransLamp.LiveTests", Guid.NewGuid().ToString("N")));
        var engine = new TranslationEngine(Path.Combine(resources, "Runtime"));
        var service = new LanguagePackService(Path.Combine(directory, "packs"), engine.ValidatePackAsync);
        MainWindow? window = null;
        try
        {
            Assert.True(engine.IsAvailable, "TRANSLAMP_RESOURCES must contain the prepared Runtime directory.");
            foreach (var direction in new[] { "en-zh", "zh-en" })
                await service.ImportAsync(Directory.EnumerateFiles(Path.Combine(resources, "LanguagePacks"), $"*.{direction}.*.tlpack").Single());
            window = new MainWindow(service, engine, Path.Combine(directory, "bundled"), _ => false, null)
            { PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false, SelectedLanguage = "en" };
            var source = Get<TextBox>(window, "SourceText");
            source.Text = "Please restart the computer.";
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0, timeoutSeconds: 60);
            Assert.Matches("[\\u4e00-\\u9fff]", Get<TextBox>(window, "TargetText").Text);
            Assert.DoesNotContain("▁", Get<TextBox>(window, "TargetText").Text);
            source.Text = "连接失败，请重新启动电脑。";
            Click(window, "SwapButton");
            await WaitUntil(() => Get<TextBox>(window, "TargetText").Text.Length > 0, timeoutSeconds: 60);
            Assert.Matches("[a-zA-Z]{3,}", Get<TextBox>(window, "TargetText").Text);
            Assert.DoesNotContain("▁", Get<TextBox>(window, "TargetText").Text);
            Assert.Equal("连接失败，请重新启动电脑。", source.Text);
        }
        finally
        {
            window?.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }, timeoutSeconds: 180);

    private static T Get<T>(MainWindow window, string name) where T : class => Assert.IsAssignableFrom<T>(window.FindName(name));
    private static void Click(MainWindow window, string name) => Get<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void RaiseComposition(TextBox source, RoutedEvent compositionEvent) => source.RaiseEvent(
        new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, source, ""))
        { RoutedEvent = compositionEvent, Handled = true });

    private static async Task WaitUntil(Func<bool> condition, int timeoutSeconds = 10, Action? duringWait = null)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            duringWait?.Invoke();
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(timeoutSeconds), "The observable translation state did not arrive before the deadline.");
            await Task.Delay(20);
        }
        duringWait?.Invoke();
    }

    private static Task OnSta(Func<Task> action, int timeoutSeconds = 45)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception error) { completion.SetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "TransLamp live translation verification" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
    }

    private sealed class PreparedModelsFactAttribute : FactAttribute
    {
        public PreparedModelsFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRANSLAMP_RESOURCES")))
                Skip = "Set TRANSLAMP_RESOURCES to run the actual CPU model UI integration; fixture worker tests do not validate model quality.";
        }
    }

    private sealed record WorkerRequest(string Text, string SourceLanguage, string TargetLanguage);

    /// <summary>A deterministic console worker exercises real process execution, JSON transport and pack leases without native models.</summary>
    private sealed class WorkerFixture : IAsyncDisposable
    {
        private readonly string _temporaryBase = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TransLamp.LiveTests"));
        private readonly string _root;
        private string RuntimeDirectory => Path.Combine(_root, "runtime");
        public LanguagePackService Service { get; }
        public IReadOnlyList<WorkerRequest> Requests => ReadLines("requests.jsonl")
            .Select(line => JsonSerializer.Deserialize<WorkerRequest>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!).ToArray();
        public string[] CompletedTexts => ReadLines("completed.jsonl").Select(line => JsonSerializer.Deserialize<string>(line)!).ToArray();
        private int[] WorkerIds => ReadLines("processes.log").Select(int.Parse).ToArray();
        public bool HasRunningWorker => WorkerIds.Any(IsRunning);

        private WorkerFixture()
        {
            _root = Path.GetFullPath(Path.Combine(_temporaryBase, Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(RuntimeDirectory);
            Service = new LanguagePackService(Path.Combine(_root, "packs"), (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        }

        public static async Task<WorkerFixture> CreateAsync(bool bothDirections = false, IReadOnlyList<(string Source, string Target)>? directions = null)
        {
            var fixture = new WorkerFixture();
            try
            {
                await fixture.BuildWorkerAsync();
                foreach (var direction in directions ?? (bothDirections ? [("en", "zh"), ("zh", "en")] : [("en", "zh")]))
                    await fixture.Service.ImportAsync(fixture.MakePack(direction.Source, direction.Target));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public MainWindow CreateWindow(Func<IReadOnlyList<DownloadableLanguagePack>, bool>? confirmMissingPacks = null,
            Func<DownloadableLanguagePack, IProgress<LanguagePackDownloadProgress>?, CancellationToken, Task>? downloadPack = null)
            => new(Service, new TranslationEngine(RuntimeDirectory), Path.Combine(_root, "bundled"), confirmMissingPacks ?? (_ => false), downloadPack)
        { PreferencesPath = null, ShowActivated = false, ShowInTaskbar = false, SelectedLanguage = "en" };

        public Task<InstalledLanguagePack> ImportPackAsync(DownloadableLanguagePack pack, CancellationToken token) =>
            Service.ImportAsync(MakePack(pack.SourceLanguage, pack.TargetLanguage), null, token);

        private async Task BuildWorkerAsync()
        {
            var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
            if (!File.Exists(Path.Combine(framework, "csc.exe")))
                framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework", "v4.0.30319");
            Assert.True(File.Exists(Path.Combine(framework, "csc.exe")), "The Windows .NET Framework compiler is required for deterministic worker fixtures.");
            var source = Path.Combine(_root, "worker.cs");
            await File.WriteAllTextAsync(source, WorkerSource);
            var start = new ProcessStartInfo(Path.Combine(framework, "csc.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/nologo", "/target:exe", "/out:" + Path.Combine(RuntimeDirectory, "python.exe"), "/r:" + Path.Combine(framework, "System.Web.Extensions.dll"), source })
                start.ArgumentList.Add(argument);
            using var compiler = Process.Start(start)!;
            var stdout = compiler.StandardOutput.ReadToEndAsync();
            var stderr = compiler.StandardError.ReadToEndAsync();
            await compiler.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(compiler.ExitCode == 0, "Fixture worker compilation failed: " + await stdout + await stderr);
            await File.WriteAllTextAsync(Path.Combine(RuntimeDirectory, "translate.py"), "Fixture worker is compiled into python.exe; no Python or model assets are needed.");
        }

        private string MakePack(string source, string target)
        {
            var files = new Dictionary<string, byte[]>
            {
                ["LICENSE"] = Encoding.UTF8.GetBytes("MIT fixture license"),
                ["NOTICE"] = Encoding.UTF8.GetBytes("Deterministic process fixture, not a translation model"),
                ["sentencepiece.model"] = Encoding.UTF8.GetBytes("fixture tokenizer"),
                ["model/model.bin"] = Encoding.UTF8.GetBytes("fixture model"),
                ["model/config.json"] = Encoding.UTF8.GetBytes("{}"),
                ["model/shared_vocabulary.json"] = Encoding.UTF8.GetBytes("[]")
            };
            var manifest = new LanguagePackManifest
            {
                SchemaVersion = 1, Id = source + "-" + target, SourceLanguage = source, TargetLanguage = target,
                DisplayName = "Live fixture " + source + " → " + target, PackageVersion = "1.0.0", ModelName = "Process fixture", ModelVersion = "1.9",
                Runtime = LanguagePackService.SupportedRuntime, ModelSource = "https://example.test/fixture", LicenseIdentifier = "MIT",
                Files = files.Select(pair => new LanguagePackFile
                { Path = pair.Key, Size = pair.Value.LongLength, Sha256 = Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant() }).ToList()
            };
            var path = Path.Combine(_root, source + "-" + target + ".tlpack");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, bytes) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
            using var metadata = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(metadata, manifest, LanguagePackManifest.JsonOptions);
            return path;
        }

        private string[] ReadLines(string name)
        {
            var path = Path.Combine(RuntimeDirectory, name);
            if (!File.Exists(path)) return [];
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = reader.ReadToEnd();
            var lastLine = content.LastIndexOf('\n');
            return lastLine < 0 ? [] : content[..lastLine].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool IsRunning(int id)
        {
            try { using var process = Process.GetProcessById(id); return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var id in WorkerIds)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                catch (ArgumentException) { }
            }
            if (!_root.StartsWith(_temporaryBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Live fixture cleanup must stay within its temporary base.");
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // This is a .NET Framework console fixture for Windows WPF tests. It deliberately echoes text;
        // actual model translation is covered separately and opt-in skips are reported by xUnit.
        private const string WorkerSource = """
            using System;
            using System.Collections.Generic;
            using System.Diagnostics;
            using System.IO;
            using System.Text;
            using System.Threading;
            using System.Web.Script.Serialization;
            class Worker
            {
                static void Main()
                {
                    Console.InputEncoding = new UTF8Encoding(false);
                    Console.OutputEncoding = new UTF8Encoding(false);
                    File.AppendAllText("processes.log", Process.GetCurrentProcess().Id + "\n");
                    var line = Console.ReadLine();
                    var json = new JavaScriptSerializer();
                    var request = json.Deserialize<Dictionary<string, object>>(line);
                    File.AppendAllText("requests.jsonl", line + "\n", new UTF8Encoding(false));
                    var text = (string)request["text"];
                    var source = (string)request["sourceLanguage"];
                    var target = (string)request["targetLanguage"];
                    Console.WriteLine("{\"progress\":0,\"total\":1}");
                    Console.Out.Flush();
                    if (text.Contains("fail second stage") && source == "en" && target == "fr")
                    {
                        Console.Error.WriteLine("Deterministic second-stage fixture failure.");
                        Environment.Exit(3);
                        return;
                    }
                    Thread.Sleep(text.Contains("slow ") && source == "en" ? 30000 : 80);
                    File.AppendAllText("completed.jsonl", json.Serialize(text) + "\n", new UTF8Encoding(false));
                    Console.WriteLine(json.Serialize(new Dictionary<string, string> { { "text", source + " → " + target + ": " + text } }));
                }
            }
            """;
    }
}
