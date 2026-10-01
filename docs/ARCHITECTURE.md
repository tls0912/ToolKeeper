# ToolKeeper Architecture

## 目標

ToolKeeper 採用 **Monorepo + Standalone Products + Hosted Tool Modules**。

ToolKeeper 本體是平台與 **單一主執行入口**，主視窗只保留工具列表，並管理 Launcher、宿主內工具視窗及 CabiDock 桌面模組的生命週期。

只有單獨上架的產品才有自己的 EXE，目前為 **001 — 汗青**、**003 — ConvAnvil**。**002 — CabiDock**、**004 — Hash Checker**、**005 — Image → ICO** 在 `ToolKeeper.exe` 內執行，各有自己的工具視窗與目錄入口。產品編號與獨立視窗不代表需要另一個執行檔。

2026-09-28 已將桌面掃描、分類、監看、Explorer 接管、群組與 Recovery 抽為 `src/ToolKeeper.Desktop` 類別庫，由 ToolKeeper 本體引用。桌面程式碼保留 `CabiDock` 命名空間，既有資料位置與格式不遷移。`src/CabiDock` 只留歷史路徑說明，開發薄殼與 `CabiDock.exe` 已退役，測試與診斷改用 ToolKeeper 宿主。整併後的真實桌面驗收仍待完成。

ToolKeeper 本體、CabiDock 設定介面、ConvAnvil、004 Hash Checker 與 005 Image → ICO 使用 `ToolKeeper.UI` 共用視窗元件；汗青保留自訂視窗並使用其中可獨立採用的語言、主題／字體、竹材／紙紋、偏好選單與「關於」元件。

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

2026-09-28 先將標準工具的 `AppWindow` 抽至 `src/ToolKeeper.UI/ToolKeeper.UI.csproj`。2026-10-01 依全系列共用介面的需求，進一步將汗青的外框、標題列、視窗操作、文字陰影與外觀套用抽入同一類別庫；汗青和一般工具使用同一骨架，只保留不同的產品布局。

| 共用來源 | 責任 | 產品提供 |
| --- | --- | --- |
| `WindowFrame` | 自訂標題列、完整 `Window.Title`、圖示、視窗操作、外框、caption 高度及全螢幕標題列顯隱 | 所屬 `Window`、caption 內容與操作插槽、工作區 |
| `UiAppearance` | 依視窗資源範圍一次套用調色盤、竹材／紙紋、介面字型及文字陰影 | 主題、解析後語言、字型／尺寸、陰影偏好 |
| `AppWindow` | 使用 `WindowFrame` 的一般工具布局：簡介、風格／語言／關於、可選 `HeaderActions`、`Workspace` | 產品名稱、副標題、簡介、功能內容、偏好檔路徑 |
| `UiStyles`、`ChromeTextShadow`、`PreferenceMenus`、`AboutContent` | 共用基本樣式、文字描邊、選單選項及關於內容 | 文案、產品資訊、選擇回呼與布局中的入口位置 |

`WindowFrame` 以組合方式使用，避免要求文件工具繼承一般工具的偏好儲存與標頭布局。ToolKeeper 本體、CabiDock 設定、ConvAnvil、004 和 005 透過 `AppWindow` 使用它；汗青在自己的 `Window` 中使用同一 `WindowFrame`，將文件分頁放入 caption 插槽，側欄、編輯器及預覽放入工作區。產品不再複製標題列或另繪視窗操作鈕，也不再同時顯示一個 30 DIP 的重複產品標題。

汗青的授權檢查、CabiDock 的群組預覽及重新命名視窗也使用同一骨架；不可縮放的對話框只保留關閉鈕。關閉鈕呼叫所屬視窗的正常 `Close()` 流程，因此汗青的未儲存文件確認、ToolKeeper 隱藏至系統匣及 CabiDock 設定視窗的隱藏邏輯，仍由產品既有 `Closing` 處理。共用骨架使用 WPF `WindowChrome` 處理 caption 與調整尺寸，最大化套用螢幕工作區；汗青自行管理全螢幕的文件布局、螢幕範圍與還原狀態。

共用模組不建立跨工具 settings 或全域偏好狀態，也不變更汗青既有 JSON 欄位與設定目錄。`AppWindow.PreferencesPath` 由產品指定；`AppWindowPreferences` 只保存風格及原始語言選項，與產品業務設定分開，只有同一路徑的視窗會同步。ToolKeeper／ConvAnvil 使用各自產品目錄下的 `ui.json`，CabiDock 使用既有（包含 `--data-directory`）資料目錄下的 `ui-preferences.json`。`UiPreferencesChanged` 通知產品原位翻譯功能區，動態資源更新顏色；不重建輸入、表格或轉換結果。

`UiAppearance.ApplyResources` 只修改呼叫者傳入的 `ResourceDictionary`。原始 `System` 語言與主題選項由產品保存；系統偏好事件、儲存錯誤回報及跨視窗通知仍由原宿主管理。`AboutInfo` 使用產品自己的名稱、版本及作者。汗青的閱讀／編輯字型、AvalonEdit、WebView2、文件與復原服務留在汗青。接入範例與資源名稱見 [ToolKeeper.UI 開發指南](../src/ToolKeeper.UI/README.md)。

本體與 001–005 共用 `BambooChrome` 與 `PaperTexture`。`Ink` 保留淺色去皮竹材，`InkDark` 保留深綠竹皮及竹節；材質以凍結的 WPF `DrawingBrush`、固定 DIP 尺寸拼貼，視窗尺寸不改變紋理比例；一般 `Light`／`Dark`／`System` 回到純色資源。共用 caption 也採用同一竹紋，不再留下系統繪製的獨立純色標題列。HTML 預覽的紙紋仍由汗青渲染器負責。

`ToolKeeper.UI` 管介面共用，`ToolKeeper.Desktop` 管桌面能力。CabiDock 嵌入 Explorer 的桌面群組不是一般應用程式視窗，保留自己的群組布局與互動，接入共用材質，不加上最小化／最大化／關閉列。一般分類及「工具番」群組的外框與標題均使用同一材質，預覽與桌面群組一致；桌面接管、分類、復原及群組互動留在 `ToolKeeper.Desktop`。

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
