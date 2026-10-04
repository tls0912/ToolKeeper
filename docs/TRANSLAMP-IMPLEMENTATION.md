# TransLamp 007 實作與待辦紀錄

更新：2026-10-04。目前開發版：0.1.6。此文件保留原型能力及過去版本的驗證紀錄；0.1.1 修正與驗收見[優化實作紀錄](TRANSLAMP-OPTIMIZATION-IMPLEMENTATION.md)。

## 0.1.6 英文中轉與缺包下載

翻譯方向優先使用目錄中的直譯模型；沒有直譯時，若來源→英文與英文→目標均受支援，便自動串接兩個模型。介面顯示完整路徑與兩段進度，只在最後一段完成後顯示譯文；任何一段失敗、取消或輸入已更動，都不會把英文中間結果當成最終譯文。原文與中間文字各限制 20,000 字元。

在翻譯頁選擇方向或停止輸入約 500 ms 後，若缺少必要語言包，顯示「是／否」對話框，列出缺少方向、個別大小及總大小。選「是」立即切換資料管理，依序下載缺少的包，且同步選取目前下載的語言；已有的包不重複下載。全部完成後保留資料管理頁，若原文非空會自動接續翻譯。取消或失敗停止後續下載，已成功安裝的包仍保留。選「否」後同一方向不因繼續打字反覆詢問；按「立即翻譯」或改變方向可再次詢問。

中轉使用原有的離線 CPU 引擎、語言包驗證與取消機制，不增加線上翻譯服務。仍需目錄提供完整方向；例如目前西班牙文只有英→西模型，因此可以翻成西班牙文，不能以西班牙文為來源中轉。

2026-10-04 驗證：核心路徑選擇、兩段完整文字傳遞、20,000 字元上限、任一段取消／失敗、不顯示過期或中間結果皆通過。介面測試 38/38 通過，包含兩包／單包下載、拒絕後不重複詢問、下載中取消／失敗、原文保留。完整 TransLamp 測試的其餘案例通過，官方全目錄與即時網路下載兩項選擇性測試本次未重跑。三種介面語言、亮／暗竹子最小 900×720 畫面檢查通過，另目視確認中文亮色與英文暗色中轉提示完整。

真實 CPU 中轉驗證另使用已快取的四個官方模型，經正式下載轉檔／安裝流程及 `TranslateRouteAsync`，確認中文→英文→法文與法文→英文→中文兩段均完成。範例「连接失败。请重新启动计算机。」產生「La connexion a échoué. S'il vous plaît redémarrez l'ordinateur.」；法文原文產生「连接失败。请重新启动计算机。」。這是管線與基本目標語言驗證，不代表所有內容的語意品質已驗收。證據位於 `artifacts/translamp-pivot/tests/*.trx`，圖像位於 `artifacts/translamp-ui/pivot-*.png`。

## 0.1.5 程式圖示

另提供一般 Windows 單一 EXE 離線安裝版，包含完整引擎與預載中英語言包；安裝及重建方式見[離線安裝檔說明](TRANSLAMP-OFFLINE-INSTALLER.md)。

採用使用者選定的深竹綠底、金色提燈與「文／A」對話框圖案。核准的原圖完整保存在 [TransLamp.png](../src/TransLamp/Resources/TransLamp.png)，未重繪或裁切；由原圖等比例縮製的 [TransLamp.ico](../src/TransLamp/Resources/TransLamp.ico) 包含 16、24、32、48、64、128、256 像素七種尺寸，保留透明背景。

EXE 透過 `ApplicationIcon` 嵌入 Windows 圖示；同一 ICO 作為 WPF Resource，供主視窗、共享標題列與工作列使用，關於視窗沿用主視窗圖示。無須隨執行檔另外擺放圖示檔案。

圖案來源：2026-10-04 以內建 `image_gen` 依「竹綠圓角方底、金色提燈、奶油色文／A 對話框、外部透明、適合 Windows 小圖示」描述生成，再由使用者選定。轉檔僅使用 WPF 高品質縮放與 PNG 格式 ICO frames。

既有介面測試 **32／32 通過、無跳過**；已檢查亮／暗竹主畫面，標題列圖示可見且未影響布局。見[介面 TRX](../artifacts/translamp-icon/tests/icon-ui.trx)。原圖副本與核准來源的 SHA-256 相同；原图 `C66CBFDBEC6E0246523321963817A5D517F7F38D1CC033157AC87DD9911C3DB1`，ICO `9CCAF34F51A0666C225E985FB853D7C487915B1404776D66EEB7F31FF663A1DA`。

獨立程式：[0.1.5 TransLamp.exe](../artifacts/TransLamp-OfflineKit-0.1.5/TransLamp.exe)。根目錄 [Start-TransLamp.cmd](../Start-TransLamp.cmd) 會依目前版本啟動，請保留整個 Offline Kit 資料夾。

## 0.1.4 開啟語言檔資料夾

「資料管理」的語言包目錄旁新增「開啟語言檔資料夾」按鈕，以 Windows 檔案總管開啟目前使用的語言包目錄；指定 `--data-dir` 時也使用該實際位置。空目錄尚未建立時會先建立，無法建立或開啟時顯示介面語言對應的錯誤訊息。保留中／英／日標籤，作業忙碌時與匯入按鈕一同停用。

既有介面測試 **32／32 通過、無跳過**，三語與亮／暗竹最小 900×720 畫面已檢查新按鈕可見且未遮擋路徑或匯入操作。見[介面 TRX](../artifacts/translamp-folder/tests/folder-ui.trx)。本輪未實際呼叫檔案總管；開啟流程經程式檢查，使用實際目錄路徑與 Windows shell。

當版獨立程式：[0.1.4 TransLamp.exe](../artifacts/TransLamp-OfflineKit-0.1.4/TransLamp.exe)。根目錄啟動入口會依專案目前版本選擇 Offline Kit。

## 0.1.3 擴充語言與下載下拉選單

- 資料管理改成來源／目標兩個下拉選單，目標只列出固定目錄已提供的直接方向。選擇後顯示單一語言包的版本、下載大小、來源、授權與下載／已安裝按鈕，下方保留已安裝清單。
- 固定目錄擴充為 15 種語言選項、27 個方向：英語與簡體中文、繁體中文、法文、葡萄牙文、日文、韓文、德文、義大利文、俄文、阿拉伯文、印地文、泰文、越南文各自雙向翻譯，另有「英語→西班牙文」單向。各方向分別安裝，此版本尚未自動中轉（0.1.6 已加入英文中轉）；Offline Kit 仍只預載原有中英兩包。
- 選單與翻譯頁的語言、原文及譯文互相獨立；切換頁面、風格或介面語言會保留選取的下載方向，僅按下下載才連線。下載／匯入等作業中停用下載選單，完成後更新所選包狀態。
- 支援官方舊版 CTranslate2 的 `shared_vocabulary.txt` 資料檔案及內含設定的模型，保留原始模型／tokenizer 位元組，不補造設定；既有 JSON 格式仍受原規則驗證。兩種格式皆經原有大小／SHA-256、路徑白名單及真實 CPU 模型載入驗證，才提交安裝。各包保留原始 README、來源與授權。
- 中文（簡／繁）及日文的分句結果不額外插入空格；泰文保留句間空格。印尼文及「西班牙文→英語」官方包需要額外 BPE 相依，本輪不擴充該引擎，也不將這些方向列入目錄。
- 既有即時翻譯與專案根目錄 `Start-TransLamp.cmd` 入口保留；入口依專案版本啟動 0.1.3 的完整 Offline Kit。

模型授權標示依各包 README，以及 [Argos 維護者對模型二進位 MIT／CC0 的明確說明](https://github.com/argosopentech/argos-translate/issues/533#issuecomment-5160080718)。原始 README 中的訓練語料來源與個別說明完整保留於 NOTICE；不將語料授權直接改寫為模型授權。俄文雙向包的 README 標題分別為 2.2（英翻俄）與 1.3（俄翻英），但官方目錄與 metadata 均為 1.9，本版以後兩者作為包版本，保留原 README 的差異。

`Prepare-TransLampResources.ps1 -Offline` 通過 **23 項 worker、7 項資源清單及 10 項真實 worker／協定檢查**。正式預設 .NET HttpClient 也已直接從官方 HTTPS 下載英翻阿拉伯文包，完成轉換、暫存驗證、安裝及真實 CPU 翻譯，見[線上下載驗證](../artifacts/translamp-expanded/network-verification.json)。此驗證不代表各電腦的代理／防火牆或翻譯語意品質均已驗收。

最終 C# 回歸 **179／179 通過、無跳過**，另有已單獨通過的 **1 項真實網路下載案例**。前者涵蓋 27 个正式模型檔經下載服務的本機 HTTP transport 完成大小／雜湊檢查、轉換、安裝、真實 CPU 翻譯，以及即時翻譯、取消、失敗復原與三語／兩主題的完整下拉選單。見[整合 TRX](../artifacts/translamp-expanded/tests/translamp-0.1.3.trx)、[27 方向安裝與翻譯結果](../artifacts/translamp-expanded/installation-verification.json)、[來源與驗證彙整](../artifacts/translamp-expanded/verification.json)。UI 以最小 900×720 重渲染，主畫面與長清單 popup 已目視檢查；截圖中的缺少 runtime 提示屬隔離測試 fixture。

各方向的例句驗證確認流程可執行，並非語意品質認證。例如泰文→英語例句將「重新啟動」譯為 `reset`；原有中英模型的已知品質限制亦仍存在，見下方原型紀錄。

0.1.3 Windows x64 self-contained 歷史產物：[TransLamp.exe](../artifacts/TransLamp-OfflineKit-0.1.3/TransLamp.exe)。請保留整個 Offline Kit 資料夾；根目錄啟動入口會依目前專案版本選取最新產物。發布腳本會另以實際隨附的 runtime 執行中英雙向 smoke，並記錄完整產物雜湊。

下方 0.1.2 與原型段落保留當時的支援範圍與驗證紀錄。

## 0.1.2 即時翻譯與資料管理

- 原文輸入／貼上後暫停 500 毫秒便自動翻譯，切換方向也會重新排程；輸入法組字期間不觸發。保留立即翻譯與 Ctrl+Enter。
- 翻譯時可繼續編輯、貼上、清除及切換方向。新的輸入會取消舊工作，並以輸入版本檢查阻止過期結果及進度回寫；取消／Esc 會清除待執行工作，下一次編輯才重新排程。
- 「翻譯」與「資料管理」分頁保留原文及方向。資料管理提供下載清單、已安裝語言包、離線匯入與移除；進度與取消操作共用。
- 固定下載目錄提供 English ⇄ 中文、English ⇄ 法文、English ⇄ 葡萄牙文，每個方向分別下載。顯示版本、真實下載大小、來源及授權。中文與法文／葡萄牙文之間沒有直接模型，不自動轉接翻譯。
- 下載明確由使用者啟動；翻譯仍完全在本機執行。下載後核對固定大小與 SHA-256，轉換為資料專用 `.tlpack`，沿用既有安全匯入、模型載入驗證及交易復原。
- 官方舊版日本語模型不符合現有 runtime 格式，本次未擴充相容層或將其列為可下載語言。

0.1.2 建置成功，完整 C# 回歸 **144／144 通過、無跳過**，包括實際中英自動翻譯、新增四方向的真模型安裝與推論、下載錯誤／取消／舊包保留、輸入節流／IME／關窗及兩頁版面。見 [整合 TRX](../artifacts/translamp-live/tests/translamp-0.1.2.trx)。

`Prepare-TransLampResources.ps1 -Offline` 通過 21 項 worker、7 項資源清單及 10 項真實 worker／協定檢查；`Publish-TransLamp.ps1` 產出 self-contained Windows x64 [0.1.2 可執行檔](../artifacts/TransLamp-OfflineKit-0.1.2/TransLamp.exe)，實際隨附 runtime 另通過中英雙向 smoke。整個資料夾需一起保留。

已目視檢查最小 900×720 的翻譯／下載／已安裝區，保留三語與亮竹／暗竹渲染驗收；深色語言卡標題前景色已修正並加入實際色彩斷言。圖片使用測試文字及合成包資料，見 `artifacts/translamp-ui`。

新語言的官方檔案實際透過 curl 取得；.NET 下載服務以這些真實檔案的 HTTP transport 測試，完成 size／SHA-256、轉換、模型驗證及 CPU 推論，見 [模型與來源驗證](../artifacts/translamp-downloads/verification.json)。另核對官方法文 URL 直接回應 HTTP 200 與預期大小。尚未逐一驗證各電腦的網路代理／防火牆，也不把流程通過視為翻譯語意品質已驗收。

下方各原型章節保留當時狀態，不代表 0.1.2 的功能清單。

後續檢查：[全面優化檢查](TRANSLAMP-OPTIMIZATION-REVIEW.md)，包含 12 項已確認問題、隔離重現證據、效能量測與修正順序。

## 0.1.0 原型交付

已建立可實際執行的 Windows x64 離線中英翻譯產品，使用真實 OPUS-MT／Argos 模型及 CTranslate2 CPU 推論，不使用示範字典或線上翻譯服務。

- 獨立 WPF 程式：`src/TransLamp`。
- 共用介面：直接繼承 `ToolKeeper.UI.AppWindow`，竹紋頂列、Ink 預設風格、風格／語言／關於入口與工具番一致。
- 純文字雙欄、調整欄寬、中英方向交換、貼上、清除、翻譯、取消、複製。
- `Ctrl+Enter` 翻譯、`Esc` 取消；上限 20,000 個字元，超過時明確提示，不靜默截斷。
- 中／英／日介面與偏好保存；本版翻譯模型只提供中英雙向。
- 語言包版本、大小、來源與授權可見；支援 `.tlpack` 離線匯入、移除。
- 翻譯與模型驗證不阻塞 UI；忙碌時限制不相容操作，取消／關窗後不寫回過期結果。
- 工具番目錄加入 007，平台入口 `toolkeeper://run/007`，獨立產品入口 `translamp://open`。

## 執行與重建

專案根目錄提供 [Start-TransLamp.cmd](../Start-TransLamp.cmd) 雙擊入口，呼叫 `scripts/Start-TransLamp.ps1`，依 `.csproj` 目前版本啟動相應 Offline Kit 的獨立 EXE，並將工作目錄設為包目錄。檢查必要程式／runtime／隨附語言包是否存在；缺件時提示準備及發布步驟，不自動下載、不回退到舊版本。`-CheckOnly` 可只驗證目標而不啟動視窗；此入口不需要工具番宿主或協定註冊。

Offline Kit 的預設輸出已改為 `artifacts/TransLamp-OfflineKit-<產品版本>`。0.1.0 原型保留於 `artifacts/TransLamp-OfflineKit-0.1.0`；0.1.1 產物與當次驗證見優化實作紀錄。完整資料夾需一起搬移，不能只複製 EXE。

原型 0.1.0 可執行檔：[`TransLamp.exe`](../artifacts/TransLamp-OfflineKit-0.1.0/TransLamp.exe)。原型壓縮包：[`TransLamp-OfflineKit-0.1.0.zip`](../artifacts/TransLamp-OfflineKit-0.1.0.zip)。原型解壓後約427 MiB，另需約162 MiB存放首次安裝的兩個語言包。

```powershell
# 開發環境需 .NET 10 SDK
dotnet build src/TransLamp/TransLamp.csproj

# 有網路的準備電腦下載固定版本資源、驗 SHA256、製作語言包
./scripts/Prepare-TransLampResources.ps1

# 已有完整下載快取時，可禁止下載並重建資源
./scripts/Prepare-TransLampResources.ps1 -Offline

# 發布附带 .NET 的 Windows x64 可攜包
./scripts/Publish-TransLamp.ps1
```

資源準備完成後，正常 `dotnet build` 會將 `artifacts/translamp` 中的 runtime／語言包複製到開發輸出，使工具番的既有 checkout 偵測可啟動具翻譯能力的 007。資源尚未準備時仍可編譯 UI，介面會明確提示缺少引擎，不提供假翻譯。大型二進位檔只位於已忽略的 `artifacts`／`bin`，不加入 Git。

目标 PC 不需要安裝 Python 或 .NET。CTranslate2 的 Microsoft Visual C++ x64 執行元件若未安裝，需使用離線包 `Prerequisites/VC_redist.x64.exe`；本次不自動修改系統或執行安裝器。

預設資料位置：`%LOCALAPPDATA%\ToolKeeper\TransLamp`。語言包放 `LanguagePacks`、UI 偏好放 `ui.json`。支援明確本機 CLI `--data-dir`、`--runtime-dir` 供驗收隔離，URI 不接受任意路徑參數。

## 翻譯與語言包設計

App 將單次請求透過 UTF-8 JSON stdin 傳給隨附的 `Runtime/python.exe -I translate.py`，接收逐行進度與最後的完整結果。每次只處理一個翻譯工作；取消會終止該推論程序及子程序。翻譯前重新驗證模型檔案雜湊，使用中的語言包會鎖定，其他實例不能同時移除或更新。

Runtime 使用固定 Windows x64 embedded Python、CTranslate2、SentencePiece 及必要相依。CPU int8 推論，不使用 GPU 或一般大型語言模型。按換行、句子與 tokenizer 邊界分段，保留換行與縮排；未完整結束的模型輸出視為失敗，不顯示半成品為完成結果。

`.tlpack` 是資料專用 ZIP，包含 manifest、模型、tokenizer、LICENSE、NOTICE。安裝器檢查：

- 語言方向、版本、runtime、來源及授權欄位。
- 明確的資料檔案白名單，拒絕路徑穿越、執行碼、重複／未列出項目、symlink。
- 檔案大小與 SHA-256，全包上限 2 GiB。
- 先驗證至暫存目錄再替換；失敗或取消保留原有正常包。
- 手動移除的隨附包會留下移除標記，避免重啟後又自動安裝；手動匯入可恢復。

SHA-256 是內容完整性與固定版本重現的檢查，**尚不是發行者簽章／信任鏈**。只應使用可信來源提供的正式資源。

原文和譯文不保存為歷史、log 或遙測。翻譯 worker 不含 HTTP 下載／雲端翻譯程式，Python audit hook 額外拒絕 socket 連線。模型下載僅在明確執行資源準備脚本時發生。此措施不等同已完成作業系統封包監測或實體斷網機驗收。

## 0.1.0 原型驗證紀錄

最終 TransLamp C# 回歸 **45／45 通過、無跳過**，包含真實 CPU 中英雙向推論、特殊符號回歸、推論中取消與包鎖定。指令：

```powershell
$env:TRANSLAMP_RESOURCES = "$PWD/artifacts/translamp"
dotnet test tests/TransLamp.Tests/TransLamp.Tests.csproj --no-restore --verbosity minimal -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
```

- 共用 AppWindow、預設 Ink、三語／主題切換保留原文、方向切換清除失效譯文、輸入限制與 URI 驗證。
- 語言包成功匯入、來源 archive 移除後仍可使用、損壞更新保留舊包、大小／hash／metadata／symlink／路徑穿越拒絕、取消清理與重啟移除狀態。
- 真實模型 process 取消及跨實例包鎖定另行通過。
- 最終離線包獨立再跑兩項真實模型整合測試，**2／2 通過**；確認隨附 .NET 10.0.12／WindowsDesktop framework、worker 與原始碼 SHA256 一致。
- 12 張最小視窗尺寸 900×720 的三語／竹亮／竹深渲染圖：`artifacts/translamp-ui`。
- 實際啟動 Offline Kit 視窗，自動準備兩個語言包並完成英翻中；確認換行保留與完成狀態。此過程發現 tokenizer 特殊符號 `▁` 外洩，已修正解碼並以相同例句通過 runtime 與 C# 回歸。
- 桌面後续驗收收到使用者實體 Esc 停止訊號，已停止所有 Computer Use；不繞過停止訊號操作桌面。後續採程式測試與檔案驗證。

007 接入時 ToolKeeper.Tests **165／165 通過**；共享工作目錄後續加入 006 後，重新還原並執行產品目錄／URI 相關契約 **67／67 通過**，確認 007 仍可共存。既有 WPF 並行測試偶發共享集合例外，驗證命令暫停 xUnit collection parallelization，未擅改全專案測試策略。

Runtime 有 **11／11 單元測試**與 **10 項真實 worker 檢查**。結果保存於 `artifacts/translamp/verification.json`。單台開發機的例句實測（含新 worker 載入；非低階硬體承諾）：

| 輸入 | 時間 | 實際結果 |
| --- | --- | --- |
| 英文59字元：`Please restart the device and check the network connection.` | 約0.28秒 | `请重新启动设备并检查网络连接。` |
| 繁中17字：`請重新啟動設備，然後檢查網路連線。` | 約0.71秒 | `Please restart the device and check the network connection.` |
| 1,079字元英文／18段 | 約1.04秒 | 18段均完成 |

### 已確認的模型品質缺陷

輸入 `Please check the network cable.` 時，模型可能產出：

> 请检查access-date=中的日期值 (帮助) 网络电缆.

`access-date=中的日期值 (帮助)` 並不存在於原文。調整為 Argos 官方 beam／length 設定仍可重現，屬目前模型的品質缺陷。**未以例句特判或字串刪除掩蓋，也未標記為已修正；正式發布前必須評估替代模型或改善方案。** 技術流程測試通過不代表所有譯文正確。

另已執行 **40 組自行撰寫的中英工程／一般例句**，40 個 process 正常完成，總計15.806秒。來源與譯文在 `artifacts/translamp/quality-samples.json`，檢視紀錄在 `quality-review.json`；此兩檔也隨離線包 `Verification` 交付。人工抽看確認：

| 原文內容 | 譯文缺陷 |
| --- | --- |
| `設備 D100 在 3000 ms 內沒有回應。` | `3000 ms` 被改為 `30000 ms` |
| IP `192.168.1.10`、port `502` | IP 被改為 `192168.1.10`，port 遺漏 |
| `C:\Logs\alarm_2026.txt` | 路徑內插入空白 |
| `retry_count` | 被翻譯為 `重试_ 计数` |
| `motor`、`韌體` | 分別誤譯為「运动」、`body` |
| 「我明天早上會到」 | 遺漏「明天」 |

目前版本可用於產品流程、離線部署與模型替換的開發驗證，**尚未通過正式翻譯品質驗收**。工程參數、位址及程式識別字仍須以原文為準。

### 可重現資源與大小

`Prepare-TransLampResources.ps1 -Offline` 已實跑成功，從固定 SHA256 快取重建並完成驗證。這是建置流程禁止下載，不代表已完成乾淨目標機的全機斷網測試。

| 元件 | 版本／實際大小 |
| --- | --- |
| Runtime | Python 3.12.10、CTranslate2 4.8.2、SentencePiece 0.2.2；131,975,734 bytes（約126 MiB） |
| English → 中文 pack | 70,271,470 bytes（約67 MiB），安裝後約81 MiB |
| 中文 → English pack | 73,453,779 bytes（約70 MiB），安裝後約81 MiB |
| Microsoft VC++ x64 | 14.44.35211.0；25,635,768 bytes，簽章驗證為 Microsoft Corporation／Valid；未執行安裝 |

Runtime 主要空間來自 CTranslate2 native libraries 與 NumPy，未引入 PyTorch／Stanza 整套環境。Python 固定版本與 native 相依的安全維護更新、完整散布授權盤點仍須在正式發布前處理。

同一工作目錄另有先前 UI／MarkPad 修改，以及其他工作加入的 HistoLens 006；本次保留並配合既有內容，未將其他工作計入 007 交付。

## 尚未完成與待使用者檢視

| 項目 | 現況／後續必要工作 |
| --- | --- |
| 正式發行 | 目前為未簽章開發版；尚未建立 Store MSIX、正式網站下載、產品 Store ID、發行者簽章。沒有虛構下載入口。 |
| Protocol 安裝 | App 接受 `translamp://open`，Launcher 會優先使用已登記 protocol；尚無正式 Installer 自動註冊或跨通路衝突處理。本次沒有寫入 Windows registry。 |
| 純離線目標機 | 此機可完成 local CPU 推論；未在從未連網、沒有既有 VC++ runtime 的乾淨機器做完整驗收。需在乾淨 Windows 10／11 x64 以隨附先決元件验证。 |
| 低階硬體 | 4 GB RAM、舊 CPU 指令集與多代 Windows 10 相容性尚未實測，不宣稱已達正式最低需求。 |
| 翻譯品質 | 中文輸出以簡體為主；已確認上方無關文字插入問題。專有名詞、型號、數字、單位與否定語意可能翻錯。尚無繁簡切換或術語保護引擎。正式發布前需人工檢視 30–50 組現場例句並決定可接受模型。 |
| 語言包下載 UI | 本版提供隨附中英包、離線匯入与可重現的資源準備脚本；尚未建立正式線上包目錄、UI下載、斷點續傳與發行者簽章。 |
| 更多語言 | 日文選項明確標示未提供，不能翻譯；不列出尚未驗證的模型作為已支援。 |
| 載入效能 | 每次翻譯啟動隔離 process 並載入模型，尚無常駐模型快取；優先選擇簡單的取消與資源回收流程。 |
| 產品封裝 | 現在是 portable開發包；是否以 Store＋官網安裝器／Offline Kit正式發行，依原規格仍需後續封裝驗證。 |
| 授權發布審查 | 模型原README標示 OPUS CC-BY 4.0，包內保留模型及 Argos MIT 授權和來源署名；正式對外散布仍需依實際發行方式再核對完整第三方義務。 |

上述項目沒有阻止本次可執行原型，也沒有被標為已完成的正式產品能力。
