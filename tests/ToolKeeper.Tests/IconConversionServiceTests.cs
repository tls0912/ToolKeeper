using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToolKeeper.Services;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class IconConversionServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ToolKeeper.IconTests", Guid.NewGuid().ToString("N"));

    public IconConversionServiceTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData(".png")]
    [InlineData(".PNG")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".bmp")]
    public async Task ConvertsOnWorkerThreadToSevenDecodableIconFrames(string extension)
    {
        var path = WriteImage("source" + extension, 40, 20, (_, _) => new byte[] { 30, 60, 240, 255 });
        var original = File.ReadAllBytes(path);
        var result = await Task.Run(() => IconConversionService.Convert(path));

        Assert.Equal(path, result.SourcePath);
        Assert.Equal(Path.ChangeExtension(path, ".ico"), result.OutputPath);
        Assert.Equal(original, File.ReadAllBytes(path));
        using var input = File.OpenRead(result.OutputPath);
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(7, reader.ReadUInt16());
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var expectedOffset = 118;
        foreach (var size in sizes)
        {
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            Assert.Equal(size == 256 ? 0 : size, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            var length = reader.ReadInt32();
            Assert.True(length > 0);
            Assert.Equal(expectedOffset, reader.ReadInt32());
            expectedOffset += length;
        }
        Assert.Equal(input.Length, expectedOffset);
        input.Position = 0;
        var decoder = new IconBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(sizes, decoder.Frames.Select(frame => frame.PixelWidth).Order().ToArray());
        Assert.All(decoder.Frames, frame => Assert.Equal(frame.PixelWidth, frame.PixelHeight));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void PreservesAspectRatioTransparencyAndCenteredPadding()
    {
        var path = WriteImage("wide.png", 32, 16, (x, _) => x < 16 ? new byte[] { 0, 0, 255, 128 } : new byte[] { 255, 0, 0, 255 });
        var result = IconConversionService.Convert(path);
        var pixels = ReadIconPixels(result.OutputPath, 32);

        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(pixels, 32, 16, 7));
        Assert.Equal(new byte[] { 0, 0, 255, 128 }, Pixel(pixels, 32, 5, 8));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(pixels, 32, 25, 23));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, Pixel(pixels, 32, 16, 24));
    }

    [Fact]
    public void CentersPortraitImageWithTransparentSidePadding()
    {
        var path = WriteImage("tall.png", 16, 32, (_, _) => new byte[] { 0, 255, 0, 255 });
        var result = IconConversionService.Convert(path);
        var pixels = ReadIconPixels(result.OutputPath, 32);
        Assert.Equal(0, Pixel(pixels, 32, 7, 16)[3]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(pixels, 32, 8, 16));
        Assert.Equal(255, Pixel(pixels, 32, 23, 16)[3]);
        Assert.Equal(0, Pixel(pixels, 32, 24, 16)[3]);
    }

    [Fact]
    public void TransparentColorsDoNotCreateDarkOrColoredFringes()
    {
        var path = WriteImage("alpha.png", 2, 2, (x, _) => x == 0 ? new byte[] { 0, 0, 255, 255 } : new byte[] { 255, 0, 0, 0 });
        var result = IconConversionService.Convert(path);
        var pixels = ReadIconPixels(result.OutputPath, 32);
        var transition = Pixel(pixels, 32, 16, 16);
        Assert.InRange(transition[3], 1, 254);
        Assert.Equal(0, transition[0]);
        Assert.Equal(0, transition[1]);
        Assert.Equal(255, transition[2]);
    }

    [Theory]
    [InlineData(1, 0, 1, 2, 3)]
    [InlineData(2, 1, 0, 3, 2)]
    [InlineData(3, 3, 2, 1, 0)]
    [InlineData(4, 2, 3, 0, 1)]
    [InlineData(5, 0, 2, 1, 3)]
    [InlineData(6, 2, 0, 3, 1)]
    [InlineData(7, 3, 1, 2, 0)]
    [InlineData(8, 1, 3, 0, 2)]
    public void AppliesEveryJpegExifOrientation(int orientation, int topLeft, int topRight, int bottomLeft, int bottomRight)
    {
        byte[][] colors = [[0, 0, 255, 255], [0, 255, 0, 255], [255, 0, 0, 255], [0, 255, 255, 255]];
        var metadata = new BitmapMetadata("jpg");
        metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
        var path = WriteImage("orientation.jpg", 40, 20, (x, y) => colors[(y < 10 ? 0 : 2) + (x < 20 ? 0 : 1)], new JpegBitmapEncoder { QualityLevel = 100 }, metadata);
        var result = IconConversionService.Convert(path);
        var pixels = ReadIconPixels(result.OutputPath, 32);
        var rotated = orientation >= 5;
        var left = rotated ? 11 : 4;
        var right = rotated ? 20 : 27;
        var top = rotated ? 4 : 11;
        var bottom = rotated ? 27 : 20;
        AssertColorNear(colors[topLeft], Pixel(pixels, 32, left, top));
        AssertColorNear(colors[topRight], Pixel(pixels, 32, right, top));
        AssertColorNear(colors[bottomLeft], Pixel(pixels, 32, left, bottom));
        AssertColorNear(colors[bottomRight], Pixel(pixels, 32, right, bottom));
        Assert.Equal(0, Pixel(pixels, 32, rotated ? 7 : 16, rotated ? 16 : 7)[3]);
    }

    [Fact]
    public async Task ParallelConversionsNeverOverwriteExistingOutputs()
    {
        var path = WriteImage("same.png", 8, 8, (_, _) => new byte[] { 0, 0, 0, 255 });
        var existing = Path.Combine(directory, "same.ico");
        File.WriteAllText(existing, "keep existing output");
        Directory.CreateDirectory(Path.Combine(directory, "same (1).ico"));
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => IconConversionService.Convert(path))));

        Assert.Equal(4, results.Select(result => result.OutputPath).Distinct().Count());
        Assert.Equal("keep existing output", File.ReadAllText(existing));
        Assert.All(results, result => Assert.True(File.Exists(result.OutputPath)));
        Assert.True(Directory.Exists(Path.Combine(directory, "same (1).ico")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void RejectsUnsupportedContentEvenWhenExtensionIsPng()
    {
        var path = WriteImage("disguised.png", 4, 4, (_, _) => new byte[] { 10, 20, 30, 255 }, new GifBitmapEncoder());
        Assert.Throws<InvalidDataException>(() => IconConversionService.Convert(path));
        AssertNoOutput();
    }

    [Fact]
    public void RejectsInvalidImagesAndUnsupportedExtensionsWithoutCreatingOutput()
    {
        var path = Path.Combine(directory, "broken.png");
        File.WriteAllText(path, "this is not an image");
        Assert.ThrowsAny<Exception>(() => IconConversionService.Convert(path));
        Assert.Throws<InvalidDataException>(() => IconConversionService.Convert(Path.Combine(directory, "image.gif")));
        AssertNoOutput();
    }

    [Fact]
    public void RejectsOversizedInputBeforeDecoding()
    {
        var path = Path.Combine(directory, "large.png");
        using (var file = File.Create(path)) file.SetLength(IconConversionService.MaxFileBytes + 1);
        var error = Assert.Throws<InvalidDataException>(() => IconConversionService.Convert(path));
        Assert.Contains("64 MiB", error.Message);
        AssertNoOutput();
    }

    [Fact]
    public void RejectsExcessiveDimensionBeforeAllocatingPixels()
    {
        var path = WriteImage("wide.png", IconConversionService.MaxDimension + 1, 1, (_, _) => new byte[] { 0, 0, 0, 255 });
        var error = Assert.Throws<InvalidDataException>(() => IconConversionService.Convert(path));
        Assert.Contains("16,384", error.Message);
        AssertNoOutput();
    }

    [Fact]
    public void CancelledConversionLeavesSourceIntactAndNoOutput()
    {
        var path = WriteImage("cancel.png", 8, 8, (_, _) => new byte[] { 0, 0, 0, 255 });
        var original = File.ReadAllBytes(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => IconConversionService.Convert(path, cancellation.Token));
        Assert.Equal(original, File.ReadAllBytes(path));
        AssertNoOutput();
    }

    private string WriteImage(string filename, int width, int height, Func<int, int, byte[]> color, BitmapEncoder? encoder = null, BitmapMetadata? metadata = null)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                color(x, y).CopyTo(pixels, (y * width + x) * 4);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        encoder ??= Path.GetExtension(filename) switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        var path = Path.Combine(directory, filename);
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }

    private static byte[] ReadIconPixels(string path, int size)
    {
        using var input = File.OpenRead(path);
        var decoder = new IconBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.Single(frame => frame.PixelWidth == size);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        converted.CopyPixels(pixels, size * 4, 0);
        return pixels;
    }

    private static byte[] Pixel(byte[] pixels, int size, int x, int y) => pixels[((y * size + x) * 4)..((y * size + x) * 4 + 4)];

    private static void AssertColorNear(byte[] expected, byte[] actual)
    {
        for (var channel = 0; channel < 3; channel++)
            Assert.InRange((int)actual[channel], Math.Max(0, expected[channel] - 15), Math.Min(255, expected[channel] + 15));
        Assert.Equal(expected[3], actual[3]);
    }

    private void AssertNoOutput()
    {
        Assert.Empty(Directory.GetFiles(directory, "*.ico"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
