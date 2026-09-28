# ToolKeeper｜工具番

ToolKeeper（工具番）是一個以「超好用的小工具」為核心的 Windows 軟體品牌與 Launcher。

原則：

- 小而專一
- 開啟就能用
- 不增加工作流程，只刪除工作流程
- 低價、低維護、離線優先
- 各正式工具原則上維持獨立 App；002 CabiDock 例外，作為 ToolKeeper 的桌面模組由本體載入
- 所有軟體的標題列都顯示主標題與副標題，格式為 `MainName - SubName`（見[全域規則](docs/PRODUCT_VISION.md#21-軟體標題與副標題全域規則)）

ToolKeeper、CabiDock 現有設定介面與 003 ConvAnvil 已使用 `ToolKeeper.UI` 的共用視窗元件。後續整併後，使用者以 `ToolKeeper.exe` 作為唯一主入口；CabiDock 的桌面掃描、分類、監看、Explorer 接管、桌面群組與 Recovery 保持模組邊界，由 ToolKeeper 載入與管理。003 ConvAnvil 及其他正式工具仍保留自己的 App。001 汗青維持既有自訂介面。範圍見[架構說明](docs/ARCHITECTURE.md)。

## ToolKeeper 本體

ToolKeeper 是平台與單一主執行入口。免費的 Hash Checker 與 Image → ICO 已可使用；後續並負責載入 CabiDock 桌面模組、工具目錄、Launcher、系統匣與「工具番」桌面群組。桌面群組可集中呈現汗青、ConvAnvil 與未來工具的啟動入口，避免大量工具捷徑散落桌面。

Hash Checker 可顯示 MD5／SHA-1／SHA-256；Image → ICO 可將 PNG／JPG／BMP 在原資料夾轉成 ICO，全部在本機離線處理。

下方以同一份工具列表呈現目錄與 Launcher 的規劃版面，左側名稱、右側「開啟／取得」。**目錄與 Launcher 的實際啟動／取得功能依本輪要求暫不實作**，按鈕停用。

```powershell
dotnet run --project src/ToolKeeper/ToolKeeper.csproj
```

功能、測試結果、使用方式與待驗收事項見 [ToolKeeper 實作與問題報告](docs/TOOLKEEPER-IMPLEMENTATION.md)。

## Products

### 001 — 汗青

Markdown 文件閱讀與編輯工具，Microsoft Store 名稱為「汗青 - Markdown Writer」（原名 MarkPad）。

- 產品規格：[`docs/products/001_MarkPad.md`](docs/products/001_MarkPad.md)
- 程式碼：[`src/MarkPad`](src/MarkPad)

目前開發版本為 **0.1.19**，執行檔為 `Hanqing.exe`：包含多分頁閱讀／編輯、搜尋取代、竹子（亮色）與竹子（深色）等主題、三語 UI、檔案保護與崩潰復原，以及先儲存原稿、再於旁邊產生附有時間戳記 PDF 的匯出功能。竹子介面搭配宣紙底色，亮色與深色的紙面纖維紋路都更清晰、疏鬆。新安裝與重設設定預設使用「竹子（亮色）」，介面與預覽的自動字型搭配「傳統文字」；已有的有效偏好繼續沿用。原有專案資料夾及腳本名稱保留 `MarkPad`。

商店版採 7 天免費試用，每次試用啟動檢查授權；到期提示購買並結束啟動，查詢失敗時可重試或關閉。首次確認正式授權後保存本機受保護紀錄，同一 Windows 使用者／電腦後續免查詢；紀錄遺失或失效時重新確認。一般未封裝開發／可攜版略過 Store 查詢。使用者已在 Partner Center 設定 7 天免費試用，本次未登入後台查驗；MSIX 1.0.1.0／x64 已建置並通過套件核對，尚未安裝或上傳，WACK 與真實 Store 授權驗收仍待完成，詳見 [封裝說明](packaging/MarkPad/README.md)。

語言切換、介面風格／字型及關於內容使用 [ToolKeeper.UI 共用元件](src/ToolKeeper.UI/README.md)。汗青沿用自訂視窗與既有操作入口，設定維持相容。

編輯模式的文件上方新增工具列，可直接調整編輯器字型與 8–72 字級，以及插入內文／H1–H6、粗體、斜體、刪除線、行內程式碼與連結。字型和字級只改變編輯顯示並記住偏好；格式操作使用標準 Markdown，可復原，並保留唯讀保護。預覽與空白視窗會隱藏這列。

預覽模式左側會自動列出 H1–H6 章節，依層級縮排，點選即可跳轉；編輯模式則採左側原始碼、右側即時預覽，不顯示章節列表。兩欄寬度可拖曳調整，切換模式與分頁保留各文件的編輯狀態。

```powershell
dotnet run --project src/MarkPad/MarkPad.csproj -- ".\README.md"
```

建置、測試、可攜版打包與選用的 Windows 檔案關聯，見 [開發說明](docs/DEVELOPMENT.md)。
已實作範圍、驗證結果與待驗收項目，見 [實作狀態](docs/IMPLEMENTATION.md)。

### 002 — CabiDock（ToolKeeper Desktop Module）

CabiDock 原有的桌面分類能力納入 ToolKeeper，定位為 **Desktop Experience / Desktop Layer**。使用者端最終不需要另外啟動 CabiDock 常駐程式，而是由 `ToolKeeper.exe` 作為單一主入口載入桌面模組。

- 產品規格：[`docs/products/002_CabiDock.md`](docs/products/002_CabiDock.md)
- 目前程式碼：[`src/CabiDock`](src/CabiDock)
- 目標：逐步整理為可由 ToolKeeper 引用的桌面模組（例如 `ToolKeeper.Desktop`）
- 狀態：已實作分類、群組操作與桌面接管；真實 Explorer 相容性仍待驗收

CabiDock 模組負責桌面掃描、分類規則、狀態保存、桌面監看、Explorer 接管、群組互動與異常恢復；ToolKeeper 負責主程式生命週期、系統匣、Launcher 與平台整合。桌面接管程式碼保持獨立模組邊界，不直接堆入 ToolKeeper 主視窗。

除了七個一般桌面分類，未來增加由 ToolKeeper 產品目錄驅動的 **「工具番」專屬群組**，直接呈現已安裝或可啟動的工具番產品，不要求先建立傳統桌面捷徑。

目前 `src/CabiDock` 仍可獨立執行供開發與桌面整合驗證；這是過渡期工程入口，不代表最終產品仍有第二個主執行入口。

啟動與驗證方式見 [CabiDock 實作狀態](docs/CABIDOCK-IMPLEMENTATION.md)，桌面整合限制見 [桌面原型驗證](docs/CABIDOCK-DESKTOP.md)。

### 003 — ConvAnvil

Text, Encoding & Byte Converter。離線檢查文字檔編碼、BOM 與換行格式，預覽轉檔結果，並在文字與位元組之間雙向轉換；003 產品規劃另包含 JSON 格式化、樹狀檢視、壓縮與複製。

- 產品規格：[`docs/products/003_ConvAnvil.md`](docs/products/003_ConvAnvil.md)
- 程式碼：[`src/ConvAnvil`](src/ConvAnvil)
- 狀態：開發實作目前採 File／Text／Bytes 三區；JSON Viewer 已納入 003 產品範圍，尚未實作

File 區提供編碼推測、手動重新解碼、轉換預覽與另存新檔；Text／Bytes 區提供 Hex、Decimal、Binary、控制字元檢視及換行轉換。無法表示的字元或不合法的位元組會顯示錯誤，不以替代字元靜默完成轉換。

```powershell
dotnet run --project src/ConvAnvil/ConvAnvil.csproj
```

使用方式、驗證結果與目前限制見 [ConvAnvil 實作狀態](docs/CONVANVIL-IMPLEMENTATION.md)；已採用的預設與後續產品選擇見 [決策報告](docs/CONVANVIL-DECISIONS.md)。

## Documents

- [Product Vision](docs/PRODUCT_VISION.md)
- [Architecture](docs/ARCHITECTURE.md)

> 工具番可以越來越大，但每個工具本身保持簡單、直接、順手。
