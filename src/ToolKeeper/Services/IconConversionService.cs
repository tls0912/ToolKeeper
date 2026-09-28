using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolKeeper.Services;

public sealed record IconConversionResult(string SourcePath, string OutputPath);

/// <summary>Creates Windows icons locally, without modifying the source image or existing files.</summary>
public static class IconConversionService
{
    public const long MaxFileBytes = 64L * 1024 * 1024;
    public const long MaxPixelCount = 32_000_000;
    public const int MaxDimension = 16_384;

    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    public static IconConversionResult Convert(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var sourcePath = Path.GetFullPath(path);
        var extension = Path.GetExtension(sourcePath);
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("支援 PNG、JPG、JPEG 與 BMP 圖片。");

        byte[] pixels;
        int width;
        int height;
        ushort orientation;
        using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (input.Length > MaxFileBytes)
                throw new InvalidDataException("圖片檔案不可超過 64 MiB。");

            // OnDemand lets us inspect dimensions before allocating the decoded pixel buffer.
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            if (decoder is not (PngBitmapDecoder or JpegBitmapDecoder or BmpBitmapDecoder))
                throw new InvalidDataException("檔案內容必須是 PNG、JPEG 或 BMP 圖片。");

            var frame = decoder.Frames[0];
            orientation = ReadOrientation(frame);
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixelCount)
                throw new InvalidDataException("圖片不得超過 3,200 萬像素，且任一邊不得超過 16,384 像素。");

            cancellationToken.ThrowIfCancellationRequested();
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            pixels = new byte[checked(width * height * 4)];
            converted.CopyPixels(pixels, checked(width * 4), 0);
        }

        // Pixel-buffer rendering works on a thread-pool thread; no WPF visual/STA is required.
        var frames = new List<byte[]>(IconSizes.Length);
        foreach (var size in IconSizes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var iconPixels = ResizeWithPadding(pixels, width, height, orientation, size, cancellationToken);
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, iconPixels, size * 4);
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream();
            encoder.Save(png);
            frames.Add(png.ToArray());
        }

        var directory = Path.GetDirectoryName(sourcePath)!;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var temporaryPath = Path.Combine(directory, $".toolkeeper-{Guid.NewGuid():N}.tmp");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)IconSizes.Length);
                var offset = 6 + IconSizes.Length * 16;
                for (var index = 0; index < IconSizes.Length; index++)
                {
                    var size = IconSizes[index];
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(frames[index].Length);
                    writer.Write(offset);
                    offset += frames[index].Length;
                }
                foreach (var frame in frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.Write(frame);
                }
                writer.Flush();
                output.Flush(flushToDisk: true);
            }

            for (var suffix = 0; suffix < 10_000; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filename = suffix == 0 ? $"{stem}.ico" : $"{stem} ({suffix}).ico";
                var outputPath = Path.Combine(directory, filename);
                try
                {
                    // File.Move has no overwrite: a racing converter also receives a new name.
                    File.Move(temporaryPath, outputPath);
                    return new IconConversionResult(sourcePath, outputPath);
                }
                catch (IOException) when (File.Exists(outputPath) || Directory.Exists(outputPath))
                {
                    // Try the next suffix, keeping the complete temporary file intact.
                }
            }
            throw new IOException("找不到可用的 ICO 檔名，請先整理同名輸出檔案。");
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static ushort ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata)
            {
                var value = metadata.GetQuery("/app1/ifd/{ushort=274}");
                if (value is ushort orientation && orientation is >= 1 and <= 8) return orientation;
            }
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // Optional unsupported/malformed metadata must not prevent converting valid pixels.
        }
        return 1;
    }

    private static byte[] ResizeWithPadding(byte[] source, int width, int height, ushort orientation, int size, CancellationToken cancellationToken)
    {
        var orientedWidth = orientation >= 5 ? height : width;
        var orientedHeight = orientation >= 5 ? width : height;
        var scale = (double)size / Math.Max(orientedWidth, orientedHeight);
        var targetWidth = Math.Max(1, (int)Math.Round(orientedWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(orientedHeight * scale));
        var left = (size - targetWidth) / 2;
        var top = (size - targetHeight) / 2;
        var output = new byte[size * size * 4];
        for (var y = 0; y < targetHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var orientedY = Math.Clamp((y + 0.5) * orientedHeight / targetHeight - 0.5, 0, orientedHeight - 1);
            for (var x = 0; x < targetWidth; x++)
            {
                var orientedX = Math.Clamp((x + 0.5) * orientedWidth / targetWidth - 0.5, 0, orientedWidth - 1);
                // Map the displayed EXIF orientation back to source pixels without a second large buffer.
                var (sourceX, sourceY) = orientation switch
                {
                    2 => (width - 1 - orientedX, orientedY),
                    3 => (width - 1 - orientedX, height - 1 - orientedY),
                    4 => (orientedX, height - 1 - orientedY),
                    5 => (orientedY, orientedX),
                    6 => (orientedY, height - 1 - orientedX),
                    7 => (width - 1 - orientedY, height - 1 - orientedX),
                    8 => (width - 1 - orientedY, orientedX),
                    _ => (orientedX, orientedY)
                };
                var x0 = (int)sourceX;
                var x1 = Math.Min(x0 + 1, width - 1);
                var fx = sourceX - x0;
                var y0 = (int)sourceY;
                var y1 = Math.Min(y0 + 1, height - 1);
                var fy = sourceY - y0;
                var destination = ((y + top) * size + x + left) * 4;
                var p00 = (y0 * width + x0) * 4;
                var p10 = (y0 * width + x1) * 4;
                var p01 = (y1 * width + x0) * 4;
                var p11 = (y1 * width + x1) * 4;
                var w00 = (1 - fx) * (1 - fy);
                var w10 = fx * (1 - fy);
                var w01 = (1 - fx) * fy;
                var w11 = fx * fy;
                var alpha = source[p00 + 3] * w00 + source[p10 + 3] * w10 + source[p01 + 3] * w01 + source[p11 + 3] * w11;
                output[destination + 3] = (byte)Math.Clamp((int)Math.Round(alpha), 0, 255);
                if (alpha <= 0) continue;
                // Interpolate premultiplied colors, avoiding fringes from invisible RGB values.
                for (var channel = 0; channel < 3; channel++)
                {
                    var color = source[p00 + channel] * source[p00 + 3] * w00
                        + source[p10 + channel] * source[p10 + 3] * w10
                        + source[p01 + channel] * source[p01 + 3] * w01
                        + source[p11 + channel] * source[p11 + 3] * w11;
                    output[destination + channel] = (byte)Math.Clamp((int)Math.Round(color / alpha), 0, 255);
                }
            }
        }
        return output;
    }
}
