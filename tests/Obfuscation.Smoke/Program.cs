using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Obfuscation.Smoke;

internal static class Program
{
    private static string publishDirectory = "";
    private static string temporaryDirectory = "";
    private static readonly List<Window> windows = [];
    private static readonly Dictionary<string, Assembly> assemblies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };

    [STAThread]
    private static int Main(string[] args)
    {
        var exitCode = 1;
        try
        {
            var options = ParseArguments(args);
            publishDirectory = Path.GetFullPath(options.Directory);
            if (!Directory.Exists(publishDirectory)) throw new DirectoryNotFoundException(publishDirectory);
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "ToolKeeper.Obfuscation.Smoke", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
            AssemblyLoadContext.Default.Resolving += ResolvePublishedAssembly;
            // A neutral application never runs the products' startup handlers.
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    SharedUi();
                    switch (options.Product.ToLowerInvariant())
                    {
                        case "toolkeeper": await ToolKeeper(); break;
                        case "markpad": case "hanqing": MarkPad(); break;
                        case "convanvil": ConvAnvil(); break;
                        case "translamp": TransLamp(); break;
                        default: throw new ArgumentException("Product must be ToolKeeper, MarkPad/Hanqing, ConvAnvil or TransLamp.");
                    }
                    VerifyPublishedLocations();
                    Console.WriteLine($"PASS {options.Product}: published contracts, WPF construction and JSON round trips.");
                    exitCode = 0;
                }
                catch (Exception exception) { Console.Error.WriteLine(Unwrap(exception)); }
                finally
                {
                    foreach (var window in windows.AsEnumerable().Reverse()) window.Close();
                    app.Shutdown(exitCode);
                }
            }));
            app.Run();
        }
        catch (Exception exception) { Console.Error.WriteLine(Unwrap(exception)); }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= ResolvePublishedAssembly;
            if (temporaryDirectory.Length > 0 && Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
        return exitCode;
    }

    private static (string Directory, string Product) ParseArguments(string[] args)
    {
        string? directory = null, product = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Expected --publish-directory <path> --product <name>.");
            switch (args[i])
            {
                case "--publish-directory": directory = args[++i]; break;
                case "--product": product = args[++i]; break;
                default: throw new ArgumentException("Unknown argument: " + args[i]);
            }
        }
        return (directory ?? throw new ArgumentException("Missing --publish-directory."),
            product ?? throw new ArgumentException("Missing --product."));
    }

    private static Assembly? ResolvePublishedAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        var path = Path.Combine(publishDirectory, name.Name + ".dll");
        return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    }

    private static Assembly Load(string name)
    {
        if (!assemblies.TryGetValue(name, out var assembly))
        {
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(publishDirectory, name + ".dll"));
            assemblies.Add(name, assembly);
            Require(assembly.GetName().Name == name, "Assembly identity changed: " + name);
        }
        return assembly;
    }

    private static Type Type(string assembly, string name) => Load(assembly).GetType(name, throwOnError: true)!;

    private static object Create(Type type, params object?[] args)
    {
        var constructor = type.GetConstructors().Single(candidate => Matches(candidate.GetParameters(), args));
        return constructor.Invoke(CompleteArguments(constructor.GetParameters(), args));
    }

    private static object? Call(object target, string name, params object?[] args)
    {
        var type = target as Type ?? target.GetType();
        var flags = BindingFlags.Public | (target is Type ? BindingFlags.Static : BindingFlags.Instance);
        var method = type.GetMethods(flags).Single(candidate => candidate.Name == name && Matches(candidate.GetParameters(), args));
        return method.Invoke(target is Type ? null : target, CompleteArguments(method.GetParameters(), args));
    }

    private static bool Matches(ParameterInfo[] parameters, object?[] args) =>
        parameters.Length >= args.Length && parameters.Skip(args.Length).All(parameter => parameter.IsOptional) &&
        parameters.Take(args.Length).Select((parameter, index) => args[index] is null || parameter.ParameterType.IsInstanceOfType(args[index])).All(value => value);

    private static object?[] CompleteArguments(ParameterInfo[] parameters, object?[] args) =>
        args.Concat(parameters.Skip(args.Length).Select(parameter => parameter.DefaultValue)).ToArray();

    private static object? Get(object target, string property) =>
        (target as Type ?? target.GetType()).GetProperty(property)!.GetValue(target is Type ? null : target);

    private static void Set(object target, string property, object? value) => target.GetType().GetProperty(property)!.SetValue(target, value);

    private static Window Window(Type type, params object?[] args)
    {
        var window = (Window)Create(type, args);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        if (type.GetProperty("PreferencesPath") is not null) Set(window, "PreferencesPath", null);
        windows.Add(window);
        Require(window.Content is FrameworkElement, type.FullName + " did not construct its WPF content.");
        Console.WriteLine("WPF " + type.FullName);
        return window;
    }

    private static T Named<T>(Window window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new InvalidOperationException("Missing XAML control " + name);

    private static void SharedUi()
    {
        var window = Window(Type("ToolKeeper.UI", "ToolKeeper.UI.AppWindow"));
        Set(window, "SelectedLanguage", "ja");
        Set(window, "SelectedTheme", "InkDark");
        Call(window, "ApplyUiPreferences");
        Require(window.Content.GetType().FullName == "ToolKeeper.UI.WindowFrame", "Shared WPF frame contract changed.");
        Require(window.Resources["TextBrush"] is Brush, "Shared theme resources did not load.");
        RoundTrip(Type("ToolKeeper.UI", "ToolKeeper.UI.AppWindowPreferences"), "{\"Theme\":\"InkDark\",\"Language\":\"ja\"}");
    }

    private static async Task ToolKeeper()
    {
        var window = Window(Type("ToolKeeper", "ToolKeeper.MainWindow"));
        Require(Named<ItemsControl>(window, "Products").ItemsSource is IEnumerable, "Product catalog binding did not load.");
        var hashWindow = Window(Type("ToolKeeper", "ToolKeeper.Modules.HashCheckerWindow"));
        var file = Path.Combine(temporaryDirectory, "abc.txt");
        await File.WriteAllTextAsync(file, "abc", new UTF8Encoding(false));
        await (Task)Call(hashWindow, "LoadHashFileAsync", file)!;
        Require(Named<TextBox>(hashWindow, "Sha256Output").Text.Equals(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", StringComparison.OrdinalIgnoreCase), "Published hash calculation failed.");
        var iconWindow = Window(Type("ToolKeeper", "ToolKeeper.Modules.ImageToIcoWindow"));
        Named<ItemsControl>(iconWindow, "IconResults");
        var image = Path.Combine(temporaryDirectory, "source.png");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255 }, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(image)) encoder.Save(stream);
        var converted = Call(Type("ToolKeeper", "ToolKeeper.Services.IconConversionService"), "Convert", image)!;
        using (var stream = File.OpenRead((string)Get(converted, "OutputPath")!))
        {
            var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Require(decoder.Frames.Count == 7 && decoder.Frames.Any(frame => frame.PixelWidth == 256), "Published ICO conversion failed.");
        }
        RoundTrip(Type("ToolKeeper", "ToolKeeper.Services.FileHashResult"),
            "{\"FilePath\":\"fixture\",\"Length\":3,\"Md5\":\"md5\",\"Sha1\":\"sha1\",\"Sha256\":\"sha256\"}");

        var preview = Window(Type("ToolKeeper.Desktop", "CabiDock.GroupPreviewWindow"));
        Set(preview, "AllowClose", true);
        Call(preview, "SetTheme", "InkDark");
        RoundTrip(Type("ToolKeeper.Desktop", "CabiDock.Models.CabiDockConfiguration"),
            "{\"Categories\":[{\"Id\":\"fixture\",\"Name\":\"Fixture\",\"Extensions\":[\".txt\"],\"Kind\":0,\"IsCustom\":true}],\"KeywordRules\":[],\"GroupOpacity\":0.75}");
        RoundTrip(Type("ToolKeeper.Desktop", "CabiDock.Models.CabiDockState"),
            "{\"ConfigurationFingerprint\":\"fixture\",\"Items\":[{\"FullPath\":\"fixture.txt\",\"Identity\":\"fixture\",\"CategoryId\":\"fixture\",\"Source\":1}],\"Groups\":{\"fixture\":{\"X\":12,\"Y\":23,\"Width\":340,\"Height\":280}}}");
        RoundTrip(FindPrivateRecord("ToolKeeper.Desktop", "PipeName", "ParentId", "Target", "UseOwnedWindowLease"),
            "{\"PipeName\":\"smoke-only\",\"ParentId\":123,\"ParentStartTicks\":456,\"Target\":{\"Window\":789,\"ProcessId\":123,\"ProcessStartTicks\":456},\"UseOwnedWindowLease\":true}");

        var research = Window(Type("HistoLens", "HistoLens.MainWindow"), Path.Combine(temporaryDirectory, "histolens"));
        Call(research, "LoadDemo");
        await (Task)Call(research, "RunResearchAsync")!;
        var result = Get(research, "Result") ?? throw new InvalidOperationException("Published HistoLens research produced no result.");
        Require((bool)Get(result, "IsResearchAllowed")!, "Published HistoLens demo research rejected its own fixture.");
        RoundTripObject(result);
        Console.WriteLine("CORE ToolKeeper hash/ICO, CabiDock contracts, HistoLens demo research");
    }

    private static void MarkPad()
    {
        var settings = Create(Type("Hanqing", "MarkPad.Services.SettingsService"), Path.Combine(temporaryDirectory, "markpad"));
        Set(Get(settings, "Settings")!, "Language", "ja");
        Call(settings, "Save");
        var reloaded = Create(Type("Hanqing", "MarkPad.Services.SettingsService"), Path.Combine(temporaryDirectory, "markpad"));
        Require((string)Get(Get(reloaded, "Settings")!, "Language")! == "ja", "Published settings persistence lost Language.");
        var app = Type("Hanqing", "MarkPad.App");
        app.GetProperty("Preferences")!.SetValue(null, reloaded);
        app.GetProperty("Recovery")!.SetValue(null, Create(Type("Hanqing", "MarkPad.Services.RecoveryService"), Path.Combine(temporaryDirectory, "recovery")));
        var window = Window(Type("Hanqing", "MarkPad.MainWindow"), true);
        Named<FrameworkElement>(window, "RootGrid");
        RoundTripObject(Get(reloaded, "Settings")!);
        RoundTrip(FindPrivateRecord("Hanqing", "Id", "Paths", "NewWindow"),
            "{\"Id\":\"cd2a98e8-0c2d-45bc-bcb1-b98b4c4b8928\",\"Paths\":[\"fixture.md\"],\"NewWindow\":true}");
        RoundTrip(Type("Hanqing", "MarkPad.Rendering.PreviewMessage"),
            "{\"Type\":\"scroll\",\"Text\":\"fixture\",\"Line\":7,\"Count\":3,\"Index\":1,\"Flag\":true,\"SourcePosition\":6.5,\"ScrollProgress\":0.5}");
        var html = (string)Call(Create(Type("Hanqing", "MarkPad.Rendering.MarkdownRenderer")), "Render",
            "# Smoke heading\n\n**Smoke body**", Create(Type("Hanqing", "MarkPad.Rendering.PreviewOptions")))!;
        Require(html.Contains("Smoke heading") && html.Contains("<strong>Smoke body</strong>"), "Published Markdown renderer failed.");
        Console.WriteLine("CORE Hanqing settings, Markdown rendering, preview and activation JSON");
    }

    private static void ConvAnvil()
    {
        var window = Window(Type("ConvAnvil", "ConvAnvil.MainWindow"));
        Require(Named<ComboBox>(window, "TextEncoding").Items.Count > 0, "Encoding catalog binding failed.");
        Require(Named<ComboBox>(window, "TextFormat").Items.Count == 6, "Format choice binding failed.");
        var encoding = Call(Type("ConvAnvil", "ConvAnvil.Services.EncodingCatalog"), "Find", "utf-8-bom")!;
        var conversion = Type("ConvAnvil", "ConvAnvil.Services.EncodingConversionService");
        const string content = "Smoke 中文\r\n";
        var bytes = (byte[])Call(conversion, "Encode", content, encoding)!;
        Require(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 BOM conversion failed.");
        Require((string)Call(conversion, "Decode", bytes, encoding)! == content, "Encoding round trip failed.");
        var format = Enum.Parse(Type("ConvAnvil", "ConvAnvil.Services.ByteFormat"), "Hex");
        var notation = Type("ConvAnvil", "ConvAnvil.Services.ByteNotation");
        var text = (string)Call(notation, "Format", bytes, format)!;
        Require(((byte[])Call(notation, "Parse", text, format)!).SequenceEqual(bytes), "Byte notation round trip failed.");
        RoundTripObject(encoding);
        Console.WriteLine("CORE ConvAnvil encodings, byte formats and JSON contracts");
    }

    private static void TransLamp()
    {
        var packs = Create(Type("TransLamp", "TransLamp.Core.LanguagePackService"), Path.Combine(temporaryDirectory, "packs"));
        var engine = Create(Type("TransLamp", "TransLamp.Core.TranslationEngine"), Path.Combine(temporaryDirectory, "absent-runtime"));
        var window = Window(Type("TransLamp", "TransLamp.MainWindow"), packs, engine, Path.Combine(temporaryDirectory, "absent-bundled"));
        Require(Named<ComboBox>(window, "SourceLanguage").Items.Count > 0, "Language option binding failed.");
        Require(!(bool)Get(engine, "IsAvailable")!, "Fixture must not use a live translation runtime.");
        var route = Call(Type("TransLamp", "TransLamp.Core.TranslationRoute"), "Resolve", "en", "zh");
        Require(route is not null && !(bool)Get(route, "ViaEnglish")!, "Published direct language route failed.");
        RoundTrip(Type("TransLamp", "TransLamp.Core.LanguagePackManifest"),
            "{\"SchemaVersion\":1,\"Id\":\"en-zh\",\"SourceLanguage\":\"en\",\"TargetLanguage\":\"zh\",\"DisplayName\":\"Fixture\",\"Files\":[{\"Path\":\"model.bin\",\"Size\":7,\"Sha256\":\"fixture\"}]}");
        RoundTrip(FindPrivateRecord("TransLamp", "Id", "Kind", "StagingDirectory", "BackupDirectory", "HadPrevious", "HadRemovedMarker"),
            "{\"Id\":\"en-zh\",\"Kind\":\"install\",\"StagingDirectory\":\"fixture-stage\",\"BackupDirectory\":\"fixture-backup\",\"HadPrevious\":true,\"HadRemovedMarker\":false}");
        Console.WriteLine("CORE TransLamp routing, manifest and transaction JSON");
    }

    private static Type FindPrivateRecord(string assembly, params string[] properties) =>
        Load(assembly).GetTypes().Single(type => !type.IsVisible && properties.All(property => type.GetProperty(property) is not null));

    private static void RoundTrip(Type type, string source)
    {
        var value = JsonSerializer.Deserialize(source, type, jsonOptions) ?? throw new InvalidOperationException("JSON returned null: " + type);
        var output = JsonSerializer.Serialize(value, type, jsonOptions);
        var actual = JsonNode.Parse(output)!.AsObject();
        foreach (var property in JsonNode.Parse(source)!.AsObject())
            Require(actual.TryGetPropertyValue(property.Key, out var node) && JsonNode.DeepEquals(property.Value, node), "JSON property changed: " + property.Key + " in " + type);
        RoundTripObject(value);
    }

    private static void RoundTripObject(object value)
    {
        var json = JsonSerializer.Serialize(value, value.GetType(), jsonOptions);
        var restored = JsonSerializer.Deserialize(json, value.GetType(), jsonOptions)!;
        var second = JsonSerializer.Serialize(restored, value.GetType(), jsonOptions);
        Require(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(second)), "JSON round trip changed values: " + value.GetType());
        Console.WriteLine("JSON " + value.GetType());
    }

    private static void VerifyPublishedLocations()
    {
        foreach (var name in new[] { "ToolKeeper", "ToolKeeper.UI", "ToolKeeper.Desktop", "HistoLens", "Hanqing", "ConvAnvil", "TransLamp" })
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == name);
            if (loaded is null) continue;
            var expected = Path.Combine(publishDirectory, name + ".dll");
            Require(Path.GetFullPath(loaded.Location).Equals(expected, StringComparison.OrdinalIgnoreCase), "Loaded build assembly instead of published assembly: " + loaded.Location);
            Console.WriteLine("ASSEMBLY " + loaded.Location);
        }
    }

    private static Exception Unwrap(Exception exception) => exception is TargetInvocationException { InnerException: not null } invocation ? Unwrap(invocation.InnerException!) : exception;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
