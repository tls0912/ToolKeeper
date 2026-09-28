using System.IO;

namespace ToolKeeper;

public partial class MainWindow
{
    private void InitializeProductPreferences()
    {
        PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolKeeper", "ToolKeeper", "ui.json");
        AboutAuthor = "不告訴你";
        Products.ItemsSource = _products;
        UiPreferencesChanged += (_, _) => UpdateProductPreferences();
        ApplyUiPreferences();
    }

    private void UpdateProductPreferences()
    {
        Description = T("Your desktop and tools, in one place.", "桌面整理與工具入口，一處掌握。", "デスクトップとツールをひとつに。");
        Resources["ToolKeeper.ToolsTitle"] = T("Tools", "工具列表", "ツール一覧");
        Resources["ToolKeeper.ToolsCatalog"] = T("Built-in modules and apps", "內建模組與應用程式", "内蔵モジュールとアプリ");
        foreach (var product in _products) product.Translate(ResolvedLanguage);
    }
}
