using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HistoLens;

/// <summary>The product image shared by the module window and the host catalog.</summary>
public static class ProductIcon
{
    private static readonly Lazy<ImageSource> Image = new(() =>
    {
        var decoder = new IconBitmapDecoder(
            new Uri("pack://application:,,,/HistoLens;component/Assets/HistoLens.ico"),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames.MaxBy(frame => frame.PixelWidth)!;
        image.Freeze();
        return image;
    });

    public static ImageSource Source => Image.Value;
}
