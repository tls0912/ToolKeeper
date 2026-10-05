# ToolKeeper｜工具番

ToolKeeper（工具番）是一個以「超好用的小工具」為核心的 Windows 軟體品牌與 Launcher。

原則：

- 小而專一
- 開啟就能用
- 不增加工作流程，只刪除工作流程
- 低價、低維護、離線優先
- 001 汗青、003 ConvAnvil 維持獨立 App；002 CabiDock、004 Hash Checker、005 Image → ICO 由 ToolKeeper 宿主載入
- 所有軟體的標題列都顯示主標題與副標題，格式為 `MainName - SubName`（見[全域規則](docs/PRODUCT_VISION.md#21-軟體標題與副標題全域規則)）

ToolKeeper、桌面設定、004／005 與 003 ConvAnvil 使用 `ToolKeeper.UI` 共用視窗元件；001 汗青維持既有自訂介面。`ToolKeeper.exe` 是平台唯一宿主，桌面能力留在 `ToolKeeper.Desktop` 類別庫，HASH／ICO 各有獨立模組視窗並在同一程序執行。儲存庫不再保留 CabiDock 開發 EXE。實機桌面驗收仍待完成，範圍見[架構說明](docs/ARCHITECTURE.md)。

## 007 TransLamp 獨立啟動

在專案根目錄雙擊 [Start-TransLamp.cmd](Start-TransLamp.cmd)，即可直接開啟目前版本的 TransLamp Offline Kit。入口會依專案版本選擇 `artifacts/TransLamp-OfflineKit-<版本>/TransLamp.exe`，不用先啟動工具番。

尚未建立離線包時，先執行 `scripts/Prepare-TransLampResources.ps1`，再執行 `scripts/Publish-TransLamp.ps1`。入口只檢查並啟動既有程式，不會自動下載或建置。可用 `scripts/Start-TransLamp.ps1 -CheckOnly` 檢查實際啟動路徑。

## ToolKeeper 本體

ToolKeeper 主視窗只保留五項工具列表。桌面設定由列表的 002 CabiDock 或系統匣開啟，桌面啟停由設定視窗或系統匣操作。免費的 Hash Checker 與 Image → ICO 各自開啟獨立視窗，保留原有操作，不在本體主畫面直接執行。

Hash Checker 可顯示 MD5／SHA-1／SHA-256；Image → ICO 可將 PNG／JPG／BMP 在原資料夾轉成 ICO，全部在本機離線處理。

工具列表與固定展開的「工具番」桌面群組共用五項目錄：001 汗青、002 CabiDock、003 ConvAnvil、004 Hash Checker、005 Image → ICO。002／004／005 是內建模組，直接由宿主開啟；001／003 找到可用版本時顯示「開啟」，有正式 Store 商品頁時顯示「取得」，兩者都沒有則保留停用的「未提供」。汗青已有正式 Store ID，ConvAnvil 目前沒有商店入口。工具番群組不套用一般分類群組的離開收合行為。

首次啟動與尚無平台偏好的既有使用者預設啟用桌面功能；之後記住啟停狀態。關閉或最小化主視窗會隱藏至系統匣，桌面設定視窗關閉只隱藏；從系統匣「結束程式」才會停止本體並恢復原生桌面。既有 CabiDock 分類、群組與設定資料路徑不變。

五項入口統一使用 `toolkeeper://run/001` 至 `toolkeeper://run/005`。本體未執行時先啟動宿主再開啟指定工具；已執行時將請求轉交既有宿主，004／005 各只保留一個視窗。正常啟動會在目前 Windows 使用者註冊 `toolkeeper` 協定；隔離目錄與診斷模式不註冊。

```powershell
dotnet run --project src/ToolKeeper/ToolKeeper.csproj
```

隔離桌面與資料後開啟 004，不接管真實桌面或註冊協定：

```powershell
New-Item -ItemType Directory -Force artifacts/toolkeeper-demo/desktop
dotnet run --project src/ToolKeeper/ToolKeeper.csproj -- --desktop-directory artifacts/toolkeeper-demo/desktop --data-directory artifacts/toolkeeper-demo/profile --activate "toolkeeper://run/004"
```

功能、測試結果、使用方式與待驗收事項見 [ToolKeeper 實作與問題報告](docs/TOOLKEEPER-IMPLEMENTATION.md)。

## Products

全系列 Release 發佈已整合 Obfuscar：工具番與全部自有工具／共用模組會自動混淆，開發建置與測試保持原樣。涵蓋範圍、發佈指令與驗證方式見 [混淆發佈說明](docs/OBFUSCATION.md)。

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

CabiDock 原有的桌面分類能力已納入 ToolKeeper，定位為 **Desktop Experience / Desktop Layer**。使用者以 `ToolKeeper.exe` 作為單一主入口載入桌面模組。

- 產品規格：[`docs/products/002_CabiDock.md`](docs/products/002_CabiDock.md)
- 桌面模組：[`src/ToolKeeper.Desktop`](src/ToolKeeper.Desktop)，沿用 `CabiDock` 命名空間與資料格式
- 宿主入口：`ToolKeeper.exe` 或 `toolkeeper://run/002`；開啟桌面設定
- 狀態：已實作平台整併、分類、群組操作與桌面接管；整併後的真實 Explorer 相容性仍待驗收

CabiDock 模組負責桌面掃描、分類規則、狀態保存、桌面監看、Explorer 接管、群組互動與異常恢復；ToolKeeper 負責主程式生命週期、系統匣、Launcher 與平台整合。桌面接管程式碼保持獨立模組邊界，不直接堆入 ToolKeeper 主視窗。

除了七個一般桌面分類，已加入由 ToolKeeper 產品目錄驅動、**固定展開的「工具番」專屬群組**，列出五項工具的開啟、取得或未提供狀態，不要求先建立傳統桌面捷徑；群組位置與大小沿用桌面配置保存。

已移除 CabiDock 獨立開發 EXE；桌面開發與診斷統一經過 ToolKeeper 宿主。獨立恢復程序使用 `ToolKeeper.exe --desktop-recovery` 私有模式，由模組自動啟動，負責異常恢復，並非第二個常駐產品入口。

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

### 004 — Hash Checker

免費的檔案 HASH 計算與比對模組。自己的視窗提供 MD5／SHA-1／SHA-256、進度、取消、結果複製與預期 HASH 比對；在 ToolKeeper 程序內執行。

- 產品規格：[`docs/products/004_HashChecker.md`](docs/products/004_HashChecker.md)
- 視窗：[`src/ToolKeeper/Modules/HashCheckerWindow.xaml`](src/ToolKeeper/Modules/HashCheckerWindow.xaml)
- 啟動入口：`toolkeeper://run/004`

### 005 — Image → ICO

免費的圖片轉 ICO 模組。自己的視窗支援 PNG／JPG／BMP 批次轉換、七種圖示尺寸、保留比例／透明度與取消；在來源資料夾輸出，不覆寫原圖或既有 ICO。

- 產品規格：[`docs/products/005_ImageToIco.md`](docs/products/005_ImageToIco.md)
- 視窗：[`src/ToolKeeper/Modules/ImageToIcoWindow.xaml`](src/ToolKeeper/Modules/ImageToIcoWindow.xaml)
- 啟動入口：`toolkeeper://run/005`

## Documents

- [Product Vision](docs/PRODUCT_VISION.md)
- [Architecture](docs/ARCHITECTURE.md)

> 工具番可以越來越大，但每個工具本身保持簡單、直接、順手。
