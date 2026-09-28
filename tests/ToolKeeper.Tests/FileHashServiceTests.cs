using System.IO;
using System.Security.Cryptography;
using System.Text;
using ToolKeeper.Services;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class FileHashServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ToolKeeper.HashTests", Guid.NewGuid().ToString("N"));

    public FileHashServiceTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Theory]
    [InlineData("", "D41D8CD98F00B204E9800998ECF8427E", "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709", "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    [InlineData("abc", "900150983CD24FB0D6963F7D28E17F72", "A9993E364706816ABA3E25717850C26C9CD0D89D", "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    public async Task ComputesStandardVectors(string content, string md5, string sha1, string sha256)
    {
        string path = WriteFile(Encoding.UTF8.GetBytes(content));

        FileHashResult result = await FileHashService.ComputeAsync(path);

        Assert.Equal(Path.GetFullPath(path), result.FilePath);
        Assert.Equal(content.Length, result.Length);
        Assert.Equal(md5, result.Md5);
        Assert.Equal(sha1, result.Sha1);
        Assert.Equal(sha256, result.Sha256);
    }

    [Fact]
    public async Task HashesBinaryFileAcrossManyBuffersAndReportsMonotonicProgress()
    {
        byte[] content = new byte[3 * 1024 * 1024 + 17];
        new Random(137).NextBytes(content);
        string path = WriteFile(content);
        var reports = new List<double>();

        FileHashResult result = await FileHashService.ComputeAsync(path, new InlineProgress(reports.Add));

        Assert.Equal(content.Length, result.Length);
        Assert.Equal(Convert.ToHexString(MD5.HashData(content)), result.Md5);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(content)), result.Sha1);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), result.Sha256);
        Assert.Equal(0d, reports[0]);
        Assert.Equal(1d, reports[^1]);
        Assert.Contains(reports, progress => progress is > 0 and < 1);
        Assert.All(reports, progress => Assert.InRange(progress, 0d, 1d));
        Assert.Equal(reports.OrderBy(progress => progress), reports);
    }

    [Fact]
    public async Task CancelsDuringReadingAndReleasesFileHandle()
    {
        string path = WriteFile(new byte[2 * 1024 * 1024]);
        using var cancellation = new CancellationTokenSource();
        var reports = new List<double>();
        var progress = new InlineProgress(value =>
        {
            reports.Add(value);
            if (value > 0)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileHashService.ComputeAsync(path, progress, cancellation.Token));

        Assert.DoesNotContain(1d, reports);
        using var exclusiveHandle = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusiveHandle.CanWrite);
    }

    [Fact]
    public async Task HonorsAlreadyCancelledTokenBeforeOpeningFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileHashService.ComputeAsync(Path.Combine(directory, "missing.bin"), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task RejectsFilesThatAreStillBeingWritten()
    {
        string path = WriteFile([1, 2, 3]);
        using var writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        await Assert.ThrowsAsync<IOException>(() => FileHashService.ComputeAsync(path));
    }

    [Fact]
    public async Task BlocksMutationUntilHashingCompletes()
    {
        string path = WriteFile(new byte[1024 * 1024]);
        bool attemptedWrite = false;
        var progress = new InlineProgress(value =>
        {
            if (value <= 0 || attemptedWrite)
            {
                return;
            }

            attemptedWrite = true;
            Assert.Throws<IOException>(() =>
            {
                using var writer = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            });
        });

        await FileHashService.ComputeAsync(path, progress);

        Assert.True(attemptedWrite);
        using var writerAfterCompletion = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
        Assert.True(writerAfterCompletion.CanWrite);
    }

    [Fact]
    public async Task ReportsMissingFile()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            FileHashService.ComputeAsync(Path.Combine(directory, "missing.bin")));
    }

    [Theory]
    [InlineData(32, "MD5")]
    [InlineData(40, "SHA-1")]
    [InlineData(64, "SHA-256")]
    public void MatchesExpectedHashIgnoringCaseAndSurroundingWhitespace(int length, string algorithm)
    {
        var result = new FileHashResult("file", 0, new string('A', 32), new string('B', 40), new string('C', 64));
        char character = length switch { 32 => 'a', 40 => 'b', _ => 'c' };
        string expected = " \r\n" + new string(character, length) + "\t ";

        Assert.True(FileHashService.IsValidExpectedHash(expected));
        Assert.Equal(algorithm, FileHashService.MatchAlgorithm(result, expected));
        Assert.Null(FileHashService.MatchAlgorithm(result, new string('F', length)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC123")]
    [InlineData("90015098 3CD24FB0D6963F7D28E17F72")]
    [InlineData("G00150983CD24FB0D6963F7D28E17F72")]
    [InlineData("９00150983CD24FB0D6963F7D28E17F72")]
    [InlineData("900150983CD24FB0D6963F7D28E17F72 file.txt")]
    public void RejectsMalformedExpectedHashes(string expected)
    {
        var result = new FileHashResult("file", 0, "900150983CD24FB0D6963F7D28E17F72", "", "");

        Assert.False(FileHashService.IsValidExpectedHash(expected));
        Assert.Null(FileHashService.MatchAlgorithm(result, expected));
    }

    private string WriteFile(byte[] content)
    {
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
