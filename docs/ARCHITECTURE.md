# ToolKeeper Architecture

## 目標

ToolKeeper 採用 **Monorepo + Standalone Products + Hosted Tool Modules**。

ToolKeeper 本體是平台與 **單一主執行入口**，主視窗只保留工具列表，並管理 Launcher、宿主內工具視窗及 CabiDock 桌面模組的生命週期。

只有單獨上架的產品才有自己的 EXE，目前為 **001 — 汗青**、**003 — ConvAnvil**。**002 — CabiDock**、**004 — Hash Checker**、**005 — Image → ICO** 在 `ToolKeeper.exe` 內執行，各有自己的工具視窗與目錄入口。產品編號與獨立視窗不代表需要另一個執行檔。

2026-09-28 已將桌面掃描、分類、監看、Explorer 接管、群組與 Recovery 抽為 `src/ToolKeeper.Desktop` 類別庫，由 ToolKeeper 本體引用。桌面程式碼保留 `CabiDock` 命名空間，既有資料位置與格式不遷移。`src/CabiDock` 只留歷史路徑說明，開發薄殼與 `CabiDock.exe` 已退役，測試與診斷改用 ToolKeeper 宿主。整併後的真實桌面驗收仍待完成。

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
│  ├─ CabiDock/              # 僅 README 指向桌面模組，無 App Project
│  ├─ ToolKeeper/            # 單一主入口、平台生命週期與 Launcher
│  ├─ ToolKeeper.Desktop/    # 已抽出的桌面模組，namespace CabiDock
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

CabiDock 是特殊案例：它已成為 ToolKeeper 的桌面能力，由 ToolKeeper 主程式管理生命週期，桌面整合保持獨立模組邊界。**單一執行入口不等於單一 Project。**

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

2026-09-28 依實際共用需求，ToolKeeper、CabiDock 設定介面與 ConvAnvil 抽取 WPF 共用視窗至 `src/ToolKeeper.UI/ToolKeeper.UI.csproj`。本輪整併維持分工：`ToolKeeper.UI` 管介面共用，`ToolKeeper.Desktop` 管桌面能力。

共用 `AppWindow` 負責：

- 原生視窗標題列，以 `MainName - SubName` 組成視窗 `Title`。
- 內容左上方 30 DIP 主標題及下方 14 DIP 產品簡介；標題靠上排列，上方不留空白列或額外上邊距。簡介由產品個別提供，與 `SubName` 分開。
- 主標題同一行右側提供「風格、語言、關於」；風格／語言選單控制整個工具視窗，關於使用宿主產品的版本與作者。
- 簡介右側保留可選的 `HeaderActions` 插槽供離線標示等內容；002 的產品操作放在自身 `Workspace`。
- 剩餘 `Workspace` 區域，承載產品自己的功能介面。

目前 ToolKeeper、CabiDock 設定視窗與 ConvAnvil 的主視窗使用 `AppWindow`。`Window.Content` 由共用視窗結構持有，產品內容指定給 `Workspace`。CabiDock 設定視窗由桌面模組持有，ToolKeeper 主視窗只派發產品啟動請求，包含 002 桌面設定；桌面啟停由設定視窗或系統匣提供，`PlatformController` 負責組合、生命週期與平台入口。

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

未來工具先決定是否單獨上架；只有單獨上架才新增獨立 App Project。內建工具由宿主管理，可有獨立視窗與清楚程式模組：

```text
src/
├─ ToolKeeper/            # 平台與單一主入口
├─ ToolKeeper.Desktop/    # CabiDock 桌面模組
├─ ToolKeeper.UI/
├─ MarkPad/
├─ ConvAnvil/
├─ FutureStandaloneProduct/  # 僅在單獨上架時建立
└─ ...
```

002、004、005 都是宿主內的工具；001、003 保留各自業務與獨立 EXE。工具目錄一律保留各產品編號，不以是否獨立程序決定要不要顯示。

ToolKeeper 與桌面模組的責任分界：

- **ToolKeeper**：程式生命週期、系統匣、工具目錄、Launcher、免費小工具、產品狀態與「工具番」桌面群組資料來源。
- **CabiDock / ToolKeeper.Desktop**：DesktopScanner、DesktopWatcher、分類引擎、Explorer 接管、桌面群組、Shell／HWND 整合與 Recovery。
- **ToolKeeper.UI**：可共用的視窗、主題、語言、字體、偏好與 About UI。
- **獨立工具**：自己的產品業務與獨立執行檔。

「工具番」桌面群組是平台資料驅動的特殊群組，呈現 001 至 005，不依靠一般副檔名分類或傳統捷徑。它固定以大的展開模式顯示，不因點標題、滑鼠離開或展開其他分類而收合；可拖曳、調整大小與保存配置，仍受主螢幕工作區限制。一般七個分類維持點擊展開與離開三秒收合。

ToolKeeper 本體已建立 WPF App Project；`FileHashService` 以串流一次計算三種 HASH，`IconConversionService` 使用 Windows WPF 影像解碼器與 ICO 封裝在本機轉檔。內建功能與目前實作範圍見 [實作與問題報告](TOOLKEEPER-IMPLEMENTATION.md)。

## 已實作的平台整併

- `ToolKeeper.App` 解析啟動參數、處理單一實例、診斷與恢復模式；`PlatformController` 持有主視窗、平台系統匣、產品目錄及 `DesktopModule`。
- `DesktopModule` 提供 `Start`、`ShowSettings`、`SetEnabled`、`SetTools`、`PrepareExit` 與 `Dispose`；Shell／HWND、分類、監看與桌面資料保存都留在類別庫。
- `HostWindowLifetime` 統一主視窗關閉／最小化隱藏至系統匣。設定視窗只隱藏；系統匣退出及登出先呼叫 `PrepareExit`，撤下群組、恢復原生圖示並允許視窗真正關閉。
- 正式恢復助手由目前的 `ToolKeeper.exe --desktop-recovery` 私有模式啟動，獨立監控父程序與恢復原生桌面；它沒有自己的 EXE，也不是第二個產品入口。
- `ProductCatalogService` 是主視窗與「工具番」桌面群組的共同資料來源，列出 001 汗青、002 CabiDock、003 ConvAnvil、004 Hash Checker、005 Image → ICO。002／004／005 由宿主開啟各自視窗；001／003 先檢查已知產品 protocol，再檢查受限本機路徑，有正式 Store ID 才提供取得。每十秒、主視窗啟用與系統匣開啟時刷新，啟動前再檢查一次。
- 所有入口使用 `toolkeeper://run/001` 至 `toolkeeper://run/005`，由 ToolKeeper 驗證並派發。桌面模組只將 `DesktopTool.ActivationUri` 傳回宿主 callback，不解析產品、不直接啟動產品程序；已上架產品自己的 protocol 是宿主派發後的實際啟動方式。
- 「工具番」群組不寫入七個分類的規則、不加入檔案分類紀錄，也不建立桌面捷徑；固定展開的群組配置保存於既有 `state.json`。初始化或更新工具列表沿用保存的展開尺寸。

平台偏好預設位於 `%LOCALAPPDATA%\ToolKeeper\ToolKeeper\platform.json`，其中 `desktopEnabled` 在首次使用及無平台偏好的既有使用者預設為 `true`。主檔損毀先嘗試健康備份；無可用備份時預設停用並保留壞檔，介面顯示保存警告。桌面原有資料仍在 `%LOCALAPPDATA%\ToolKeeper\CabiDock`。

`--data-directory` 指定桌面測試資料位置，並把平台與本體 UI 偏好隔離至其 `toolkeeper-host` 子目錄；`--desktop-directory` 只掃描指定目錄，停用真實 Explorer 桌面接管。這些隔離模式與離屏測試不能取代真實桌面、Explorer 重啟與異常恢復驗收。
