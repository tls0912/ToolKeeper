# ToolKeeper.UI 開發指南

`ToolKeeper.UI` 是 `net10.0-windows` WPF 類別庫，提供可由產品選用的介面元件。汗青在自訂視窗中使用語言、主題、字體與「關於」元件；ToolKeeper 本體、CabiDock、ConvAnvil 則使用 `AppWindow`，共用標題行右側的「風格、語言、關於」入口。

三個工具的功能區已跟隨五種風格與中英日語言切換，保留使用中的輸入與結果。汗青保留自訂標題列、文件分頁與 `Window` 基底，**沒有改為繼承 `AppWindow`**。只引用此類別庫不會自動改變其他視窗的樣式。

## 使用共用視窗

`AppWindow` 自行合併 `UiStyles.xaml`，將主標題與三個偏好按鈕放在第一列，簡介與 `HeaderActions` 放在第二列，產品內容放入 `Workspace`。產品可訂閱 `UiPreferencesChanged`，使用 `ResolvedLanguage` 或 `T(en, zh, ja)` 原位更新文案；以 `DynamicResource` 引用共用色彩，避免切換時重建表格、輸入區或結果物件。

```csharp
// 在衍生視窗初始化完成後設定；實際路徑由產品自己的資料目錄決定。
PreferencesPath = Path.Combine(productDataDirectory, "ui.json");
AboutAuthor = "不告訴你";
UiPreferencesChanged += (_, _) => UpdateProductLabels();
ApplyUiPreferences();
```

`SelectedTheme` 預設 `Light`，`SelectedLanguage` 預設 `System`。風格包括亮色、深色、竹子（亮色）、竹子（深色）與跟隨系統；語言包括跟隨系統、英文、繁體中文與日文。竹子風格採用的傳統文字依本機字型與語言解析，技術內容可保留等寬字型。公開屬性或 `ApplyUiPreferences()` 可用於程式套用／測試；選單的使用者操作才會保存偏好。共用模組提供調色盤與名稱，竹子裝飾、宣紙背景等產品視覺由宿主維護。

首次顯示視窗時，從 `PreferencesPath` 載入獨立的風格／語言 JSON；設為 `null` 就不讀寫設定。載入無效值回到上述預設，保存以暫存檔後替換，與產品既有業務 JSON 分開。相同路徑的開啟視窗會同步，其他產品維持自己的偏好。系統風格的事件於顯示時訂閱、關閉時解除。

關於浮窗使用 `MainName`、`Description`、`Icon`、`AboutAuthor` 及衍生視窗所在組件的三段版本；作者未提供時不顯示作者行。Close、Esc、外側點擊以及宿主隱藏／關閉會收起浮窗。共用視窗提供 `SuccessBrush`／`ErrorBrush`，產品狀態文字也可跟隨深淺風格。

## 模組與宿主的責任

| 元件 | 共用模組提供 | 宿主產品負責 |
| --- | --- | --- |
| `UiLanguage` | 語言解析、三語文字選擇、選單選項 | 產品文案、原始設定值、儲存與重新套用 |
| `UiTheme` | 主題判斷、調色盤、七個 WPF brush resource | 指定資源範圍、系統主題事件、產品專屬裝飾與渲染器 |
| `UiTypography` / `InkTypography` | 介面／閱讀字體解析、傳統文字 fallback、字級資源 | 字體選項與尺寸設定、編輯器／WebView2 的套用 |
| `PreferenceMenus` | 有勾選狀態的語言與主題 `MenuItem` | 選單承載、選擇後的保存、視窗更新與錯誤回報 |
| `AboutInfo` / `AboutContent` | 共用資訊模型與內容版面、關閉按鈕與 Esc | 產品名稱、版本、作者、已翻譯簡介、Popup／Window 與焦點 |

模組不持有跨工具共用的 settings，也不建立目前語言、主題或視窗的全域狀態。`ApplyResources` 只修改呼叫者傳入的 `ResourceDictionary`；產品各自保留設定服務、儲存位置與多視窗同步方式。

## 明確接入樣式

在產品專案加入參考：

```xml
<ProjectReference Include="..\ToolKeeper.UI\ToolKeeper.UI.csproj" />
```

由需要樣式的視窗明確合併字典：

```xml
<Window.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="/ToolKeeper.UI;component/Resources/UiStyles.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Window.Resources>
```

`UiStyles.xaml` 包含預設亮色調色盤、`Button`／`TextBox` 樣式，並合併 `MenuStyles.xaml` 與 `SidebarScrollStyles.xaml`。其中包含選單、字體選擇器與具名捲軸樣式；只有合併字典的資源範圍會採用這些樣式。

共用按鈕的基礎樣式名稱為 `UiButtonStyle`，字典另以隱含樣式套用到 `Button`。需要客製按鈕時，明確以這個具名樣式為基礎，避免與宿主的隱含樣式同名查找。

例如汗青的自訂標題列需要 Chrome hit test，可在合併字典後加入：

```xml
<Style TargetType="Button" BasedOn="{StaticResource UiButtonStyle}">
  <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True" />
</Style>
```

上述 `shell` 對應 `clr-namespace:System.Windows.Shell;assembly=PresentationFramework`。共用樣式本身沒有此設定，一般原生視窗不需要加入。產品專屬外框、標題列、內容配置與竹子裝飾仍由產品維護。

## 解析語言並保留 System

以下 C# 片段使用 `ToolKeeper.UI`，並假設宿主已有自己的 `Settings`、設定保存與重新套用方法。

```csharp
var language = UiLanguage.Resolve(Settings.Language);
var label = UiLanguage.Text(language, "Theme", "主題", "テーマ");
```

可保存的語言值為 `System`、`en`、`zh-TW`、`ja`。`Resolve` 預設讀取 `CultureInfo.CurrentUICulture.Name`，也可傳入第二個參數指定系統語言供測試。系統語言為 `zh-TW`、`zh-HK`、`zh-MO` 或 `zh-Hant*` 時解析為繁體中文，`ja*` 解析為日文，其餘為英文。

`Settings.Language` 必須保留使用者選擇的原始值；例如 `System` 解析成 `zh-TW` 後，仍保存 `System`。把解析後的值傳給 `Text`、字體解析與選項標籤，把原始值傳給選單的 `selected` 參數，才能正確勾選「跟隨系統」。模組不修改執行緒文化設定。

## 套用主題與字體

在宿主的 WPF UI 執行緒，對該視窗的資源套用設定：

```csharp
var language = UiLanguage.Resolve(Settings.Language);
var ink = UiTheme.IsInk(Settings.Theme);
UiTheme.ApplyResources(Resources, Settings.Theme);

var family = new System.Windows.Media.FontFamily(
    UiTypography.ResolveInterfaceFont(Settings.UiFontFamily, language, ink));
UiTypography.ApplyResources(Resources, family, Settings.UiFontSize);
SetResourceReference(FontFamilyProperty, "UiFontFamily");
SetResourceReference(FontSizeProperty, "UiFontSize");
SetResourceReference(BackgroundProperty, "WindowBackground");
SetResourceReference(ForegroundProperty, "TextBrush");
```

主題值為 `Light`、`Dark`、`Ink`、`InkDark`、`System`。竹子（亮色）／竹子（深色）沿用 `Ink`／`InkDark`，深色沿用 `Dark`，不變更既有偏好。英文顯示名稱為 `Bamboo Light`／`Bamboo Dark`，日文為「竹（ライト）」／「竹（ダーク）」。`System` 讀取 Windows 的應用程式明暗偏好；讀取失敗時使用亮色。`UiTheme.IsDark`／`ApplyResources` 的選用 `systemDark` 參數可讓宿主沿用已取得的結果，也方便測試。系統偏好變動的訂閱、Dispatcher 切換與關閉視窗時取消訂閱，仍由宿主處理。

共用元件保留以下資源名稱。前兩組由對應的 `ApplyResources` 更新，具名樣式由合併字典提供；色彩與尺寸可繼續以 `DynamicResource` 引用：

- 色彩：`WindowBackground`、`SurfaceBrush`、`TextBrush`、`MutedBrush`、`LineBrush`、`HoverBrush`、`AccentBrush`。
- 字體與尺寸：`UiFontFamily`、`UiFontSize`、`UiSmallFontSize`、`UiHeadingFontSize`、`UiSectionFontSize`、`UiMenuMaxWidth`。
- 具名樣式：`UiButtonStyle`、`FontPickerComboBoxStyle`、`FontPickerItemStyle`、`SidebarScrollViewerStyle`。

`UiTypography.ApplyResources` 將介面字級限制在 10–20；非有限值回到 16。空字體選項表示依語言與主題自動解析。竹子主題的自動字體及明確選擇 `InkTypography.FontChoice`（保存值 `@ink`）都會使用本機已安裝的楷體／明朝體等候選字體，再逐步 fallback；不下載或附帶字型檔。字體選項由宿主顯示為「傳統文字」／`Traditional`／「伝統書体」，明確選擇的一般字體名稱會保留。

閱讀字體可透過 `UiTypography.ResolveReadingFont(...)` 取得。共用模組不直接控制 AvalonEdit、WebView2 或 PDF；汗青自行把解析後的字體與風格旗標傳給編輯器與預覽。編輯器字體設定及文件文字內容也仍由汗青管理。

## 建立偏好選單

```csharp
var language = UiLanguage.Resolve(Settings.Language);
var languageMenu = new System.Windows.Controls.MenuItem
{
    Header = UiLanguage.Text(language, "Language", "語言", "言語")
};
PreferenceMenus.AddLanguageChoices(languageMenu.Items, Settings.Language, language,
    value =>
    {
        Settings.Language = value;
        SaveAndApplyPreferences(); // 宿主提供：保存自己的設定並更新自己的視窗。
    }, ReportError);

var themeMenu = new System.Windows.Controls.MenuItem
{
    Header = UiLanguage.Text(language, "Theme", "主題", "テーマ")
};
PreferenceMenus.AddThemeChoices(themeMenu.Items, Settings.Theme, language,
    value =>
    {
        Settings.Theme = value;
        SaveAndApplyPreferences();
    }, ReportError);
```

`SaveAndApplyPreferences` 與 `ReportError(Exception)` 是宿主提供的方法，不是共用 API。選單建立時依 `selected` 勾選；使用者點擊時，元件將 routed click 標記為已處理，並將選項值傳給 callback。callback 例外交給選用的 `onError`；未提供時例外繼續拋出。元件不自行保存設定，也不更新其他視窗。重新建立選單時應傳入最新設定與解析後的語言。

`ContextMenu` 與 `Popup` 位於獨立視覺樹。宿主須讓其承載元素使用該視窗的資源（例如 `menu.Resources = Resources`），並處理顯示位置與生命週期；不要為此修改其他產品的全域資源。

## 承載關於內容

`AboutInfo` 的版本必須由產品自己的組件提供，不能使用 `ToolKeeper.UI` 的版本。作者與已翻譯的簡介／品牌文字也由產品傳入，例如汗青：

```csharp
var language = UiLanguage.Resolve(Settings.Language);
var info = new AboutInfo(
    ProductName: "汗青",
    Version: typeof(App).Assembly.GetName().Version?.ToString(3) ?? "",
    Description: UiLanguage.Text(language,
        "Offline Markdown reader and editor.",
        "離線 Markdown 閱讀與編輯工具。",
        "オフライン Markdown リーダー・エディター。"),
    Author: "不告訴你",
    Brand: UiLanguage.Text(language, "ToolKeeper", "工具番 · ToolKeeper", "ToolKeeper"),
    Icon: Icon);

var body = AboutContent.Create(info, language, CloseAbout, Settings.UiFontSize);
```

`CloseAbout` 是宿主關閉自身 Popup／Window 的 callback。`Create` 回傳 `FrameworkElement`（內容為 `StackPanel`），不會開啟視窗。宿主將 `body` 放入自己的 Popup 或 Window，必要時包上 `ScrollViewer`，設定可用高度、邊框與共用資源，再於顯示後將焦點放到 `(Button)body.FindName("AboutCloseButton")`。

共用內容保留產品名稱、版本、品牌、簡介與作者版面；只翻譯「作者」標籤與關閉按鈕。點擊關閉或按 Esc 會呼叫 `CloseAbout`。汗青繼續使用自己的 `ShowFlyout` 處理 Popup、尺寸、選單滑鼠擷取釋放與焦點，沒有更換宿主視窗架構。

## 相容性邊界

汗青的 JSON 設定欄位、設定目錄與已保存的語言／主題／字體值維持原樣，設定驗證與遷移留在汗青。其他工具後續接入時，可以只選用所需 API 和資源，不需要共用設定檔或改為繼承 `AppWindow`。共用 API 的責任是介面呈現；文件、編輯、儲存、復原與各工具的業務流程仍屬於各產品。

## 本次接入驗證

2026-09-28 共用標題行與三個工具完整接入：整份 solution 的 430 項測試通過（共用 UI 35、ToolKeeper 55、ConvAnvil 117、CabiDock 129、汗青 94）。真實 WPF 隔離視窗的 810 項檢查通過，涵蓋實際選單、三語／五種風格、同產品同步、跨產品隔離、保存／重開、About 關閉／Esc／隱藏及操作內容保留。最小尺寸截圖已目視檢查。紀錄及 27 張截圖見 `artifacts/shared-header-checks`；三個 framework-dependent Release 資料夾在 `artifacts/shared-header-apps`。
