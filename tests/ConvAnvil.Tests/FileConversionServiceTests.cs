using System.IO;
using System.Text;
using ConvAnvil.Services;
using Xunit;

namespace ConvAnvil.Tests;

public sealed class FileConversionServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ConvAnvil.Tests", Guid.NewGuid().ToString("N"));

    public FileConversionServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task ReadProducesByteSnapshotAndDoesNotModifySource()
    {
        var path = Path.Combine(directory, "source.txt");
        byte[] original = [0xEF, 0xBB, 0xBF, 0x41, 0x0D, 0x0A];
        await File.WriteAllBytesAsync(path, original);
        var snapshot = await FileConversionService.ReadAsync(path);
        Assert.Equal(Path.GetFullPath(path), snapshot.Path);
        Assert.Equal(original, snapshot.Bytes);
        Assert.Equal("UTF-8", snapshot.Detection.BomName);
        await File.WriteAllTextAsync(path, "changed");
        Assert.Equal(original, snapshot.Bytes);
    }

    [Fact]
    public async Task SavePublishesCompleteConvertedBytesAndPreservesSource()
    {
        var source = Path.Combine(directory, "source.txt");
        var target = Path.Combine(directory, "converted.txt");
        await File.WriteAllTextAsync(source, "中文", new UTF8Encoding(false));
        var snapshot = await FileConversionService.ReadAsync(source);
        var text = EncodingConversionService.Decode(snapshot.Bytes, EncodingCatalog.Default);
        var converted = EncodingConversionService.Encode(text, EncodingCatalog.Find("big5")!);
        await FileConversionService.SaveNewAsync(target, converted, source);
        Assert.Equal("A4A4A4E5", Convert.ToHexString(await File.ReadAllBytesAsync(target)));
        Assert.Equal("E4B8ADE69687", Convert.ToHexString(await File.ReadAllBytesAsync(source)));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task ExistingDestinationIsNeverOverwritten()
    {
        var target = Path.Combine(directory, "existing.txt");
        await File.WriteAllTextAsync(target, "keep me");
        await Assert.ThrowsAsync<IOException>(() => FileConversionService.SaveNewAsync(target, [0x41]));
        Assert.Equal("keep me", await File.ReadAllTextAsync(target));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task SourcePathAliasIsRejectedEvenIfSourceWasRemovedAfterRead()
    {
        var source = Path.Combine(directory, "source.txt");
        var alias = Path.Combine(directory, ".", "SOURCE.TXT");
        await Assert.ThrowsAsync<IOException>(() => FileConversionService.SaveNewAsync(alias, [0x41], source));
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task OversizedFileIsRejectedWithoutChangingIt()
    {
        var path = Path.Combine(directory, "large.txt");
        await using (var file = File.Create(path)) file.SetLength(FileConversionService.MaxFileBytes + 1L);
        var exception = await Assert.ThrowsAsync<IOException>(() => FileConversionService.ReadAsync(path));
        Assert.Contains("16 MiB", exception.Message);
        Assert.Equal(FileConversionService.MaxFileBytes + 1L, new FileInfo(path).Length);
    }

    [Fact]
    public async Task ExactFileLimitIsAccepted()
    {
        var path = Path.Combine(directory, "limit.txt");
        await using (var file = File.Create(path)) file.SetLength(FileConversionService.MaxFileBytes);
        var snapshot = await FileConversionService.ReadAsync(path);
        Assert.Equal(FileConversionService.MaxFileBytes, snapshot.Bytes.Length);
    }

    [Fact]
    public async Task AlreadyCancelledReadAndSaveLeaveNoOutput()
    {
        var path = Path.Combine(directory, "source.txt");
        await File.WriteAllTextAsync(path, "A");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileConversionService.ReadAsync(path, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileConversionService.SaveNewAsync(Path.Combine(directory, "output.txt"), [0x41], path, cancelled.Token));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task InvalidDestinationDirectoryDoesNotCreatePartialOutput()
    {
        var target = Path.Combine(directory, "missing", "output.txt");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => FileConversionService.SaveNewAsync(target, [0x41]));
        Assert.Empty(Directory.GetFiles(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
