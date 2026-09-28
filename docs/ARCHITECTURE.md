# ToolKeeper Architecture

## 目標

ToolKeeper 採用 **Monorepo + Standalone Apps**。

每一個正式產品都是可獨立建置、獨立上架的 Windows App。ToolKeeper 本體負責工具目錄與 Launcher，並可提供 [產品願景](PRODUCT_VISION.md) 明列的少量免費小工具。

目前產品為 **001 — MarkPad**、**002 — CabiDock** 與 **003 — ConvAnvil**，各自使用獨立 WPF App Project。免費本體使用 `src/ToolKeeper`，已實作 Hash Checker 與 Image → ICO；目錄與 Launcher 目前只有合併列表版面，實際啟動／取得操作暫緩。

ToolKeeper 本體、002 與 003 的主視窗共用 `src/ToolKeeper.UI/ToolKeeper.UI.csproj` 的 `AppWindow`，標題行右側提供風格、語言與關於入口，各產品仍保留獨立入口與業務邏輯。001 汗青保留自訂視窗，使用同一模組的語言、主題／字體、偏好選單與「關於」內容。

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
│  ├─ CabiDock/
│  ├─ ToolKeeper/
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

### 1. 一個產品先維持一個 Project

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

2026-09-28 依使用者要求，ToolKeeper 本體、002 CabiDock 與 003 ConvAnvil 抽取實際共用的 WPF 主視窗至 `src/ToolKeeper.UI/ToolKeeper.UI.csproj`。這是已確認的介面共用需求，不將各產品拆成額外的業務層專案。

共用 `AppWindow` 負責：

- 原生視窗標題列，以 `MainName - SubName` 組成視窗 `Title`。
- 內容左上方 30 DIP 主標題及下方 14 DIP 產品簡介；標題靠上排列，上方不留空白列或額外上邊距。簡介由產品個別提供，與 `SubName` 分開。
- 主標題同一行右側提供「風格、語言、關於」；風格／語言選單控制整個工具視窗，關於使用宿主產品的版本與作者。
- 簡介右側保留可選的 `HeaderActions` 插槽供離線標示等內容；002 的產品操作放在自身 `Workspace`。
- 剩餘 `Workspace` 區域，承載產品自己的功能介面。

三個產品的主視窗繼承 `AppWindow`。`Window.Content` 由共用視窗結構持有，產品內容應指定給 `Workspace`；002 的桌面接管切換與群組操作預覽按鈕，放在自身 `Workspace` 第一列靠右的水平工具列。分類、桌面整合、編碼／位元組轉換、雜湊與 ICO 轉換等業務邏輯，以及各產品的狀態與資料儲存，仍留在各產品專案。

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

未來工具直接新增：

```text
src/
├─ MarkPad/
├─ CabiDock/
├─ ConvAnvil/
└─ ...
```

每個產品仍以 Standalone App 為原則。

ToolKeeper 本體已建立獨立 WPF App Project；`FileHashService` 以串流一次計算三種 HASH，`IconConversionService` 使用 Windows WPF 影像解碼器與 ICO 封裝在本機轉檔，未新增第三方執行期套件。內建功能與本輪暫緩範圍見 [實作與問題報告](TOOLKEEPER-IMPLEMENTATION.md)。
