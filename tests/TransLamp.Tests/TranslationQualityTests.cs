using TransLamp.Core;
using Xunit;

namespace TransLamp.Tests;

public sealed class TranslationQualityTests
{
    [Theory]
    [InlineData("Device D100 did not respond within 3000 ms.", "設備 D100 在 30000 ms 內沒有回應。", "3000")]
    [InlineData("Port 502 did not respond.", "通訊埠沒有回應。", "502")]
    [InlineData("Server 192.168.1.10 did not respond.", "伺服器 192168.1.10 沒有回應。", "192.168.1.10")]
    [InlineData("Connect to 192.168.1.10:502.", "連接 192.168.1.10:503。", "192.168.1.10:502")]
    [InlineData("Connect to [2001:db8::1]:502.", "連接 [2001:db8::1]:503。", "[2001:db8::1]:502")]
    [InlineData("Set %MW100 to zero.", "將 %MW10 設為零。", "%MW100")]
    [InlineData("Check DB1.DBX0.1.", "檢查 DB1.DBX0.2。", "DB1.DBX0.1")]
    [InlineData("Error 0x1F.", "錯誤 0x2F。", "0x1F")]
    [InlineData("Value 16#FF.", "數值 16#FE。", "16#FF")]
    [InlineData("Firmware v1.2.3.", "韌體 v1.2.4。", "v1.2.3")]
    [InlineData("Set retry_count to ten.", "將 重試_計數 設為十。", "retry_count")]
    [InlineData("Logs are at C:\\Logs\\alarm_2026.txt.", "日誌位於 C:\\Logs\\alarm_2026 .txt。", "C:\\Logs\\alarm_2026.txt")]
    [InlineData("Read https://example.test/api/v1?retry=3.", "請讀取 https://example.test/api/v1?retry=4。", "https://example.test/api/v1?retry=3")]
    [InlineData("Use /var/log/alarm_2026.txt.", "使用 /var/log/alarm_2027.txt。", "/var/log/alarm_2026.txt")]
    [InlineData("Use \"C:\\Program Files\\ToolKeeper\\alarm.txt\".", "使用 \"C:\\Program Files\\ToolKeeper\\alarm2.txt\"。", "C:\\Program Files\\ToolKeeper\\alarm.txt")]
    [InlineData("Wait 3000 ms.", "等待 3,000 ms。", "3000")]
    [InlineData("Offset -3.", "偏移量 3。", "-3")]
    [InlineData("Ratio 3/4.", "比例 3/5。", "3/4")]
    public void FlagsMissingOrChangedSourceLiterals(string source, string translation, string literal)
    {
        var difference = Assert.Single(TranslationQualityCheck.FindChanges(source, translation));
        Assert.Equal(literal, difference.Literal);
        Assert.Equal(1, difference.SourceOccurrences);
        Assert.Equal(0, difference.TranslationOccurrences);
    }

    [Theory]
    [InlineData("3000")]
    [InlineData("13")]
    [InlineData("D3")]
    [InlineData("3.0")]
    [InlineData("192.168.1.3")]
    public void StandaloneNumberMustBePreservedAsAWholeLiteral(string translation)
    {
        Assert.Equal("3", Assert.Single(TranslationQualityCheck.FindChanges("Try 3 times.", translation)).Literal);
    }

    [Fact]
    public void ChecksRepeatedOccurrencesAndIgnoresAdditionalNumbers()
    {
        var difference = Assert.Single(TranslationQualityCheck.FindChanges("Retry 3 times, then 3 more times.", "重試 3 次，再試 3000 次。"));
        Assert.Equal("3", difference.Literal);
        Assert.Equal(2, difference.SourceOccurrences);
        Assert.Equal(1, difference.TranslationOccurrences);
        Assert.Empty(TranslationQualityCheck.FindChanges("Retry 3 times.", "重試 3 次；錯誤 42。"));
    }

    [Theory]
    [InlineData("Wait ten seconds.", "等待 10 秒。")]
    [InlineData("The motor did not respond.", "馬達沒有回應。")]
    [InlineData("Device D100 needs 3000 ms.", "設備 D100 需要 3000 ms。")]
    [InlineData("Port 502 at 192.168.1.10.", "192.168.1.10 的通訊埠 502。")]
    [InlineData("Check retry_count twice.", "請檢查 retry_count 兩次。")]
    [InlineData("Read \"C:\\Program Files\\ToolKeeper\\alarm.txt\".", "讀取 \"C:\\Program Files\\ToolKeeper\\alarm.txt\"。")]
    public void DoesNotInferLiteralsFromWordsOrReportPreservedValues(string source, string translation) =>
        Assert.Empty(TranslationQualityCheck.FindChanges(source, translation));

    [Fact]
    public void PreservesBothTextsAndHandlesTheMaximumInputLength()
    {
        var source = new string('7', TranslationEngine.MaximumInputCharacters);
        var translation = source;
        Assert.Empty(TranslationQualityCheck.FindChanges(source, translation));
        Assert.Equal(source, translation);
        Assert.Equal(TranslationEngine.MaximumInputCharacters, source.Length);
    }
}
