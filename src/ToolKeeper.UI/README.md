# ToolKeeper.UI 開發指南

`ToolKeeper.UI` 是 `net10.0-windows` WPF 類別庫，提供全系列的共用介面骨架、視窗操作、樣式、竹材／紙紋、字型、文字陰影及偏好元件。ToolKeeper 本體及 001–005 都使用同一 `WindowFrame`。未特別指定時，以工具番本體現有外觀為預設標準，見[工具番介面標準](../../docs/UI-STANDARD.md)。

一般工具透過 `AppWindow` 取得標準布局；汗青把文件分頁、側欄及編輯／預覽布局組合到同一骨架。產品各自保存設定與功能狀態，切換外觀不重建工作區。只引用組件不會自動改變其他視窗。

## 使用共用視窗

`AppWindow` 使用 `WindowFrame` 作為 `Content`，自行合併 `UiStyles.xaml`。完整 `MainName - SubName` 顯示於共用 caption，內容區保留簡介、三個偏好入口及可選 `HeaderActions`，產品內容放入 `Workspace`。不再額外顯示一個大型產品名稱。產品可訂閱 `UiPreferencesChanged`，使用 `ResolvedLanguage` 或 `T(en, zh, ja)` 原位更新文案；以 `DynamicResource` 引用共用色彩，避免切換時重建表格、輸入區或結果物件。

```csharp
// 在衍生視窗初始化完成後設定；實際路徑由產品自己的資料目錄決定。
PreferencesPath = Path.Combine(productDataDirectory, "ui.json");
AboutAuthor = "不告訴你";
UiPreferencesChanged += (_, _) => UpdateProductLabels();
ApplyUiPreferences();
```

簡介與「風格／語言／關於」位於同一行，三個入口靠右排列；簡介可隨寬度換行，可選 `HeaderActions` 位於下一行左側。

`SelectedTheme` 預設 `Ink`（竹子亮色），`SelectedLanguage` 預設 `System`。風格包括亮色、深色、竹子（亮色）、竹子（深色）與跟隨系統；語言包括跟隨系統、英文、繁體中文與日文。竹子風格採用的傳統文字依本機字型與語言解析，技術內容可保留等寬字型。公開屬性或 `ApplyUiPreferences()` 可用於程式套用／測試；選單的使用者操作才會保存偏好。共用模組統一提供調色盤、竹材與紙紋；宿主負責把材質放到產品自己的功能配置中。

首次顯示視窗時，從 `PreferencesPath` 載入獨立的風格／語言 JSON；設為 `null` 就不讀寫設定。載入無效值回到上述預設，保存以暫存檔後替換，與產品既有業務 JSON 分開。相同路徑的開啟視窗會同步，其他產品維持自己的偏好。系統風格的事件於顯示時訂閱、關閉時解除。

關於浮窗使用 `MainName`、`Description`、`Icon`、`AboutAuthor` 及衍生視窗所在組件的三段版本；作者未提供時不顯示作者行。Close、Esc、外側點擊以及宿主隱藏／關閉會收起浮窗。共用視窗提供 `SuccessBrush`／`ErrorBrush`，產品狀態文字也可跟隨深淺風格。

## 使用同一骨架提供不同布局

文件工具不必採用一般工具的 `AppWindow` 標頭，也不必改用其偏好儲存方式。以自己的 `Window` 建立共用骨架：

```csharp
var frame = new WindowFrame(this)
{
    CaptionContent = documentTabs,   // 可省略；汗青放文件分頁。
    CaptionActions = overflowButton, // 可省略；汗青放分頁溢出入口。
    Workspace = productLayout
};
Content = frame;

var language = UiLanguage.Resolve(Settings.Language);
UiAppearance.ApplyResources(Resources, Settings.Theme, language,
    Settings.UiFontFamily, Settings.UiFontSize,
    Settings.InterfaceTextShadowEnabled, Settings.InterfaceTextShadowThickness);
frame.ApplyMetrics(Settings.UiFontSize);
frame.ApplyLanguage(language);
```

`WindowFrame` 統一 caption、圖示、完整 `Window.Title`、最小化／最大化／還原／關閉、外框及螢幕工作區處理；`NoResize` 只顯示關閉，`CanMinimize` 禁止最大化。完整標題在產品最小尺寸內顯示，極窄自訂尺寸使用省略號並保留完整 tooltip；`CaptionContent`、`CaptionActions` 與 `Workspace` 僅承載產品內容。caption 控制使用 `WindowChrome` 的互動命中設定，空白處保留拖曳行為。關閉按鈕走 `Window.Close()`，不越過產品的 `Closing` 取消、隱藏或未儲存文件確認。

汗青的全螢幕模式用 `IsFullScreen`、`IsCaptionVisible`、`CaptionLeftInset` 與 `CaptionRevealRequested` 接上共用標題列；螢幕範圍、還原位置、側欄與文件狀態仍由汗青管理。不要為不同布局再複製一套標題列或視窗按鈕。

`UiAppearance.ApplyResources` 一次更新調色盤、竹材／紙紋、介面字型及 `ChromeTextShadow` 資源，只作用於指定字典。`ChromeTextShadow` 與 `Resources/ChromeTextStyles.xaml` 已從汗青移至 `ToolKeeper.UI`，保留既有陰影開關、厚度、資源 key 與繪製方式。閱讀和編輯字型不是視窗介面字型，仍由文件產品處理。

## 模組與宿主的責任

| 元件 | 共用模組提供 | 宿主產品負責 |
| --- | --- | --- |
| `WindowFrame` / `UiAppearance` | 共用視窗骨架、視窗操作及整套外觀資源 | 產品布局、偏好值、視窗與工作內容的生命週期 |
| UiLanguage | 語言解析、三語文字選擇、選單選項 | 產品文案、原始設定值、儲存與重新套用 |
| `UiTheme` | 主題判斷、調色盤與 WPF brush resource | 指定資源範圍、系統主題事件、產品專屬渲染器 |
| `BambooChrome` / `PaperTexture` | 以汗青為基準的竹材、竹節與紙紋，固定 DIP 拼貼 | 將材質接入外框、標題、工具列及工作面，保留產品配置 |
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

產品只需提供功能布局；共用 caption 及其插槽的 Chrome hit test 由 `WindowFrame` 處理。需要放進其他 caption 區域的自訂控制項，仍應明確設定 `WindowChrome.IsHitTestVisibleInChrome`。基本控制項與陰影模板集中於共用字典，避免產品逐一複製。

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
UiAppearance.ApplyResources(Resources, Settings.Theme, language,
    Settings.UiFontFamily, Settings.UiFontSize,
    Settings.InterfaceTextShadowEnabled, Settings.InterfaceTextShadowThickness);
SetResourceReference(FontFamilyProperty, "UiFontFamily");
SetResourceReference(FontSizeProperty, "UiFontSize");
SetResourceReference(BackgroundProperty, "ChromeBackgroundBrush");
SetResourceReference(ForegroundProperty, "TextBrush");
```

主題值為 `Light`、`Dark`、`Ink`、`InkDark`、`System`。竹子（亮色）／竹子（深色）沿用 `Ink`／`InkDark`，深色沿用 `Dark`，不變更既有偏好。英文顯示名稱為 `Bamboo Light`／`Bamboo Dark`，日文為「竹（ライト）」／「竹（ダーク）」。`System` 讀取 Windows 的應用程式明暗偏好；讀取失敗時使用亮色。`UiTheme.IsDark`／`ApplyResources` 的選用 `systemDark` 參數可讓宿主沿用已取得的結果，也方便測試。系統偏好變動的訂閱、Dispatcher 切換與關閉視窗時取消訂閱，仍由宿主處理。

共用元件保留以下資源名稱。前兩組由對應的 `ApplyResources` 更新，具名樣式由合併字典提供；色彩與尺寸可繼續以 `DynamicResource` 引用：

- 色彩：`WindowBackground`、`SurfaceBrush`、`TextBrush`、`MutedBrush`、`LineBrush`、`HoverBrush`、`AccentBrush`。
- 字體與尺寸：`UiFontFamily`、`UiFontSize`、`UiSmallFontSize`、`UiHeadingFontSize`、`UiSectionFontSize`、`UiMenuMaxWidth`。
- 具名樣式：`UiButtonStyle`、`FontPickerComboBoxStyle`、`FontPickerItemStyle`、`SidebarScrollViewerStyle`。

`UiTypography.ApplyResources` 將介面字級限制在 10–20；非有限值回到 16。空字體選項表示依語言與主題自動解析。竹子主題的自動字體及明確選擇 `InkTypography.FontChoice`（保存值 `@ink`）都會使用本機已安裝的楷體／明朝體等候選字體，再逐步 fallback；不下載或附帶字型檔。字體選項由宿主顯示為「傳統文字」／`Traditional`／「伝統書体」，明確選擇的一般字體名稱會保留。

閱讀字體可透過 `UiTypography.ResolveReadingFont(...)` 取得。共用模組不直接控制 AvalonEdit、WebView2 或 PDF；汗青自行把解析後的字體與風格旗標傳給編輯器與預覽。編輯器字體設定及文件文字內容也仍由汗青管理。

## 全系列竹材與紙紋

2026-10-01 起，本體及 001–005 的竹子風格比照汗青，共用 `BambooChrome` 與 `PaperTexture` 的材質。`Ink` 是淺色去皮竹材，保留淡竹節與纖維；`InkDark` 是深綠竹皮，保留明顯竹節與縱向纖維。兩者都搭配低對比紙紋工作面，不能只套用米色或綠色純色背景。

| 資源 | 竹子風格用途 | 一般亮／深／系統風格 |
| --- | --- | --- |
| `ChromeBackgroundBrush` / `ChromeRailBrush` | 縱向竹紋背景、側緣及外框 | `WindowBackground` 純色 |
| `ChromeTitleBrush` | 橫向竹紋標題區 | `WindowBackground` 純色 |
| `ChromeToolbarBrush` | 橫向竹紋工具列 | `SurfaceBrush` 純色 |
| `ChromeTabBrush` | 配合竹材的分頁漸層 | `SurfaceBrush` 純色 |
| `PaperBackgroundBrush` | 工作區底面與功能卡片的紙紋 | `SurfaceBrush` 純色 |

`WindowFrame` 的標題列及外框直接使用共用竹紋，`AppWindow` 的工作面和產品功能卡片使用紙紋；輸入框與表格內層可保留配套低對比純色，避免紋理影響內容可讀性。汗青也使用同一骨架及材質；WebView2 的 HTML 紙紋仍由汗青渲染器維護。

`BambooChrome` 與 `PaperTexture` 的命名空間皆為 `ToolKeeper.UI`。使用自訂視窗時，先套用基礎調色盤，再更新材質資源；每次切換都執行兩步，才能在離開竹子風格時清除舊材質：

```csharp
UiTheme.ApplyResources(Resources, Settings.Theme);
BambooChrome.ApplyResources(Resources,
    UiTheme.IsInk(Settings.Theme), UiTheme.IsDark(Settings.Theme));
SetResourceReference(BackgroundProperty, "ChromeBackgroundBrush");
```

材質使用已凍結的 WPF `DrawingBrush`。竹紋的縱向拼貼為 152 × 384 DIP，橫向拼貼旋轉為 384 × 152 DIP；紙紋拼貼為 421 × 347 DIP。`ViewportUnits` 與 `ViewboxUnits` 都使用 `Absolute`，以固定大小重複紋理；不要把整塊材質拉伸成視窗大小。改變視窗大小、顯示比例或群組展開尺寸時，竹節與纖維仍保持相同 DIP 比例。

CabiDock 桌面群組與群組預覽使用同一套材質，外框與標題涵蓋一般分類及「工具番」群組。同步來源仍是 CabiDock 自己的 UI 偏好，不能為了統一材質改讀其他產品的設定檔。本體與 004／005 保留既有共用 `ui.json` 的同步範圍；汗青與 ConvAnvil 仍各自保存偏好。

接入或修改材質後，檢查竹亮、竹深及返回一般風格的資源替換，確認紋理不拉伸、文字可讀，並確認切換時保留輸入、結果與產品操作狀態。建置或離屏渲染通過不等同於完成真實桌面及多 DPI 的人工驗收。

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

共用內容保留產品名稱、版本、品牌、簡介與作者版面；只翻譯「作者」標籤與關閉按鈕。點擊關閉或按 Esc 會呼叫 `CloseAbout`。汗青使用自己的 `ShowFlyout` 處理 Popup、尺寸、選單滑鼠擷取釋放與焦點，共用骨架不接管文件工具的浮層狀態。

## 相容性邊界

汗青的 JSON 設定欄位、設定目錄與已保存的語言／主題／字體值維持原樣，設定驗證與遷移留在汗青。後續一般工具使用 `AppWindow`；需要不同布局的主視窗組合 `WindowFrame`，不必共用設定檔或繼承一般工具的偏好管理。CabiDock 嵌入桌面的群組保持群組布局，僅接入適用的共用材質與元件。共用 API 的責任是介面呈現；文件、編輯、儲存、復原與各工具的業務流程仍屬於各產品。

## 本次接入驗證

2026-09-28 共用標題行與三個工具完整接入：整份 solution 的 430 項測試通過（共用 UI 35、ToolKeeper 55、ConvAnvil 117、CabiDock 129、汗青 94）。真實 WPF 隔離視窗的 810 項檢查通過，涵蓋實際選單、三語／五種風格、同產品同步、跨產品隔離、保存／重開、About 關閉／Esc／隱藏及操作內容保留。最小尺寸截圖已目視檢查。紀錄及 27 張截圖見 `artifacts/shared-header-checks`；三個 framework-dependent Release 資料夾在 `artifacts/shared-header-apps`。

2026-10-01 全系列竹皮接入：650 項測試完成（共用 UI 38、汗青 170、ToolKeeper 157、ConvAnvil 117、CabiDock 168），涵蓋材質固定比例、主題回切、資源隔離、未儲存內容／結果保留及群組換宿主後的原位切換。CabiDock 並行執行曾出現原生區域檢查偶發失敗，與既有測試記載的 WPF DPI 初始化競態相符；將 `DesktopVisibilityAuditTests`／`DesktopRecoveryGuardTests` 與其餘測試分開執行後，16／16 及 152／152 通過，未改動其產品邏輯或斷言。

五個真正 `AppWindow` 的繁中竹亮、竹深與一般亮／深回切共 200 項離屏檢查通過，產生 20 張最小尺寸畫面；五視窗的亮／深竹材與兩張桌面群組材質畫面已目視核對。測試紀錄與離屏驗證程式位於 `artifacts/series-bamboo-checks`，群組畫面位於 `artifacts/cabidock-ui/bamboo-group-Ink.png` 與 `bamboo-group-InkDark.png`。未操作真實 Explorer 接管，未完成實機多 DPI 人工驗收。
### 2026-10-01 共用介面骨架抽取

一般工具與汗青主窗均使用同一 `WindowFrame`，汗青授權檢查、CabiDock 群組預覽及重新命名視窗也已接入；桌面內嵌群組維持群組布局。Release solution 建置成功，656 項測試通過：共用 UI 44、汗青 170、ToolKeeper 157、ConvAnvil 117、CabiDock 168（原生區域測試仍分組執行）。新加入的自動高度測試曾因實機 DPI 取整出現 0.667 DIP 差距，已依每側最多一實體像素修正斷言，產品尺寸未因測試放大。

隔離驗證共 1,056 項通過：主視窗三語／五種風格與輔助視窗布局 982 項、汗青標題／分頁／字級與 F11 還原 65 項、真實 HWND 的拖曳與縮放命中、最小化、最大化工作區、還原及取消關閉 9 項。驗證程式與結果分別位於 `artifacts/shared-frame-checks`、`artifacts/hanqing-frame-checks`、`artifacts/frame-native-checks`。使用隔離狀態與離屏或透明測試視窗；沒有操作實際桌面接管或重新啟動使用者正在執行的 Debug 程式。畫面已抽樣目視核對；尚未做跨實體螢幕 DPI 切換與人工 Snap Layout 驗收。
