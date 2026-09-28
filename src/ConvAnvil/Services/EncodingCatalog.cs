using System.Runtime.InteropServices;
using System.Text;

namespace ConvAnvil.Services;

public sealed record EncodingOption(string Id, string Name, int CodePage, bool EmitBom)
{
    public override string ToString() => Name;
}

public static class EncodingCatalog
{
    static EncodingCatalog()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        WindowsAnsi = new EncodingOption("windows-ansi", $"Windows ANSI（CP {GetACP()}）", (int)GetACP(), false);
        All = Array.AsReadOnly(new[]
        {
            new EncodingOption("utf-8", "UTF-8（無 BOM）", 65001, false),
            new EncodingOption("utf-8-bom", "UTF-8（有 BOM）", 65001, true),
            new EncodingOption("utf-16le", "UTF-16 LE（有 BOM）", 1200, true),
            new EncodingOption("utf-16le-no-bom", "UTF-16 LE（無 BOM）", 1200, false),
            new EncodingOption("utf-16be", "UTF-16 BE（有 BOM）", 1201, true),
            new EncodingOption("utf-16be-no-bom", "UTF-16 BE（無 BOM）", 1201, false),
            new EncodingOption("utf-32le", "UTF-32 LE（有 BOM）", 12000, true),
            new EncodingOption("utf-32le-no-bom", "UTF-32 LE（無 BOM）", 12000, false),
            new EncodingOption("utf-32be", "UTF-32 BE（有 BOM）", 12001, true),
            new EncodingOption("utf-32be-no-bom", "UTF-32 BE（無 BOM）", 12001, false),
            WindowsAnsi,
            new EncodingOption("big5", "Big5（CP 950）", 950, false),
            new EncodingOption("shift-jis", "Shift_JIS（CP 932）", 932, false),
            new EncodingOption("gb18030", "GB18030（CP 54936）", 54936, false),
            new EncodingOption("gbk", "GBK（CP 936）", 936, false),
            new EncodingOption("windows-1252", "Windows-1252", 1252, false),
            new EncodingOption("ascii", "ASCII", 20127, false)
        });
    }

    public static IReadOnlyList<EncodingOption> All { get; }
    public static EncodingOption Default => All[0];
    public static EncodingOption WindowsAnsi { get; }

    public static EncodingOption? Find(string id) => All.FirstOrDefault(item => item.Id == id);

    public static EncodingOption? FindByCodePage(int codePage, bool emitBom = false) =>
        All.FirstOrDefault(item => item.CodePage == codePage && item.EmitBom == emitBom);

    public static Encoding GetEncoding(EncodingOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.CodePage switch
        {
            65001 => new UTF8Encoding(option.EmitBom, true),
            1200 => new UnicodeEncoding(false, option.EmitBom, true),
            1201 => new UnicodeEncoding(true, option.EmitBom, true),
            12000 => new UTF32Encoding(false, option.EmitBom, true),
            12001 => new UTF32Encoding(true, option.EmitBom, true),
            _ => Encoding.GetEncoding(option.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        };
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();
}
