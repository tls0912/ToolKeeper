using System.Windows.Media;

namespace ToolKeeper.UI;

/// <summary>Product metadata supplied by the application, with localized description and brand text.</summary>
public sealed record AboutInfo(
    string ProductName,
    string Version,
    string Description,
    string Author,
    string Brand = "ToolKeeper",
    ImageSource? Icon = null);
