# ToolKeeper Architecture

## 目標

ToolKeeper 採用 **Monorepo + Standalone Products + Integrated Desktop Module**。

ToolKeeper 本體是平台與 **單一主執行入口**，負責工具目錄、Launcher、少量免費小工具，以及 CabiDock 桌面模組的生命週期與平台整合。

正式工具原則上仍可獨立建置、獨立上架，例如 **001 — 汗青**、**003 — ConvAnvil** 與未來產品。**002 — CabiDock 是例外**：其產品能力納入 ToolKeeper，作為 Desktop Experience / Desktop Layer，不再要求使用者啟動第二個常駐主程式。

目前 `src/CabiDock` 仍保留可獨立執行的 WPF App，供既有功能與 Explorer 桌面整合驗證；後續逐步將桌面掃描、分類、監看、Explorer 接管、桌面群組與 Recovery 整理為可由 ToolKeeper 引用的模組（例如 `ToolKeeper.Desktop`）。這是 **產品合體、程式模組化**，不是把 CabiDock 程式碼直接搬進 ToolKeeper 主視窗。

ToolKeeper、CabiDock 現有設定介面與 ConvAnvil 使用 `ToolKeeper.UI` 共用視窗元件；汗青保留自訂視窗並使用其中可獨立採用的語言、主題／字體、偏好選單與「關於」元件。

## Repository Structure

```text
ToolKeeper/
├─ docs/
│  ├─ PRODUCT_VISION.md
│  ├─ ARCHITECTURE.md
│  └─ products/
│     ├─ 001_MarkPad.md
│     ├─ 002_CabiDock.md
│     └─ 003_ConvAnvil.md
│
├─ src/
│  ├─ MarkPad/
│  │  ├─ Models/
│  │  ├─ Rendering/
│  │  ├─ Resources/
│  │  ├─ Services/
│  │  ├─ App.xaml
│  │  ├─ App.xaml.cs
│  │  ├─ MainWindow.xaml
│  │  ├─ MainWindow.xaml.cs
│  │  ├─ app.manifest
│  │  └─ MarkPad.csproj
│  ├─ CabiDock/              # 過渡期：既有 002 實作與獨立驗證入口
│  ├─ ToolKeeper/            # 最終單一主執行入口
│  ├─ ToolKeeper.Desktop/    # 目標模組；由 CabiDock 桌面能力逐步整理而來
│  ├─ ToolKeeper.UI/
│  └─ ConvAnvil/
│
├─ tests/
│  ├─ MarkPad.Tests/
│  ├─ CabiDock.Tests/
│  ├─ ToolKeeper.Tests/
│  └─ ConvAnvil.Tests/
│
├─ Directory.Build.props
└─ ToolKeeper.sln
```

## 核心原則

### 1. 獨立產品維持簡單；平台桌面能力保持模組邊界

汗青、ConvAnvil 與未來獨立產品仍優先維持單一 App Project，不為低價小工具預先拆分大量業務層。

CabiDock 是特殊案例：它已成為 ToolKeeper 的桌面能力，因此最終由 ToolKeeper 主程式管理生命週期，但桌面整合仍保持獨立模組邊界。**單一執行入口不等於單一 Project。**

MarkPad 的產品邏輯維持在單一 App Project：

```text
src/MarkPad/MarkPad.csproj
```

介面共用需求透過引用 `ToolKeeper.UI` 處理，不拆散產品的文件與編輯業務邏輯。

不預先拆成：

- MarkPad.Domain
- MarkPad.Application
- MarkPad.Infrastructure
- MarkPad.Contracts
- MarkPad.Common

除非未來真的出現獨立部署、明確共用或測試隔離需求。

### 2. 依實際需求抽取共用介面

不因產品數量增加就建立 `Shared`、`Common`、`Core`。

只有當至少兩個產品已經出現**實際重複程式碼**，且抽取後能降低維護成本，才考慮共用元件。

> 先重複，再抽象；不要為想像中的未來抽象。

2026-09-28 依實際共用需求，ToolKeeper、CabiDock 現有設定介面與 ConvAnvil 抽取 WPF 共用視窗至 `src/ToolKeeper.UI/ToolKeeper.UI.csproj`。後續 CabiDock 併入 ToolKeeper 後，共用 UI 與桌面業務模組仍分開：`ToolKeeper.UI` 管介面共用，CabiDock／`ToolKeeper.Desktop` 管桌面能力。

共用 `AppWindow` 負責：

- 原生視窗標題列，以 `MainName - SubName` 組成視窗 `Title`。
- 內容左上方 30 DIP 主標題及下方 14 DIP 產品簡介；標題靠上排列，上方不留空白列或額外上邊距。簡介由產品個別提供，與 `SubName` 分開。
- 主標題同一行右側提供「風格、語言、關於」；風格／語言選單控制整個工具視窗，關於使用宿主產品的版本與作者。
- 簡介右側保留可選的 `HeaderActions` 插槽供離線標示等內容；002 的產品操作放在自身 `Workspace`。
- 剩餘 `Workspace` 區域，承載產品自己的功能介面。

目前 ToolKeeper、CabiDock 設定視窗與 ConvAnvil 的主視窗使用 `AppWindow`。`Window.Content` 由共用視窗結構持有，產品內容指定給 `Workspace`。整併完成後，CabiDock 不再需要作為第二個產品主入口，但分類、桌面整合與 Recovery 等業務邏輯仍留在桌面模組；ToolKeeper 只負責組合、生命週期與平台入口。

同日新增可獨立採用的語言、主題／字體、偏好選單與「關於」元件，先接入汗青，再依使用者要求接入 `AppWindow` 及三個工具的功能區。汗青的 `MainWindow` 仍繼承 WPF `Window`，保留自訂標題列、文件分頁及既有操作入口；全品牌標題規則仍適用。

新增元件以宿主明確接入為原則：

- 視窗自行合併 `Resources/UiStyles.xaml`，`UiTheme.ApplyResources` 與 `UiTypography.ApplyResources` 只修改傳入的資源範圍。產品專屬視窗裝飾、AvalonEdit／WebView2 套用及系統主題事件仍由產品處理。
- `UiLanguage.Resolve` 將設定解析為顯示語言；原始 `System` 選項仍由產品保存。`PreferenceMenus` 將選擇值交給宿主 callback，由宿主保存設定、回報錯誤及更新自身視窗。
- 主程式提供 `AboutInfo` 的產品名稱、版本、作者與已翻譯的簡介／品牌文字。`AboutContent` 只建立內容，Popup／Window、尺寸與焦點由宿主承載。

共用模組不建立跨工具 settings 或全域偏好狀態，也不變更汗青既有 JSON 欄位與設定目錄。`AppWindow.PreferencesPath` 由產品指定；`AppWindowPreferences` 只保存風格及原始語言選項，與產品業務設定分開，只有同一路徑的視窗會同步。ToolKeeper／ConvAnvil 使用各自產品目錄下的 `ui.json`，CabiDock 使用既有（包含 `--data-directory`）資料目錄下的 `ui-preferences.json`。`UiPreferencesChanged` 通知產品原位翻譯功能區，動態資源更新顏色；不重建輸入、表格或轉換結果。接入範例與資源名稱見 [ToolKeeper.UI 開發指南](../src/ToolKeeper.UI/README.md)。

### 3. MarkPad 內部分工

- **Models**：文件 Tab、設定狀態等簡單資料模型
- **Services**：檔案、設定、Recovery、Windows 整合等實際服務
- **Rendering**：Markdown → Preview 的轉換與 Preview 相關邏輯
- **Resources**：預覽 CSS／JavaScript 等產品專屬 UI Resource；原生共用樣式由 `ToolKeeper.UI` 提供
- **MainWindow**：組合 UI 與協調使用者操作

V1 不要求所有 UI 都套完整 MVVM。若某區塊複雜度真的提高，再局部引入 ViewModel。

### 4. 第三方套件隔離

主要第三方技術：

- AvalonEdit
- Markdig
- WebView2
- UTF.Unknown（ConvAnvil 的編碼偵測）

加入時盡量集中在對應功能區，不讓第三方 API 擴散到整個程式。

例如：

```text
Rendering/MarkdownRenderer.cs
```

負責包住 Markdig。

ConvAnvil 的 `Services/EncodingDetectionService.cs` 負責包住 UTF.Unknown；文字與位元組轉換仍使用 .NET 標準 `Encoding`，並使用嚴格錯誤處理。

### 5. 產品規格優先於架構

V1 已 Freeze 的產品規格：

```text
docs/products/001_MarkPad.md
```

架構只為了完成產品規格存在。

若架構設計與「簡單、順手、快速開啟」衝突，優先簡化架構。

## Future Products

未來一般工具仍直接新增獨立 App：

```text
src/
├─ ToolKeeper/            # 平台與單一主入口
├─ ToolKeeper.Desktop/    # CabiDock 桌面模組
├─ ToolKeeper.UI/
├─ MarkPad/
├─ ConvAnvil/
├─ Product004/
└─ ...
```

一般產品仍以 Standalone App 為原則；**CabiDock 不再套用這條規則**，因為它已成為 ToolKeeper 平台的桌面層。

ToolKeeper 與桌面模組的責任分界：

- **ToolKeeper**：程式生命週期、系統匣、工具目錄、Launcher、免費小工具、產品狀態與「工具番」桌面群組資料來源。
- **CabiDock / ToolKeeper.Desktop**：DesktopScanner、DesktopWatcher、分類引擎、Explorer 接管、桌面群組、Shell／HWND 整合與 Recovery。
- **ToolKeeper.UI**：可共用的視窗、主題、語言、字體、偏好與 About UI。
- **獨立工具**：自己的產品業務與獨立執行檔。

「工具番」桌面群組是平台資料驅動的特殊群組，不依靠一般副檔名分類：工具安裝或可啟動後可直接出現在群組，讓未來大量工具不必各自在桌面留下傳統捷徑。

ToolKeeper 本體已建立 WPF App Project；`FileHashService` 以串流一次計算三種 HASH，`IconConversionService` 使用 Windows WPF 影像解碼器與 ICO 封裝在本機轉檔。內建功能與目前實作範圍見 [實作與問題報告](TOOLKEEPER-IMPLEMENTATION.md)。
