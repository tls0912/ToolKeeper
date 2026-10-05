using ToolKeeper.UI;

namespace MarkPad;

public partial class MainWindow
{
    private void ApplyInterfaceTextShadow() => ChromeTextShadow.ApplyResources(
        Resources, _dark, Settings.InterfaceTextShadowEnabled, Settings.InterfaceTextShadowThickness);
}
