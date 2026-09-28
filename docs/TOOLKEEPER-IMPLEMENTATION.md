# ToolKeeper 本體：實作與問題報告

更新日期：2026-09-28。

## 本輪範圍

本輪完成 **五項工具目錄、004／005 模組視窗拆分與統一 URI 啟動流程**。`ToolKeeper.exe` 是唯一平台宿主，主視窗只顯示工具列表；002 CabiDock 列表入口或系統匣可開啟桌面設定，桌面啟停保留在設定視窗與系統匣。免費 **004 Hash Checker** 與 **005 Image → ICO** 各自有獨立的 UI，在宿主同一程序內執行。

本體列表與桌面「工具番」專屬群組使用同一份五項目錄：001 汗青、002 CabiDock、003 ConvAnvil、004 Hash Checker、005 Image → ICO。002／004／005 為內建模組，直接開啟；001／003 為獨立 App，依可用狀態顯示「開啟／取得／未提供」。工具番群組固定展開，離開滑鼠或點擊標題都不收合；一般桌面分類群組的原有收合行為保留。

本體仍是 .NET 10 WPF `src/ToolKeeper`，引用 `ToolKeeper.Desktop` 類別庫。桌面模組保留 `CabiDock` 命名空間、資料格式與原有設定位置；CabiDock 獨立開發 EXE 已移除，開發與診斷統一使用宿主。001／003 維持獨立 App，本輪無新增第三方執行期套件。

本體、桌面設定、004／005 與 003 使用 `ToolKeeper.UI.AppWindow` 共用外觀。原生標題分別為 `ToolKeeper - 工具番`、`Hash Checker - File Hash Verification`、`Image → ICO - Icon Converter`；內容左上是主標題與產品簡介，右側是風格、語言與關於。

圖示使用使用者提供的「桌子與檯燈」ICO，原檔收錄於 `src/ToolKeeper/Resources/ToolKeeper.ico`，包含 16、24、32、48、64、128、256 px 七個尺寸。本體執行檔、系統匣、002／004／005 模組及其附屬視窗共用此圖示；宿主也明確設定 WPF `Window.Icon`，使關於面板顯示一致圖示。品牌只由 ToolKeeper 宿主注入，001／003 等獨立執行檔保留各自圖示，不由共用 UI 程式庫決定。

共用標題列右側提供「風格、語言、關於」。ToolKeeper 本體接入完整視窗切換：淺色、深色、水墨、水墨暗版與跟隨系統會套用到功能卡片、按鈕、輸入框、結果與比對狀態；介面支援繁體中文、英文、日文與系統語言，文案及正在顯示的操作狀態會立即更新。水墨風格沿用共用介面字型，HASH 值保留等寬字型，方便逐字檢查。

本體與 004／005 共用 `%LOCALAPPDATA%\ToolKeeper\ToolKeeper\ui.json`，保留拆分前的風格／語言偏好，並由 `AppWindow` 在使用相同偏好路徑的視窗間同步。CabiDock 設定仍使用既有獨立偏好。切換時不重建工作區，不清除 HASH 輸入／選取範圍、雜湊結果、ICO 結果或取消控制。工具列表同步翻譯，產品集合保持同一實例。關於資訊由各視窗提供產品名稱、組件版本、簡介與作者「不告訴你  Untold」。

### 桌面生命週期與偏好

- 初次使用及尚無平台偏好的既有使用者預設啟用桌面功能，後續啟停狀態保存於 `%LOCALAPPDATA%\ToolKeeper\ToolKeeper\platform.json` 的 `desktopEnabled`。
- 主檔損毀、缺必要欄位時先讀健康備份；沒有可用備份時預設停用，保留壞檔且不覆寫，主畫面回報警告。暫時性 I/O 寫入失敗保留記憶體狀態，於產品刷新、下次前台刷新與退出時重試保存。
- 主視窗關閉或最小化會隱藏至系統匣，進行中的 HASH／ICO 作業繼續；桌面設定關閉只隱藏。系統匣提供開啟本體、桌面設定、桌面啟停與結束程式。
- 004／005 各自只保留一個開啟中的視窗，重複啟動會喚回既有視窗；關閉模組只取消該模組工作，不結束平台或其他模組。平台退出時關閉全部模組。
- 明確退出與 Windows 登出／關機先執行 `PrepareExit`，恢復原生圖示、撤下群組並允許視窗真正結束。恢復助手由 `ToolKeeper.exe --desktop-recovery` 私有模式啟動，獨立處理主程序異常。
- 既有 CabiDock 分類與群組資料仍放在 `%LOCALAPPDATA%\ToolKeeper\CabiDock`；不搬動檔案或遷移設定。工具番群組不參與副檔名／關鍵字分類，其配置沿用 `state.json`。

### 產品狀態與 Launcher

`ProductCatalogService` 將 002／004／005 標為 `BuiltIn` 且可開啟，交由宿主處理；001／003 標為 `Standalone`。獨立 App 優先檢查已知 URI Handler，再確認受限的本機發行／開發路徑具有執行檔、組件與 runtimeconfig。汗青沿用 `toolkeeper-markpad:` 與正式 Store ID `9NHF764PXW9C`；ConvAnvil 尚無正式 protocol／Store ID，但找到完整本機版本可開啟。無安裝且有正式 Store ID 時提供「取得」，否則保留停用的「未提供」。本體不替獨立 App 註冊自己的協定，不代為下載或安裝。

每十秒、主視窗啟用及系統匣開啟時刷新產品狀態，主列表與桌面群組使用同一次資料。Launcher 在使用者點擊時重新檢查，失敗會在主視窗工具列表內顯示訊息，該訊息區平時隱藏；安裝移除不留下可盲目啟動的舊狀態。

### URI 啟動與單一宿主

所有工具入口使用 `toolkeeper://run/<ID>`，只接受目錄中的 `001` 至 `005`，不接受任意路徑、可執行命令、查詢參數或片段。主列表、桌面群組、桌面設定入口與外部協定經過同一驗證／分派流程。

- **本體未執行**：啟動宿主、初始化平台及桌面模組，再開啟指定工具，不先顯示無關的本體主畫面。
- **本體已執行**：第二次短暫啟動將 URI 經目前使用者的命名管線轉交相同資料目錄／工作階段的既有宿主，取得接收回覆後退出。無 URI 的重複啟動會喚回主視窗。
- **正常啟動**：在 `HKCU\Software\Classes\toolkeeper` 註冊目前 Windows 使用者的協定處理命令；既有非 ToolKeeper 擁有的註冊保留並顯示原因。內部列表不需要經 Shell 重新啟動程序。
- **隔離或診斷**：指定 `--data-directory` 或 `--desktop-directory`，以及診斷／恢復模式，均不註冊真實 `toolkeeper` 協定。

正式註冊命令使用 `ToolKeeper.exe --protocol-activate "%1"`。`--protocol-activate` 只接受精確兩個參數：旗標與一個有效 URI，不允許附加其他選項。開發 CLI 使用 `--activate "toolkeeper://run/004"`，可同時指定隔離目錄；診斷模式不能同時啟動工具。

開發執行：

```powershell
dotnet run --project src/ToolKeeper/ToolKeeper.csproj
```

隔離測試桌面與全部狀態：

```powershell
New-Item -ItemType Directory -Force artifacts/toolkeeper-demo/desktop
dotnet run --project src/ToolKeeper/ToolKeeper.csproj -- --desktop-directory artifacts/toolkeeper-demo/desktop --data-directory artifacts/toolkeeper-demo/profile --activate "toolkeeper://run/004"
```

`--desktop-directory` 只掃描指定目錄，不接管真實 Explorer 桌面；可從桌面設定開啟群組操作預覽。`--data-directory` 同時將平台／本體及 004／005 UI 偏好放在指定資料目錄的 `toolkeeper-host` 子目錄。以上命令會開啟 HASH 模組；改成 `/002` 可開啟桌面設定，改成 `/005` 可開啟 ICO 模組。再次以相同資料目錄執行會轉交既有宿主。

建置測試：

```powershell
dotnet test tests/ToolKeeper.Tests/ToolKeeper.Tests.csproj -c Release
```

產生資料夾版本：

```powershell
dotnet publish src/ToolKeeper/ToolKeeper.csproj -c Release --self-contained false -o artifacts/ToolKeeper.Integrated
```

執行 `artifacts/ToolKeeper.Integrated/ToolKeeper.exe`。這是需要 **.NET 10 Desktop Runtime** 的資料夾版本，移動時須保留同資料夾檔案，包含桌面模組與共用 UI。開發與測試使用 .NET 10 SDK；此版本未打包成 Microsoft Store 安裝套件。

### 004：Hash Checker 模組

視窗 `ToolKeeper.Modules.HashCheckerWindow` 只包含 HASH UI，由宿主建立與管理，沒有 ICO 控制；完整規格見 [004 Hash Checker](products/004_HashChecker.md)。

- 從檔案總管拖入單一檔案，或按「選擇檔案」，自動計算 MD5、SHA-1、SHA-256。
- 三種 HASH 共用同一趟串流讀取；緩衝區為 128 KiB，不將整個檔案載入記憶體，沒有另設檔案容量上限。
- 結果可選取複製；「複製全部」產生附演算法名稱的三行文字。
- 在預期 HASH 欄貼上純十六進位值，自動辨識 32／40／64 位長度、不分大小寫、忽略頭尾空白，立即顯示相符、不相符或輸入格式錯誤。
- 可取消，或直接拖入另一個檔案重新計算。舊結果立即清空，較早工作的結果不會覆蓋最新結果。
- 計算期間只允許其他程序讀取檔案；遇到使用中的寫入檔案、資料夾或無權限檔案會顯示錯誤。
- 多檔拖放不會偷偷只處理第一個；此區維持一次一檔。

### 005：Image → ICO 模組

視窗 `ToolKeeper.Modules.ImageToIcoWindow` 只包含 ICO UI，由宿主建立與管理，沒有 HASH 控制；完整規格見 [005 Image → ICO](products/005_ImageToIco.md)。

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
| 本體尺寸 | 預設 820 × 620，最小 720 × 500；工具列表可捲動 |
| 004／005 尺寸 | 各自預設 760 × 660，最小 660 × 560；HASH 內容及 ICO 結果可捲動 |
| 工具列表操作 | 與桌面群組共用產品狀態；有可用 App 時開啟，有正式 Store ID 時取得，否則停用 |
| 發行 | 本機開發／資料夾版本；正常啟動為目前使用者註冊 ToolKeeper 協定，不進行上架或安裝獨立 App |

## 驗證結果

2026-09-28 圖示統一：Release 建置與發布成功，`MainWindowIntegrationTests`、`UiSmokeTests`、`ModuleWindowManagerTests` 合計 **24 項通過**。從發布版建立主視窗、002 設定、004、005、群組預覽及載入群組內容的原生宿主視窗，於離屏透明視窗的 `Loaded` 後確認六者共用同一個已凍結的圖示來源；也驗證內嵌 ICO 與來源雜湊一致、執行檔可擷取圖示、系統匣圖示在關閉來源串流後仍能解碼。驗證未接管 Explorer 或註冊正式協定；紀錄位於 `artifacts/icon-tests`。獨立 001／003 與共用 UI 程式庫未因宿主品牌改動。

2026-09-28 主視窗簡化：移除桌面狀態／啟停／設定列後，`MainWindowIntegrationTests`、`UiSmokeTests`、`ModuleWindowManagerTests` 合計 **24 項針對性測試通過**。保留五項目錄（包含可開啟桌面設定的 002）、列表內錯誤訊息與最小尺寸驗證。已重新發布 `artifacts/ToolKeeper.Integrated`。

本次離屏截圖使用 `artifacts/toolkeeper-ui/launcher-*`、`hash-*`、`ico-*`。已目視英文深色最小尺寸：本體 704×460 內容區呈現五項工具列表；HASH／ICO 各自 644×520 內容區保持獨立功能與頁尾，內容超出時由各自捲動區呈現。三語與風格切換、最小尺寸及捲動可達性測試通過。真實外部協定點擊、桌面群組與 Explorer 接管仍待人工驗收。

### 歷史紀錄：移除主視窗桌面列之前的模組拆分驗證

以下為上一輪分環境完成的 296 項驗證紀錄。

2026-09-28 模組拆分與 URI 流程：`ToolKeeper.Tests` **158／158**、`CabiDock.Tests` **138／138**，共 **296／296** 相關測試通過。ToolKeeper 的 150 項於 sandbox 執行，另外 8 項協定註冊測試因 sandbox 禁止 Registry 寫入，改在正常使用者環境使用隨機暫存登錄子樹完成；未修改正式 `toolkeeper` 協定。這是分環境驗證，紀錄位於 `artifacts/module-tests`，含 `modules-verified_*`、`modules-desktop-final_*` 與 `protocol-user-profile.trx`。

驗證涵蓋 HASH／ICO 工作流程、獨立模組視窗、五項目錄、視窗重用／關閉、冷啟動偏好載入、URI 參數白名單與引號注入拒絕、同宿主訊息轉交、逾時取消、協定註冊擁有權與舊 DelegateExecute 清理。桌面測試另涵蓋工具番永遠展開、拖移／縮放與保存、一般分類收合、URI 回呼與首次布局避讓；已保存位置不重新排版。

Release solution 建置成功，0 警告、0 錯誤；最新版已發布至 `artifacts/ToolKeeper.Integrated`，目錄只有 `ToolKeeper.exe` 一個執行檔。實際發布版的隱藏合成視窗診斷退出碼 0，`published-opacity.json` 回報 `Succeeded: true`；此診斷不接管真實桌面，也不註冊協定。

### 歷史紀錄：整併初版，尚未拆分 004／005

以下數字、發布記錄與 `integrated-*` 截圖屬於前一階段，不作為本輪模組拆分驗證結果。

2026-09-28 平台整併驗證：ToolKeeper **98／98**、CabiDock **135／135**、ToolKeeper.UI **35／35**、ConvAnvil **117／117** 通過。MarkPad 在 sandbox 環境有 6 項 DPAPI／WebView2 相關失敗，使用正常 Windows 使用者環境且 `--no-build --no-restore` 重測後 **157／157** 通過。以上合計 **542／542** 是分環境完成的驗證，並非單次 solution 測試全部通過；TRX 紀錄在 `artifacts/integration-tests`，MarkPad 正常環境紀錄為 `markpad-user-profile.trx`。

當時新增驗證涵蓋桌面啟停／設定請求、產品可用狀態與三語切換、平台偏好及啟動參數、主視窗隱藏後工作續行，以及帶錯誤訊息的最小視窗離屏布局。當時目視的 `artifacts/toolkeeper-ui/integrated-en-Dark-minimum-client.png` 為舊的 844×700 合併畫面：桌面工具列、ICO 提示、工具列表與頁尾完整，未出現重疊或裁切；此截圖不反映目前的模組視窗。當時尚未完成真實 Explorer 桌面與商店啟動人工驗收。

`ToolKeeper.sln` Release 最終建置為 **0 個警告、0 個錯誤**。已發布 `artifacts/ToolKeeper.Integrated`，其中只有 `ToolKeeper.exe` 主執行檔，另含 `ToolKeeper.Desktop.dll`／`ToolKeeper.UI.dll`；需要 .NET 10 Desktop Runtime。以實際發布版執行 `--diagnose-group-opacity`，報告 `Succeeded: true`、退出碼 0，30／50／100% 不透明度與父子視窗關係檢查通過。這只操作隱藏合成視窗，不代表實際桌面接管已驗收。

以下保留整併前的歷史驗證紀錄；其中工具列表停用是當時的實作狀態。

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
- 整併後的真實 Explorer 接管、Win+D、Explorer 重啟、混合 DPI、多螢幕切換、正常關機／登出與異常恢復仍待人工驗收。
- 實際 Store 版／本機版開啟、安裝／移除後的主列表與桌面群組更新、正式 Store 商品頁導向仍待人工驗收；自動測試不安裝 App 或開啟真實商店，協定註冊測試使用隔離的測試登錄鍵。
- 真實 Windows 外部 `toolkeeper://run/<ID>` 點擊的冷啟動／已執行轉交、固定展開桌面群組的實際滑鼠操作，仍待人工驗收。正常宿主啟動會寫入目前使用者的協定註冊，隔離／診斷執行不會。
