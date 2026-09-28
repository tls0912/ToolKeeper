# ToolKeeper｜工具番

ToolKeeper（工具番）是一個以「超好用的小工具」為核心的 Windows 軟體品牌與 Launcher。

原則：

- 小而專一
- 開啟就能用
- 不增加工作流程，只刪除工作流程
- 低價、低維護、離線優先
- 每個工具都是獨立 App
- 所有軟體的標題列都顯示主標題與副標題，格式為 `MainName - SubName`（見[全域規則](docs/PRODUCT_VISION.md#21-軟體標題與副標題全域規則)）

ToolKeeper 本體、002 CabiDock 與 003 ConvAnvil 採用 `ToolKeeper.UI` 共用主視窗外觀：原生標題列顯示主標題與副標題，內容左上顯示 30 DIP 主標題及 14 DIP 產品簡介。標題同一行右側提供「風格、語言、關於」，功能區同步切換五種風格與中英日語言，各產品分別保存偏好。產品簡介獨立於副標題；各產品的功能與資料處理仍由各自專案負責。001 汗青保留既有自訂介面，本輪不加入共用主視窗。範圍見[架構說明](docs/ARCHITECTURE.md#2-依實際需求抽取共用介面)。

## ToolKeeper 本體

免費的 Hash Checker 與 Image → ICO 已可使用：左上拖入檔案顯示 MD5／SHA-1／SHA-256，右上拖入 PNG／JPG／BMP 直接在原資料夾產生 ICO；全部在本機離線處理。

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

### 002 — CabiDock

依類型與檔名關鍵字自動分類桌面；群組平時收合，點擊展開、滑鼠離開收合，保留檔案原始位置。

- 產品規格：[`docs/products/002_CabiDock.md`](docs/products/002_CabiDock.md)
- 程式碼：[`src/CabiDock`](src/CabiDock)
- 狀態：已實作分類、群組操作與桌面接管；真實 Explorer 相容性仍待驗收

目前可使用七個預設分類、自訂副檔名與關鍵字規則、分類保存、桌面監看、群組拖曳與系統匣。一般啟動會嘗試將群組附掛桌面，裁切受管理檔案的原生圖示並保留系統圖示；包含暫停恢復、獨立異常恢復程序與 Explorer 變更後重建。裁切仍保留 Explorer 原有鍵盤選取模型，尚未完成 V1 的真實桌面驗收。

```powershell
dotnet run --project src/CabiDock/CabiDock.csproj
```

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
