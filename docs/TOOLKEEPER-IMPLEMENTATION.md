# ToolKeeper 本體：實作與問題報告

更新日期：2026-09-28。

## 本輪範圍

依本輪要求，先完成免費 **Hash Checker** 與 **Image → ICO**。主畫面參考提供的草圖：左上檔案 HASH、右上圖片轉 ICO、下方整列工具列表，名稱在左、操作在右。

「目錄」與「Launcher」合併成同一份列表的規劃版面；**實際目錄／Launcher 操作先不實作**。下方列出汗青（原名 MarkPad）、CabiDock、ConvAnvil，「開啟／取得」按鈕停用並標示規劃預覽。沒有猜測安裝狀態，也沒有配置假商店連結。

新增獨立的 .NET 10 WPF 本體專案 `src/ToolKeeper`，沿用儲存庫風格，無新增第三方執行期套件。既有三款產品與原有未提交變更保留。

2026-09-28 主視窗改用 `ToolKeeper.UI` 的 `AppWindow`，與 002、003 共用基本外觀。原生標題列為 `ToolKeeper - 工具番`，內容左上以 30 DIP 顯示 `ToolKeeper`，下方以 14 DIP 顯示獨立產品簡介；右側操作與下方功能內容由本體提供。Hash Checker、Image → ICO 與工具列表的行為仍由本體專案負責。

共用標題列右側提供「風格、語言、關於」。ToolKeeper 本體接入完整視窗切換：淺色、深色、水墨、水墨暗版與跟隨系統會套用到功能卡片、按鈕、輸入框、結果與比對狀態；介面支援繁體中文、英文、日文與系統語言，文案及正在顯示的操作狀態會立即更新。水墨風格沿用共用介面字型，HASH 值保留等寬字型，方便逐字檢查。

偏好儲存在 `%LOCALAPPDATA%\ToolKeeper\ToolKeeper\ui.json`。切換時不重建工作區，不清除預期 HASH、選取範圍、雜湊結果、ICO 結果或取消控制；執行中的作業繼續完成。下方工具列表仍維持停用操作。關於資訊由主程式提供產品名稱、組件版本、簡介與作者「不告訴你  Untold」。

開發執行：

```powershell
dotnet run --project src/ToolKeeper/ToolKeeper.csproj
```

建置測試：

```powershell
dotnet test tests/ToolKeeper.Tests/ToolKeeper.Tests.csproj -c Release
```

產生資料夾版本：

```powershell
dotnet publish src/ToolKeeper/ToolKeeper.csproj -c Release --self-contained false -o artifacts/ToolKeeper
```

執行 `artifacts/ToolKeeper/ToolKeeper.exe`。這是需要 **.NET 10 Desktop Runtime** 的資料夾版本，移動時須保留同資料夾檔案。開發與測試使用 .NET 10 SDK；此版本未打包成 Microsoft Store 安裝套件。

### 左上：Hash Checker

- 從檔案總管拖入單一檔案，或按「選擇檔案」，自動計算 MD5、SHA-1、SHA-256。
- 三種 HASH 共用同一趟串流讀取；緩衝區為 128 KiB，不將整個檔案載入記憶體，沒有另設檔案容量上限。
- 結果可選取複製；「複製全部」產生附演算法名稱的三行文字。
- 在預期 HASH 欄貼上純十六進位值，自動辨識 32／40／64 位長度、不分大小寫、忽略頭尾空白，立即顯示相符、不相符或輸入格式錯誤。
- 可取消，或直接拖入另一個檔案重新計算。舊結果立即清空，較早工作的結果不會覆蓋最新結果。
- 計算期間只允許其他程序讀取檔案；遇到使用中的寫入檔案、資料夾或無權限檔案會顯示錯誤。
- 多檔拖放不會偷偷只處理第一個；此區維持一次一檔。

### 右上：Image → ICO

- 支援 `.png`、`.jpg`、`.jpeg`、`.bmp`，可多選或一次拖入多張，逐張處理。
- 依實際圖片格式解碼，錯誤副檔名不會讓非圖片內容通過；不支援 GIF、SVG、HEIC、WebP 或資料夾。
- 產生含 **16、24、32、48、64、128、256 px** 七個尺寸的 ICO，使用 PNG 影像資料封裝；已由 Windows WPF ICO 解碼器驗證。
- 保留長寬比例及透明度，以透明邊界置中補成正方形。JPEG 支援 EXIF 的八種旋轉／鏡像方向。
- 自動儲存在來源圖片旁，如 `photo.png` → `photo.ico`；同名已存在時改用 `photo (1).ico` 等名稱。既有 ICO 與來源圖片均不覆寫。
- 先寫入同目錄暫存檔，完成後以不覆寫的移動發布輸出；失敗或合作式取消會清理暫存檔。
- 各筆結果列出成功輸出路徑或失敗原因；一張失敗不會中斷其他圖片。「在資料夾中顯示」可定位成功的輸出。
- 可取消批次，已完成的 ICO 保留。轉換中不接受第二批，避免混淆當前結果。

## 已採用的預設與限制

| 項目 | 本輪採用方式 |
| --- | --- |
| 隱私與費用 | 本機處理，不上傳、不登入、無付費 API |
| ICO 圖片限制 | 單檔不超過 64 MiB、3,200 萬像素、任一邊不超過 16,384 像素；避免異常圖片耗盡記憶體 |
| 非正方形圖片 | 維持比例，以透明邊界補齊，不裁切、不拉伸 |
| 取消速度 | HASH 每個讀取區塊檢查；圖片轉換在解碼後、縮放及寫出階段檢查，Windows 原生解碼期間需等該步完成 |
| 小視窗 | 預設 1100 × 850，最小 860 × 740；縮小時左側內容與右側結果可捲動，工具列表保持下方 |
| 工具列表操作 | 只保留版面，啟動／取得按鈕停用；後續再接安裝偵測、URI protocol 與正式 Store ID |
| 發行 | 本機開發／資料夾版本；本輪不進行上架、安裝或登錄檔註冊 |

## 驗證結果

2026-09-28 完整視窗風格／語言切換：測試 **55／55 通過**。新增驗證涵蓋三語與五種風格切換後保留同一工作區、ICO 項目與雜湊輸入選取範圍，並確認執行中的雜湊／轉換仍可取消及正常完成。已目視英文深色、日文水墨暗版與繁體中文水墨的 844×700 最小內容尺寸截圖；標題右側三個入口與下方功能區色彩、字型和文案一致，工具列表操作維持停用。截圖位於 `artifacts/toolkeeper-ui`，仍採不顯示桌面視窗的 WPF 離屏驗證。

2026-09-28 共用主視窗調整：Release 建置無警告，測試 **50／50 通過**。已目視 844×700 最小內容尺寸的離屏渲染，標頭、產品簡介、操作區與工作區配置正常；原生標題以 `Title`／`WindowStyle` 自動測試驗證。本輪未完成完整人工桌面驗收。

以下保留 2026-09-26 的驗證紀錄。

- 整份 `ToolKeeper.sln` Release 建置通過：**0 個警告、0 個錯誤**。
- ToolKeeper 本體 Release 測試 **49／49 通過**：18 項 Hash 服務案例、22 項 ICO 服務案例、9 項 WPF UI／拖放案例。測試紀錄位於 `artifacts/toolkeeper-tests/toolkeeper-release.trx`。
- Hash 覆蓋空檔與標準向量、多區塊內容、進度、取消、檔案占用、比對格式、失敗後清除舊結果與同時切換檔案。
- ICO 覆蓋七尺寸封裝與實際 Windows 解碼、長寬比例、透明留白與邊緣、八種 EXIF 方向、併行同名碰撞、非圖片／損壞內容、容量及像素限制、取消與來源保留。
- UI 覆蓋混合成功／失敗批次、工具列表的左側名稱與右側停用操作、預設與最小視窗版面、捲動後可完整讀取比對／狀態。實際 WPF 路由拖放事件驗證接受時回報 Copy，Move-only、文字、多檔 HASH 與忙碌中的 ICO 則回報 None。
- 四張離屏畫面已產生並檢視：`artifacts/toolkeeper-ui/empty-default.png`、`empty-minimum-client.png`、`results-default.png`、`results-minimum-client.png`。
- 已產生 Release 可執行資料夾 `artifacts/ToolKeeper`，入口為 `ToolKeeper.exe`；未自動開啟使用者桌面視窗。

## 問題與待驗收

目前沒有需要先回答才能繼續的阻塞問題。

- 開發環境預設 NuGet 設定的讀取受到 sandbox 限制；已使用儲存庫現有的離線套件快取、局部 `DOTNET_CLI_HOME`／`APPDATA` 與 `.packages/test-nuget.config` 完成還原，沒有修改使用者全域設定。
- 發現並修正最小視窗的 ICO 空白提示文字裁切、JPEG EXIF 方向未套用，以及 WPF Drop 回報效果可能包含 Move 的問題；拖放明確回報 Copy，拒絕只允許 Move 的來源，並增加回歸驗證。
- UI 測試採離屏 WPF 視窗與圖像渲染，沒有操作使用者的檔案總管、剪貼簿或桌面。真實 Explorer 拖放、檔案選擇對話框、剪貼簿占用及多螢幕 DPI 切換，仍列為人工驗收項目，不宣稱已完成實機操作驗證。
- 目錄與 Launcher 的啟動／取得整合依指示暫緩，不屬於本輪未解決錯誤；待後續啟用時需提供正式 Store 商品 ID 與各 App 的啟動註冊。
