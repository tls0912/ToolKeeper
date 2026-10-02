# TransLamp 007 全面優化檢查

日期：2026-10-03。檢查基準：目前工作目錄中的 0.1.0 開發版與已準備的真實中英模型；Git HEAD `3cbe4ba`，包含尚未提交的 007 實作。

## 結論與範圍

原型已具備工具番共用介面、本機 CPU 翻譯、可攜語言包及離線包，但仍不適合當成已通過品質驗收的正式版本。優先改善「翻譯內容是否可靠」和「更新、移除、打包失敗後能否保留可用狀態」，再依實測改善速度與體積。

本次涵蓋 `src/TransLamp`、Python worker、語言包、準備／發布腳本、相關測試，以及 ToolKeeper 的 007 啟動入口。不是全系列產品的完整審查；沒有修改 MarkPad、HistoLens 或其他既有工作。本次僅新增診斷附件與此文件，未套用產品修正、提交或發布。

優先級：**P1**＝主要使用流程的內容正確性問題，正式發布前必須處理；**P2**＝已重現的可靠性／契約缺陷，應排入下一輪修正；**P3**＝易用性或需量測決定的改善。未驗證事項另列，不當成已確認缺陷。

## 優先排序速覽

共 12 項已確認問題：1 項 P1、9 項 P2、2 項 P3；另列性能候選與發布驗證缺口。

| 順序 | 項目 | 建議處理 |
| --- | --- | --- |
| 1 | O01 翻譯數字／語意、O10 長句切段 | 建立品質門檻；修分段並比較模型，不盲目還原佔位符 |
| 2 | O02–O05 語言包替換、移除、中斷、競態 | 保留最後可用狀態，修正鎖內條件與復原 |
| 3 | O07–O09 批次準備、資源驗證、發布輸出 | 一包失敗不阻止另一包，最終產物實際驗證 |
| 4 | O06、O11–O12 契約與介面狀態 | 方向一致、跨視窗刷新、失效狀態清除 |
| 5 | 小批次與載入成本 | 在品質驗證下量測改善；常駐模型暫後排 |

## 已確認問題

### O01 · P1 · 技術數字與語意失真，成功完成不代表譯文可用

**證據**：既有 40 組合成例句中，`設備 D100 在 3000 ms 內沒有回應。` 被譯成 `Device D100 did not respond within 30000 ms.`；IP `192.168.1.10` 變成 `192168.1.10` 並遺漏 port `502`；network cable 例句插入不相干的 `access-date=中的日期值 (帮助)`。路徑、變數、工業術語和時間資訊也有錯誤。來源：[品質樣本](../artifacts/translamp/quality-samples.json)、[既有品質紀錄](TRANSLAMP-IMPLEMENTATION.md#已確認的模型品質缺陷)。

**影響**：對工廠與設備維修用途，數字與角色錯置比文字不自然更嚴重；目前僅顯示通用提醒，無法指出哪個參數已經改變。

**建議**：先增加精確技術 literal 的缺失／變形提示與品質回歸資料集，保留原文供對照；再用相同例句比較模型。不要以已知例句字串替換掩蓋錯誤，也不要把數字存在視為語意正確。

**驗收**：數值、IP、port、路徑、PLC device、版本／hex／變數及否定語意分項評估。已知缺陷不得因 worker exit code 為 0 就算通過；關鍵資訊變動必須被偵測或列明品質不合格。

### O02 · P2 · 完整性檢查通過的無效模型可以取代正常模型

**位置**：[LanguagePackService.cs](../src/TransLamp/Core/LanguagePackService.cs)，`Import` 第 115–161 行；manifest 檢查第 208 行起。

**重現**：先匯入可真實翻譯的 en-zh 模型；再匯入內容無效、但 manifest 的大小與 SHA256 完全相符的合成 2.0.0 包。匯入成功，原本 82,713,318 bytes 的 model.bin 被 10 bytes fixture 取代，備份刪除，下一次翻譯才回報 `translation-failed`。

**建議**：區分「檔案完整」與「runtime 可載入」。以隔離 worker 在 staging 驗證 tokenizer／模型載入後才替換；若 runtime 不存在，應明確標記未驗證並保留舊版本，不能宣稱已確認可用。仍需接受者決定模型品質，載入成功本身不證明語意品質。

**驗收**：壞 model.bin、壞 tokenizer、無效 vocabulary/config 等包被拒絕時，舊模型仍可翻譯且版本不變。

### O03 · P2 · 移除失敗可能留下半刪除模型

**位置**：[LanguagePackService.cs](../src/TransLamp/Core/LanguagePackService.cs)，`Remove` 第 76–87 行。

**重現**：以不允許刪除的 read handle 持有 model.bin，呼叫 Remove。得到 IOException，但包內原有 8 個檔案僅剩 model.bin；manifest 已消失、安裝列表為 0、移除 marker 尚未寫入。

**建議**：先在鎖內將整個包移到可復原的待刪除目錄，再提交移除狀態；清理失敗可重試。若無法改名，完整保留原包並回報占用。

**驗收**：占用、權限不足、marker 寫入失敗和中斷之後，狀態只能是「完整仍可用」或「已移除、殘檔可清理」，不可默默半毀。

### O04 · P2 · 更新的中斷狀態沒有復原流程

**位置**：[LanguagePackService.cs](../src/TransLamp/Core/LanguagePackService.cs)，第 141–161 行的 target → `.previous-*` → staging 替換流程；安裝列表略過隱藏目錄。

**證據範圍**：隔離測試建立「舊包已搬到備份、新包尚未搬入 target」的磁碟狀態，**沒有真的切斷電源或殺死使用者程序**。新 service 看不到任何可用包；執行 bundled 安裝後啟用 1.0.0，原 9.0.0 仍藏在 `.previous-*`，staging 也保留。

**建議**：加入最小的安裝交易狀態與啟動復原，先恢復最後可用版本，再清理自己建立的暫存目錄。不能只靠正常 exception 的 catch 回滾涵蓋程序中斷。

**驗收**：逐一模擬替換各階段的重啟狀態，不遺失原可用版本、不自行降版、不無限累積 staging。

### O05 · P2 · bundled 安裝存在檢查與寫入之間的競態

**位置**：[LanguagePackService.cs](../src/TransLamp/Core/LanguagePackService.cs)，第 67–70 行的「已安裝／已移除」檢查在 `Import` 的寫入鎖之外。

**重現**：控制隔離測試的排程：A 完成「尚未安裝」檢查並等待；B 手動匯入 9.0.0；A 隨後匯入 bundled 1.0.0。最終版本變成 1.0.0，覆蓋使用者選擇。

**建議**：讓「只在尚未安裝時安裝」的條件與提交置於同一寫入鎖內，並在鎖內重查移除 marker。手動更新與 bundled 補裝的行為需明確區別；不需要擴大成通用交易框架。

**驗收**：交錯執行兩個 service／process 的 bundled 安裝、手動匯入與移除，bundled 不能覆寫已完成的手動選擇。現行單一程序不覆蓋既有包的測試仍須通過。

### O06 · P3 · 匯入接受的語言方向超出實際 runtime 能力

**位置**：[LanguagePackService.cs](../src/TransLamp/Core/LanguagePackService.cs)，`ValidateManifest` 第 213 行起；[MainWindow.xaml.cs](../src/TransLamp/MainWindow.xaml.cs) 第 43 行與 Python worker 的請求驗證。

**證據**：C# 匯入及 Verify 接受合成 `ja-en` 包並列為已安裝；UI／worker 僅提供 en ⇄ zh。probe 的合成模型本身也無法翻譯，因此不以 generic `translation-failed` 單獨推定故障原因；契約差異由各層的明確條件確認。

**建議**：目前 runtime 明確拒絕不支援的方向，或把包標成「已儲存但不相容」；避免顯示已匯入卻永遠不能選用。未來擴充以一份 capability 契約同步，不必現在先建插件架構。

**驗收**：日文、不支援代碼及不相容模型格式，在匯入階段給出可理解且一致的結果；中英正常包維持可用。

### O07 · P2 · 一個壞 bundled 包會阻止後續健康包準備

**位置**：[LanguagePackService.cs](../src/TransLamp/Core/LanguagePackService.cs)，`InstallBundledAsync` 第 60–72 行。

**重現**：排序第一個 archive 是損壞 ZIP，第二個為可接受的健康 fixture。整個批次立刻拋錯，已安裝數為 0，第二包未處理。

**建議**：逐包收集結果，讓其他獨立方向繼續安裝；取消仍應立即停止，最後顯示成功與失敗包，不能把部分成功寫成全部成功。

**驗收**：任一方向損壞不阻止另一方向；所有包失敗、部分成功、取消三種狀態可辨識。

### O08 · P2 · 發布腳本未驗證資源內容

**位置**：[Publish-TransLamp.ps1](../scripts/Publish-TransLamp.ps1)，第 15–21 行。

**重現**：以假的 python.exe、translate.py、VC_redist.x64.exe、無效 resource-build.json 和兩個非 ZIP `.tlpack` 作隔離 fixture；**以局部函式攔截 dotnet publish，不執行假執行檔**。前置檢查仍放行，複製資源並輸出 ready 訊息。這證明發布前置檢查不足，不是聲稱假程式可執行。

**建議**：驗證固定資源清單、檔案 hash、兩個包的方向／版本／manifest、runtime 與 worker 的版本一致性。發布後在最終產物做真實雙向 smoke test，證據需綁定該產物的 hash；過去準備成功的 JSON 不能替代當次驗證。

**驗收**：任一資源被修改、重複方向的兩包、缺失 DLL、舊驗證報告都不能產生「完整離線包已就緒」結果。

### O09 · P2 · 輸出目錄防護不完整，重發保留舊檔

**位置**：[Publish-TransLamp.ps1](../scripts/Publish-TransLamp.ps1)，第 22 行及第 28–39 行。

**重現**：`OutputDirectory = ResourceDirectory/Runtime` 仍可到達 dotnet publish 呼叫；probe 在這裡以 sentinel 終止，沒有執行污染來源的完整複製。另外，輸出中預先存在的 `Runtime/obsolete.dll` 在重發後仍保留。

**建議**：正規化路徑後禁止 source/output 相同或任一方包含另一方。以新的 staging 輸出完整一版、驗證後再交付，避免把舊資料夾直接當成乾淨發布目錄。舊檔存在不代表它一定會被載入，但會使交付內容不再由本次建置決定。

**驗收**：相同、父子、尾端分隔字元／大小寫變體均拒絕；移除過的 runtime 檔與語言包不出現在新包；失敗發布保留上一個可用包。

### O10 · P2 · 長技術步驟在 token 上限處斷開，容易失去步驟與數值關係

**位置**：[translate.py](../runtime/TransLamp/translate.py)，`make_plan`／`token_chunks`。

**重現**：450 字、10 步驟的繁中合成文字，編成 331 tokens，現行切成 256＋75；分界落在「等待｜108 ms」。程式完整涵蓋所有來源 tokens，**未發現直接截斷輸入**，但模型結果重複步驟 06／07、漏掉步驟 08 與數值 32，共有 7 種數字計數不符。

**隔離候選**：按分號分成 10 子句後，同例的數字計數全部吻合，仍將「韌體」翻成 body。另確認 `Dr. Smith` 與 `1. Stop…` 的縮寫／編號會被現行句點規則拆開。證據：[segmentation.json](../artifacts/translamp-review/runtime/segmentation.json)。

**建議／驗收**：在 token 上限前優先找分號或合理子句邊界，保留序號與單位的上下文；維持 no-truncation、換行及 EOS 檢查。以縮寫、清單、無標點長句、中英混合、長識別字測試，不能用「數字吻合」取代人工語意檢查。上述候選尚未套用。

### O11 · P2 · 已開視窗無法感知另一實例匯入的語言包

**位置**：[MainWindow.xaml.cs](../src/TransLamp/MainWindow.xaml.cs)，第 16／36／116／146／196 行的快取與可用性判斷。

**重現**：視窗先讀到空列表；第二個 LanguagePackService 真實匯入 en-zh；對舊視窗反射模擬 `OnActivated` 並編輯原文，列表仍為 0、翻譯按鈕停用。直接呼叫 `RefreshPacks` 或建立新視窗才變為 1 並啟用。

**測試界線**：沒有 Show 視窗或操作實體桌面；為 UI 可用性準備的空 runtime 檔案只用於存在性判斷、從未執行。這是狀態邏輯重現，不是新的 GUI 翻譯驗收。

**建議／驗收**：未忙碌時在視窗啟用／開啟管理面板時重新讀取，必要時提供重新整理；先不要引入常駐檔案監聽服務。兩視窗匯入／移除後，舊視窗能更新狀態，且不清除尚未翻譯的原文。

### O12 · P3 · 原文變更後，完成或錯誤狀態仍留在畫面

**位置**：[MainWindow.xaml.cs](../src/TransLamp/MainWindow.xaml.cs)，`SourceTextChanged` 第 116–122 行、`InvalidateResult` 第 129–134 行。

**重現**：以明確標示 SYNTHETIC 的完成／錯誤狀態進行 probe，修改或手動清空原文後，譯文已清除且 Copy 停用，但舊狀態與錯誤顏色仍保留。沒有虛構真實翻譯成功。

**建議／驗收**：原文變更時切回「原文已變更，請重新翻譯」或空白就緒狀態；操作進行中保留進度／取消狀態。測試完成→編輯、錯誤→編輯、手動全選刪除及切換介面語言。

## 已量測的優化候選

### 先做小批次，再評估常駐模型

[batching.json](../artifacts/translamp-review/runtime/batching.json) 使用相同模型、CPU int8、4 threads，將逐段呼叫改為 batch＝4：

| 方向／樣本 | 逐段推論 | batch＝4 推論 | 已知限制 |
| --- | --- | --- | --- |
| en → zh，10 段 | 約 0.212 秒 | 0.131–0.132 秒 | 該樣本 10／10 段輸出相同 |
| zh → en，8 段 | 約 0.180 秒 | 0.099–0.101 秒 | 1／8 段輸出變動，line 變 network；不能宣稱品質無回歸 |

最終小量重測的純推論約減少 38%／44%，**不包含 C# hash、process／模型載入或 UI 時間**；樣本少且僅一台開發機，不能直接宣稱整體速度或舊 PC 提升相同比例。建議先試小批次，保留原順序、每批進度與取消，再重跑全部品質案例。

[profiles.json](../artifacts/translamp-review/runtime/profiles.json) 的分項量測：套件 import 約 53 ms、tokenizer 27–30 ms、模型載入 95–246 ms、短句推論 21–24 ms，planner 只有約 0.28–0.33 ms。優化 planner 的速度收益很低；改分段是為內容品質。

另外，實際 production worker 新 process 各方向兩次短句約 0.262–0.266 秒，未清除 OS 磁碟快取、未包含 C# 檔案驗證，不能稱為真正冷機啟動。分項 probe 內部包含三次推論，不能拿它的 subprocess 全程與單次 worker 混算。

常駐模型確實可節省載入，但會增加取消、模型更新鎖定、閒置釋放及多實例的管理成本，暫排在小批次與品質修正之後。短句 probe 峰值 working set 約 162 MiB，而 private bytes 約 922 MiB；兩個數值定義不同，不能只報前者就聲稱 4 GB 機器無壓力。尚未量測整個 WPF＋worker 組合的峰值。

### 技術 literal 保護先提示，不能盲目置換

[protection.json](../artifacts/translamp-review/runtime/protection.json) 對 6 句做三種隔離候選實驗：文字型 `ZXQ0QXZ` 佔位符完整保留 5／6 句、`[[TL0]]` 為 2／6，atomic unknown 方案為 0／6。

其中 IP／port 例句還原後成為 `The port of 192.168.1.10 for server 502 did not respond.`，數字雖在，角色已互換。因此不建議無條件採用佔位符還原；先做精確 literal 缺失／變形提示，並把角色關係納入人工品質評估。`ten → 10` 是合理數字表達轉換之一，不能把所有新增數字一律當錯誤。

### 易用性與體積的低優先項目

- 可記住上次翻譯方向與雙欄比例；是否保存原文／譯文仍維持目前不保存的原則。
- 語言包以「中文 ⇄ English」作使用者理解的群組，必要時再展開單向包；不要讓使用者必須理解底層模型才能操作。
- 安裝進度可顯示目前檔案與大小；目前長時間停留在不確定進度，較難區分工作中與卡住。
- 缺 VC++／native 元件時，提供可辨識的本機診斷與正確先決元件指引；目前 IsAvailable 只看兩個檔案存在。
- Offline Kit 解壓約 427 MiB，首次安裝兩個模型再增加約 162 MiB。先盤點重複資源與 native 相依，再決定體積優化；不應直接刪 DLL／授權檔或移除 self-contained .NET，破壞離線部署。

## 診斷證據與限制

- 無視窗 UI probe：**12／12 檢查通過**，重現 O11／O12；[原始碼](../artifacts/translamp-review/ui/Program.cs)、[結果](../artifacts/translamp-review/ui/results.json)。三種語言在 900×720 DIP 下所查主要控制邊界皆在範圍內；900×650 DIP 的品質頁腳超界，但低於現行 MinHeight＝720，僅作緊湊佈局候選依據，不列為已確認高 DPI 缺陷。
- 本次 C# core 既有回歸：**29／29 通過**，包含 2 個真實模型整合測試。新缺陷是在既有測試以外的隔離 probe 重現，不代表上述舊測試失敗。
- [Core probe 原始碼](../artifacts/translamp-review/core/Program.cs)、[五項重現與初次方向測試](../artifacts/translamp-review/core/run-20261002-165110/results.json)、[方向測試補跑](../artifacts/translamp-review/core/run-20261002-165212/results.json)。初次方向測試遇到環境 rename denied，已單獨補跑，不列為產品缺陷。
- [打包 probe](../artifacts/translamp-review/packaging/Probe-Publish.ps1)、[打包結果](../artifacts/translamp-review/packaging/results.json)。dotnet 為 stub，沒有做新的正式發布。
- 本次 Python 單元測試 **11／11 通過**；另完成技術 literal、分段、四次真實短句 worker 與小批次探針。[runtime 詳細紀錄與命令](../artifacts/translamp-review/runtime/runtime-review.md)、[單元測試輸出](../artifacts/translamp-review/runtime/unit-tests.txt)。
- 先前完整 45／45 C# 測試、10 項 worker 檢查與 12 張介面圖，見[實作紀錄](TRANSLAMP-IMPLEMENTATION.md)。此處不把先前結果當成本次重新執行。
- `artifacts/` 是本機忽略目錄；診斷附件供重現與審閱，尚未成為正式回歸測試。後續修正時應把必要案例搬入 tests，並以修正前失敗、修正後通過驗證。

## 發布與產品缺口

以下是尚未驗證或尚未完成的規格項目，不與本次已重現缺陷混計：

| 範圍 | 下一步與完成條件 |
| --- | --- |
| 全機離線 | 在從未準備過 runtime 的乾淨 Windows 目標機測試 USB 部署、重啟、兩方向、取消及語言包匯入；以作業系統層觀察網路行為。Python socket guard 只代表已測的 Python 路徑。 |
| 舊硬體 | 4 GB RAM、舊 CPU、Windows 10/11、100／1,000／20,000 字，量測首筆/後續延遲、峰值記憶體、UI 反應；目前單機結果不能推為最低需求。 |
| 高 DPI／可及性 | 實體 125／150／200% DPI、小螢幕、鍵盤焦點、螢幕閱讀器與高對比驗收。離屏 DIP 渲染不能取代實機結果。 |
| 安裝／入口 | 正式 Installer／MSIX、`translamp://open` 登錄、Store 身分及通路共存仍未完成。現有本機候選與 URI parser 不等於已安裝 protocol。 |
| 模型取得 | 線上語言包目錄、UI 下載、來源驗證及更新策略尚未完成；離線匯入已具備。更多語言不可先標成可用。 |
| 散布資源 | 建立實際二進位清單、來源、版本及授權／notice 對照，安排固定 runtime 的安全維護更新。現有模型文本與 hash 是基礎，尚未完成全部 native 相依盤點。 |
| 中文輸出 | 目前以簡體為主；繁體切換屬產品選項，不能把機械繁簡轉換當作翻譯品質修復。 |

## 建議執行順序

1. 先把數字／技術 literal／角色與否定語意的缺陷變成可重現品質案例，建立最低可接受條件；改善模型前保留明確的問題提示。
2. 修正語言包替換／移除的失敗復原與鎖內條件，再補 runtime 可載入檢查和方向契約；優先局部修改。
3. 完成發布資源檢查與乾淨 staging 輸出，讓最終 Offline Kit 綁定當次驗證證據。
4. 再修介面狀態同步與提示、量測效能、完成乾淨機與舊硬體驗收。

保留目前的共用 AppWindow、工具番竹紋樣式、CPU 離線推論、取消 process tree、檔案白名單／hash、保留原文與不寫入翻譯歷史的設計。沒有證據需要為這些優化重寫整個架構，也不建議此時擴張 OCR、PDF 翻譯、雲端 API 或帳號功能。
