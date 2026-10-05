using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace ToolKeeper;

/// <summary>Default branding for the executable and hosted windows without a product icon.</summary>
internal static class PlatformIcon
{
    private const string ResourceName = "ToolKeeper.Resources.ToolKeeper.ico";
    private static readonly Lazy<ImageSource> WindowImage = new(LoadWindowImage);

    public static ImageSource Source => WindowImage.Value;

    public static void InitializeHostedWindows()
    {
        // ApplicationIcon already supplies the native default. Set the WPF property too,
        // so module About panels receive an image too. Preserve explicit product icons.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, args) =>
            {
                if (sender is Window { Icon: null } window && ReferenceEquals(args.OriginalSource, window))
                    window.SetCurrentValue(Window.IconProperty, WindowImage.Value);
            }), handledEventsToo: true);
    }

    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = OpenResource();
        using var source = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)source.Clone();
    }

    private static ImageSource LoadWindowImage()
    {
        using var stream = OpenResource();
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames.MaxBy(frame => frame.PixelWidth)!;
        image.Freeze();
        return image;
    }

    private static Stream OpenResource() => typeof(PlatformIcon).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException("Missing ToolKeeper application icon.");
}
