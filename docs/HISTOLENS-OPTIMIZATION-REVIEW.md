# 006 HistoLens 全面檢查與優化清單

檢查日期：2026-10-03。對象為目前工作目錄的 **006 合成資料開發預覽**，包含核心、保存與載入、介面、宿主整合、測試及交付流程。本次沒有修改產品程式或正式測試設定；新增的診斷程式與證據放在 `artifacts/histolens-review/`。

建議先修正 **排序、失敗時保留成果、載入完整性**，再處理資料來源接入前的條件政策與大量結果保存。既有測試通過不代表下列新邊界已涵蓋。沒有證據顯示需要更換技術棧或全面重構。

## 範圍與結論

| 範圍 | 本次檢查方式 | 結論與限制 |
| --- | --- | --- |
| 核心公式、時間軸、採樣、診斷 | 程式／規格比對、既有測試、獨立合成資料探針 | 基準計算通過；缺量與零均量走不同 AND 政策，見 O06 |
| 保存、載入、一致性 | 實際建立及載入合成研究檔；大筆数保存量測 | 可重現半套載入、身份不一致及容量上限，見 O03／O04／O07 |
| 介面操作與案例圖 | STA Dispatcher 診斷、控制項排序／切換／取消 | 可重現排序、狀態、語言及資料順序問題；未做人工多螢幕／DPI 驗收 |
| 宿主入口與生命週期 | 006 路由／偏好路徑盤點、94 項定向測試 | 序列執行通過；預設平行執行不穩定，見 O12 |
| 效能與取消 | 10,000 筆資料、不同回看／觀察長度 | 此機計算均低於 2 秒；短期配置量偏高，見 O11 |
| 免費資料、正式封裝 | 核對已交付文件與目前能力 | 仍為既有未完成項目；本次未下載真實行情、未新增服務或商業承諾 |

此處「全面」指 006 現有實作及其直接整合，不是工具番全系列程式碼審查。其他產品既有未提交修改保留。優先級：P2 為影響結果解讀、工作保留、目標工作流或驗證可靠性的問題；P3 為操作品質、極端輸入及局部效能改善。下列項目尚未修正。

## 優先清單

| ID | 優先 | 項目 | 觸發範圍 |
| --- | --- | --- | --- |
| O01 | P2 | 百分比／首次觸及日依文字排序 | 現有 Demo 可操作 |
| O02 | P2 | 被資料條件阻擋的執行覆蓋上次完整結果 | 現有 Demo 可操作 |
| O03 | P2 | 不完整研究檔造成半套載入，更新旗標可能卡住 | 匯入結構異常、但校驗碼有效的合成檔 |
| O04 | P2 | 結果身份未完整綁定快照身份 | 匯入不一致合成檔 |
| O05 | P2 | 等價快照的日線順序影響圖表 | 匯入順序不同的合法合成檔 |
| O06 | P2 | 缺量與零均量的 AND 政策不一致 | 額外合成邊界；真實 Provider 接入前需處理 |
| O07 | P2 | 10,000 筆密集事件可計算但不能保存 | 目標資料规模；目前內建 Demo 未達此量 |
| O08 | P3 | 首次運算因編輯取消後仍顯示研究中 | 現有 Demo 可操作 |
| O09 | P3 | 切換語言未刷新案例說明 | 現有 Demo 可操作 |
| O10 | P3 | 極端上方門檻到運算中才溢位 | 現有 Demo、極端輸入 |
| O11 | P3 | 重複窗口品質檢查配置大量短命物件 | 10,000 筆長窗口量測 |
| O12 | P2 | 宿主 WPF 測試平行執行不穩定 | 測試流程，未證實為產品故障 |
| O13 | P3 | 預覽交付物缺少完整重建步驟 | 開發／交付流程 |

## 問題證據與最小改善方向

### O01：排序使用格式化文字

- 位置：`src/HistoLens/MainWindow.Presentation.cs:64–75,131–141`；`MainWindow.cs` 的 `Column` 綁定。
- 重現：Demo 預設研究後，對案例的 `Change` 套用升冪排序，57 個有效觀察結果有 **41 對相鄰數值逆序**；例如 -0.599151% 排在 -0.633208% 前面。這是文字比較結果；首次觸及日也以字串提供，會有 10 與 2 的排序問題。
- 影響：使用者無法可靠找出最大／最小變動。完整研究統計本身未因排序而改變。
- 建議：保留 decimal／int 原值作為 `SortMemberPath` 或欄位資料；顯示才格式化。含日期／比率的複合欄位明訂排序欄位。
- 驗收：負數、正數、0、空值、2／10 日皆按數值排序；排序不改變研究結果物件與有效 N。

### O02：被阻擋的研究取代上一份成果

- 位置：`src/HistoLens/MainWindow.cs:181–187`。
- 重現：先完成 Demo 的 15 個事件，再把研究截止日改為快照截止日的隔天並執行。引擎回傳 `IsResearchAllowed=false`；介面先指定 `Result`，事件數變成 0，前次物件不再保留。
- 影響：正常參數操作就會清掉尚未保存的可見研究；與規格 17.2「取消與失敗不覆蓋上一份完整研究」的工作保留要求不一致。
- 建議：先判斷執行結果能否成為完整研究；阻擋原因以獨立狀態呈現，舊結果保留且明示為前次成果。
- 驗收：資料品質阻擋、非法截止日與例外都保留上一份完整結果，並可辨別新執行失敗原因。

### O03：載入不是完整成功後才替換

- 位置：`src/HistoLens/ResearchStore.cs:64–73`；`MainWindow.cs:279–301`；`MainWindow.Presentation.cs:64`。
- 重現 A：使用現有 `SaveAsync` 保存 `Statistics=null` 的合成研究，校驗碼和快照 hash 均有效。載入時拋 `ArgumentNullException`，但 `Result` 已被替換，`Statistics` 仍為 null。
- 重現 B：保存 `UpperThreshold=decimal.MaxValue` 的研究。載入已指定 `Snapshot`／`Result`，之後在顯示百分比乘 100 時溢位，`_updating` 保持 true；後續編輯不再標記結果過期。
- 影響：UI 捕捉例外後宣稱「保留原工作」，實際並未保留；可能需要重新開啟視窗才能恢復正常編輯追蹤。一般由引擎生成的正常檔案未重現此問題。
- 建議：先驗證集合、巢狀物件、設定範圍與必要結果結構，預先建好顯示資料後才切換狀態；更新旗標用 `try/finally` 還原。不要只捕捉例外後改顯示文字。
- 驗收：校驗碼錯誤、內容結構異常、模板不支援、數值無法顯示等情況，都保留舊 `Snapshot`／`Result`、表格與可編輯狀態。

### O04：快照雜湊不能代替結果身份驗證

- 位置：`src/HistoLens/ResearchStore.cs:64–73`。
- 重現：保留 `DataContentHash`／`DataSnapshotId`，但將研究標的改成 `DIFFERENT`、來源改成 `different-source`；Save／Load 皆接受，而快照標的仍是 `DEMO-006`。
- 影響：摘要身份與實際案例日線可互相矛盾。checksum 可檢查檔案內容是否一致，不代表研究語意必然有效；這不是身份認證或外部攻擊證明。
- 建議：驗證研究與快照的 InstrumentId、Code、來源、資料版本／截止日等必要契約，以及結果的觀察期與設定一致性。避免為此新增簽章或遠端服務。
- 驗收：不一致檔案明確拒絕且保留現有工作；正常舊版本檔案按已定義相容政策載入。

### O05：圖表沒有排序資料

- 位置：`src/HistoLens/MainWindow.Presentation.cs:121–123`；`PriceChart.cs:27–43`。
- 重現：把正常 Demo 快照的 `Bars` 反轉，內容 hash 仍相同；保存及載入成功，案例原始表格的 9 筆日期為 2023-07-19 到 2023-07-07，8 對均倒序。
- 影響：圖表以第一／最後一筆計算時間跨度，又直接依輸入順序連線；倒序輸入會產生錯誤時間座標。引擎依日曆計算的統計未因這項順序改變。
- 建議：送入案例表格與圖表前依日期排序；保留原始快照不必改寫。圖表可對輸入排序契約作明確防護。
- 驗收：正序、倒序、打亂但內容相同的快照，研究值、表格順序、時間軸與事件線相同。

### O06：缺量與零均量走不同 AND 政策

- 位置：`src/HistoLens/Core/ResearchEngine.cs:178–206`。
- 重現：HL-R003，回看 2 日、相對量門檻 1，收盤序列 102、101、100、102、103，研究第 3／4 筆。第 1 筆 Volume=null、第 2 筆 0，其餘 100。第 3 筆價格條件明確不成立，但缺量使所有條件一起 Unknown；第 4 筆兩條件成立，卻被 `EventStartUnknown` 排除，案例為 0。
- 對照：只把第 1 筆 Volume 改為 0，前日相對量仍因零均量 Unknown，但 AND 變為 NotMatched；第 4 筆正常採樣，案例為 1。
- 判讀：已確認兩條不可計算路徑會影響採樣；現有 Demo 沒有缺量。程式註解採「任一可計算條件 false，AND 即 false」，但缺量的前置品質閘門採全體 Unknown。需要固定資料品質與條件邏輯的邊界，不能默默更改研究規則。
- 建議：若沿用逐條件三態 AND，MissingVolume 只影響需要量的條件，再做三態合併；若要整窗資料不完整一律 Unknown，則要一致處理並明載政策／版本。
- 驗收：同一 false AND unknown 語意不因缺值或零分母的實作分支而改變；FirstInRun 的排除原因與事件起點可核對。

### O07：保存容量與大量運算目標不一致

- 位置：`src/HistoLens/ResearchStore.cs:20,29–35`；`MainWindow.cs:240–249`。
- 同一探針生成明示合成日曆的固定價格序列，HL-R001／回看 120／EveryMatch／5、10、20、60 日；量測單次保存，不包含引擎計算：

| 日線 | 事件 | SaveAsync 回傳 Task 前的同步時間 | 保存結果 |
| ---: | ---: | ---: | --- |
| 780 | 660 | 39.1 ms | 成功，3,639,660 bytes |
| 10,000 | 9,880 | 559.5 ms | `InvalidDataException`，超過 16 MiB 開發上限 |

- 影響：目標規模可算出結果，卻無法保存；大量 JSON 序列化在第一個 await 前執行，從 UI 呼叫時會佔住 UI 執行緒。第二次量測後整個探針程序 working set 為 654,622,720 bytes，包含前面所有 UI／載入探針，**不是保存單項峰值或產品穩態記憶體**。
- 建議：先決定開發版支援的保存規模，避免最後才拒絕；將序列化／hash 移出 UI 執行緒，評估精簡 JSON 或串流。若改檔案格式，保留舊版讀取與明確版本，不宜只無限制提高上限。
- 驗收：10,000 筆密集事件完成保存與載入一致性，或在執行前清楚顯示可保存上限；保存過程維持 UI 回應並處理取消。

### O08：編輯取消首次運算後留下「研究中」

- 位置：`src/HistoLens/MainWindow.cs:189–196,229–235`。
- 重現：首次啟動 Demo 運算後立即把上方門檻改成 7；結束時無結果、執行按鈕可用、取消按鈕不可用，但狀態仍為「研究中…」。
- 建議：沒有舊結果時，編輯也要更新待執行／已取消狀態；generation 不同時避免舊工作改狀態的原則應保留。
- 驗收：首次與已有結果兩種情況的取消狀態、按鈕及資料一致。

### O09：案例文字未隨語言切換刷新

- 位置：`src/HistoLens/MainWindow.cs:320–325`；`MainWindow.Presentation.cs:124–125`。
- 重現：選好案例後從繁中改英文，欄位語言改變，案例下方「收盤價格路徑……」仍為中文；重新選案例才會刷新。
- 建議：Translate 更新選中案例說明；現有診斷文字／枚舉翻譯的完整性仍按原待辦處理。
- 驗收：三語切換不重跑研究、不改選中案例，所有已實作翻譯的區塊立即一致。

### O10：極端門檻延後到運算時溢位

- 位置：`src/HistoLens/Core/ResearchEngine.cs` 的 `ValidateDefinition` 及 `Observe:283`。
- 重現：Demo 上方門檻設為 `decimal.MaxValue / 100`，通過定義驗證，在 `P0 * (1 + threshold)` 拋 `OverflowException`。UI 能捕捉失敗，沒有證據顯示程序崩潰。
- 建議：按研究能力驗證輸入範圍或使用不溢位的比較方法，提供欄位錯誤。不要引入未經定義的市場漲跌幅硬限制。
- 驗收：極端值在工作開始前被拒絕或可安全計算，並保留原成果。

### O11：可局部降低核心短命物件配置

- 熱點：`SnapshotValidator.GetBarReasons:80–90` 每次建立 List／Distinct／Array；`ResearchEngine.cs:176,269` 在多個重疊窗口反覆呼叫。
- 同一程序先暖機，以下列第二次量測；每組為 10,000 筆、6 條件、EveryMatch。資料與配置量測方法詳見探針，未包含 UI／磁碟：

| 最長回看 | 觀察期 | 時間 | 當前執行緒累計配置量 |
| ---: | --- | ---: | ---: |
| 120 | 5／10／20／60 | 196.3 ms | 384,663,112 bytes |
| 2,500 | 5／10／20／60 | 875.5 ms | 3,149,266,464 bytes |
| 120 | 5／10／20／2,500 | 1,367.0 ms | 3,743,075,912 bytes |

- **配置量不是常駐記憶體**。三組都低於此機的 2 秒目標；與先前測試程序的計時環境／資料不同，不能宣稱此次已優化或直接比較加速倍率。
- 建議：先每次研究預先計算各 bar 的品質原因，避免窗口內重建；若仍有必要，再以相同 fixture 量測局部彙總改善。保留原因順序、取消檢查、decimal 口徑及排除政策。
- 取消探針三次在約 100 ms 提出請求，觀察到延遲 2.05／0.45／0.41 ms；未出現完成半份結果或不取消的證據。

### O12：宿主测试的平行執行不穩定

- 位置：失敗堆疊包括 `ToolKeeper.UI/WindowFrame.cs:139` → `AppWindow.cs:72` → WPF `DependencyObjectPropertyDescriptor.AddValueChanged` 的 Dictionary 更新。
- 預設設定連跑兩次，均為 93／94；失敗案例不同，皆報 non-concurrent collection 的並行更新錯誤。單獨重跑其中失敗測試為 1／1。
- 僅以證據目錄的 `.runsettings` 設 `ParallelizeTestCollections=false`，原批次 94／94 通過。設定格式依 [xUnit 官方 RunSettings 文件](https://xunit.net/docs/config-runsettings) 核對。
- 判讀：支持測試平行化與 WPF 狀態交互作用的假設；一次序列成功不能證明根因，也不能直接判定正式產品有同一競態。
- 建議：針對共用 WPF 狀態的測試集合隔離執行緒／平行範圍，再追蹤穩定度；避免為使測試通過直接更改產品行為或關閉所有非 UI 測試的平行化。
- 驗收：保留最初失敗紀錄，確認一組明確 runner 設定下多次完整批次穩定，並記錄失敗是否消失；不以單次綠燈替代根因結論。

### O13：預覽產物不能完全由現有文件命令重建

- 位置：`docs/HISTOLENS-IMPLEMENTATION.md` 的可重跑命令；`tests/HistoLens.Tests/MainWindowTests.cs:34–40`。
- 現有 test／publish 命令不足以重建六張 PNG、TRX、範例 JSON、`Start-HistoLens.cmd` 及隔離 profile；範例與圖片另需 `HISTOLENS_SCREENSHOT_DIR`，啟動檔及 profile 目前屬本機產物。
- 建議：補一個範圍明確的 preview 準備腳本或完整命令，檢查輸出位置，產生同樣的隔離啟動設定；產物繼續留在 artifacts，不需把二進位檔加入版本控制。
- 驗收：乾淨輸出目录依單一流程可重建預覽與證據；雙擊只使用隔離 profile／空桌面來源。

## 真實資料接入前的門檻與既有待辦

以下不是本次新證實的 Demo 故障：

1. 免費來源、歷史日線、實際交易日曆及公司行動覆蓋仍待接入；沿用[實作文件的官方候選與限制](HISTOLENS-IMPLEMENTATION.md)，目前沒有真實股票研究可供驗收。
2. 零成交與交易狀態一致性需明定於 Provider／validator 邊界。探針 `TradedZeroVolume` 在 `Status=Traded, Volume=0` 時仍得到有效 +5% 結果且無診斷；規格 6.3 要求零成交用狀態表示。接入時應拒絕或正規化矛盾來源資料，不能直接把量缺失或 0 當成有效成交，也不能未定義規則就改寫來源價格。現有 `ResearchEngineTests.cs:355` 的零均量測試也使用 Traded 零量，須同步核對規格與測試政策，不能直接修改行為後宣稱語意未變。
3. 自訂六條件編輯器、真實下載／更新、來源權限控制、完整圖表、匯出／保存管理、獨立產品／MSIX 皆是原有未完成能力，沒有因這次檢查而新增範圍。
4. 人工鍵盤操作、跨螢幕 DPI、螢幕閱讀器及真實網路中斷情境未驗證。此次為讀碼、既有測試與離屏控制項診斷。

## 建議執行順序與驗收批次

1. **現有預覽可靠性**：O01、O02、O03，並將 O08／O09 的小範圍狀態修正一起驗收；各自補能重現問題的行為測試。
2. **檔案與資料契約**：O04、O05、O06、O10；固定條件未知政策並保留舊檔相容性，完成後再接真實 Provider。
3. **目標資料規模**：O07，先確保結果可保存；O11 以量測决定是否值得做局部配置改善，不預先建大型快取或新服務。
4. **可重複交付**：O12、O13；穩定測試執行與預覽生成流程。既有免費資料／正式產品待決策事項仍集中在實作文件。

## 驗證紀錄與重現

- 本輪 Release HistoLens 既有測試 **53／53 通過**。
- 宿主定向測試：預設並行兩次均 **93／94**；單獨失敗案例 **1／1**；序列診斷 **94／94**。不將本輪概括成「全部測試一次通過」。
- UI／Store 探針實際執行成功並輸出預期診斷；探針使用 reference DLL 與隔離本機資料夾，不修改產品程式。異常檔案只供本機重現，不是正常 demo 範例。
- 環境：Intel Core Ultra 7 255HX、20 核心／20 邏輯處理器、RAM 33,752,997,888 bytes、ZHITAI TiPlus7100s 2TB NVMe、Windows build 26200、.NET 10.0.12、x64。量測期間可能有其他檢查程序；數值為診斷樣本，非嚴格基準競賽。

證據入口：

- [UI／保存探針原始碼](../artifacts/histolens-review/ui/Program.cs)、[9 組結果](../artifacts/histolens-review/ui/evidence/results.json)、[順序探針結果](../artifacts/histolens-review/ui/evidence/results-order.json)。
- [核心探針原始碼](../artifacts/histolens-review/core/Program.cs)、[正確性邊界](../artifacts/histolens-review/core/correctness.jsonl)、[效能](../artifacts/histolens-review/core/performance.jsonl)、[取消](../artifacts/histolens-review/core/cancellation.jsonl)。
- [宿主測試摘要](../artifacts/histolens-review/baseline/baseline-summary.md)、[序列重跑 TRX](../artifacts/histolens-review/baseline/histolens-host-serialized.trx)、[完整 log](../artifacts/histolens-review/baseline/host-serialized.log)。

從專案根目錄執行：

```powershell
dotnet build src/HistoLens/HistoLens.csproj -c Release
dotnet run --project artifacts/histolens-review/ui/Probe.csproj -c Release
$env:HISTOLENS_REVIEW_MODE = 'order'
dotnet run --project artifacts/histolens-review/ui/Probe.csproj -c Release
Remove-Item Env:HISTOLENS_REVIEW_MODE
dotnet run --project artifacts/histolens-review/core/CoreProbe.csproj -c Release -- correctness
dotnet run --project artifacts/histolens-review/core/CoreProbe.csproj -c Release -- performance
dotnet run --project artifacts/histolens-review/core/CoreProbe.csproj -c Release -- cancel
```

這些診斷原始碼／證據為本機 artifacts，未加入正式測試專案或 Git 追蹤；需長期保留時，優先將確認問題的最小重現轉為正式測試。

效率紀錄：本輪主任務 `TaskRunId=940`，核心檢查／宿主驗證分工執行，由主代理驗收與彙整。代理工具未提供獨立關閉介面；只確認完成狀態，`ClosedNormally` 保留未知。使用者要求重新載入設定後，已重讀 5 個 agent 設定；現有執行中代理是否支援熱套用未經確認，不將檔案設定值冒稱為實際執行值。
