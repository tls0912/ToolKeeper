# HistoLens 個人版上櫃歷史下載

> 歷史查證／舊版紀錄：0.3.16 已依使用者決策只保留 FinMind。本文的官方連線及操作不再是現行功能，舊官方下載器與線上驗證腳本已移除；保存本文作證據追溯。目前流程與限制見 [FinMind 接入紀錄](HISTOLENS-FINMIND-IMPLEMENTATION.md)。

更新日期：2026-10-04。適用於使用者這次明確允許 TWSE／TPEx 網站來源的**個人版**；延續原始價格掃描及事件註記，不建立還原股價。先前「尚未接入上櫃」的診斷是當時狀態，本文件記錄後續實作。

## 取得流程與契約

`TpexHistoricalDataProvider` 實作共用 `IMonthlyHistoricalDataProvider`。請求必須明確指定 `Market="TPEx"`，目前只接受四位數、現行上櫃普通股，日期自 2011-01-01 起、不得早於所核對的上櫃日、不得包含台灣時間今天，單次最多十年。這是個人版提供者的支援範圍，不是完整十年已逐檔驗證的宣稱。

只有使用者按下載才連線：先核對身分，再依缺失月份取得獨立市場交易日期、個股原價與成交量，最後取得對應期間已公布的除權息。所有請求序列執行，預設每次間隔 3 秒，單次逾時 45 秒、回應上限 8 MiB；無自動重試、代理或存取控制繞過。

| 用途 | 實際資源 | 核對與限制 |
| --- | --- | --- |
| 證券穩定身分 | `https://isin.twse.com.tw/isin/e_C_public.jsp?strMode=4` | 英文 ISIN 名冊；必須位於 `Stocks`、市場為 `TPEx LISTED`、CFI 為 `ESVUFR`；保留 ISIN 及上櫃日，正確解析 Big5 |
| 上櫃公司主檔 | `https://www.tpex.org.tw/openapi/v1/mopsfin_t187ap03_O` | `SecuritiesCompanyCode`、`CompanyAbbreviation`、`DateOfListing`；與 ISIN 上櫃日一致才接受，不能用現行主檔拼接不同存續期 |
| 個股月行情 | `https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock?date=2025/07/01&code=6488&response=json` | 核對 root／table 日期、root 代碼、subtitle 代碼、唯一表格、欄位、`totalCount` 與重複日期；民國日轉公曆，空值及 `--` 保留空值 |
| 獨立市場日期 | `https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingIndex?date=2025/07/01&response=json` | 日成交量值指數月報的實際市場交易日期；沒有用該股票有行情的日期反推開市日，也沒有用週末／國假補日期 |
| 已公布除權息 | `https://www.tpex.org.tw/www/zh-tw/bulletin/exDailyQ?startDate=2025/07/01&endDate=2025/07/31&response=json` | 在同年度內按所需期間查詢，再以代碼篩選；檢查期間回聲、日期、欄位及筆數；僅保留事件日與官方事件類型 |

本次身分實測：6488 環球晶的兩份官方名冊均為上櫃日 **2015-09-25**；ISIN **TW0006488000**，穩定識別為 `TPEx:TW0006488000`。不是把股票代碼直接當成跨市場身分。

## 原價、數量與事件

來源月報欄位為「日 期、成交張數、成交仟元、開盤、最高、最低、收盤、漲跌、筆數」。已正規化日期欄空白，但不猜測未知欄名／單位。普通股張數或仟股乘 1,000 成為內部股數，成交仟元乘 1,000 成為元；`OriginalVolumeUnit` 仍保存 `lots` 或 `thousand-shares`，`TpexVolumePrecision` 診斷明示**換算不增加來源精度，不能冒稱已取得精確零股數**，且官方個股月報不含鉅額交易。

來源顯示成交量 0 張但仍有價格時，不將整日錯標為停牌／無成交，另留來源精度提示。沒有行情的市場日保持缺漏；不補 0、不移動日期。OHLC 不作任何公司行動調整。

2025-07-16 的官方 `exDailyQ` 已實取：6488 為除息，現金股利 6 元、前收 310 元、除息參考價 304 元。當天 `tradingStock` 的漲跌欄是數值 18.50，並沒有寫「除息」，因此事件註記來自獨立官方結果表，不能只靠月報漲跌或股價跳動推定。

已知事件使用 `ExDividend`／`ExRight`／`ExRightAndDividend` 等類型及 `TPEx/exDailyQ` 來源。`ActionCoverage.IsVerified=false`、`ComparabilityCoverage=null`，明確保留所有公司行動尚未核實完整；相似度掃描照原價執行並保留警示。除權息下載失敗時，保留已下載的原價，停止後續事件請求並記錄原因；不把失敗解讀成沒有事件。

## 快取與失敗處理

提供者回傳實際查詢的 `CheckedPriceRanges` 與 `VerifiedCalendarRanges`，缺月查詢以已完成月份日範圍回傳；共享快取依明確市場及穩定身分隔離。月份資料不會冒標成 TWSE。行情、日曆或身分的 HTTP／解析失敗會拋出，既有快取依原子保存流程保持原狀。

快照保存標準化資料、來源、截止日、本機取得時間、原始單位、身分／行情／日曆／事件回應的內容雜湊版本及快照雜湊。來源沒有提供的逐列修訂版本不自行發明；維持既有缺月／缺日補抓策略，尚未新增自動歷史修訂輪詢。

## 驗證

```powershell
dotnet test tests/HistoLens.Tests/HistoLens.Tests.csproj --no-restore `
  --filter FullyQualifiedName~TpexProviderTests --verbosity minimal
```

本次 31 個離線合成案例通過，涵蓋市場與普通股身分、獨立上市櫃日一致性、缺日、原價與數量單位、缺價、0 張精度、事件日、事件來源失敗、只補缺月、錯期間／代碼／筆數／欄名／單位／重複日期、HTML、HTTP 429、大小上限及取消。測試不提交真實原始行情。

少量真實流程驗證使用忽略目錄下的臨時程式：

```powershell
dotnet run --project artifacts/histolens-tpex-live-20261004/Live.csproj -- `
  artifacts/histolens-source-verification/tpex-live-20261004-1450
```

程式使用正常提供者：先下載 6488 的 2025 年 6 月，再延伸到 7 月，接著重複相同期間，核對第二次只補 7 月、第三次沒有新請求，再從磁碟重開、檢查日期與雜湊並執行原價相似度掃描。輸出目錄必須全新，避免覆蓋原始證據。實際結果由同目錄 `summary.json` 保存。

2026-10-04 台灣時間 **14:51:23 至 14:53:03** 的實際端到端試跑全部通過：

| 驗證項目 | 實際結果 |
| --- | --- |
| 網路請求 | **10 次序列 GET，全部 HTTP 200**；兩輪身分核對各 2 次，加上每月行情、日曆與事件各 1 次 |
| 第一次六月 | 21 筆行情，下載 1 個月 |
| 第二次延伸七月 | 僅補 7 月 23 筆；原六月 30 個已核對日曆日略過 |
| 第三次同期間 | 下載月數 0、新增網路請求 0；61 個日曆日均重用 |
| 行情與日曆 | 共 **44 筆行情、44 個市場交易日**，2025-06-02 至 2025-07-31；缺日 0、日曆外行情 0 |
| 公司行動 | 2025-07-16 `ExDividend`，來源 `TPEx/exDailyQ`；完整事件覆蓋仍標未知 |
| 磁碟重開 | TPEx 穩定身分與內容雜湊一致 |
| 原價掃描 | 5 交易日窗口、門檻 0 的驗證設定；`IsAllowed=true`，35 個候選均可計算，保留 6 個不重疊結果；事件未知診斷仍存在且不阻擋 |

這是兩個月份的下載、增量、離線載入與掃描驗證，**不是五年或十年全量驗證**。真實 raw 回應、請求時間／狀態／雜湊、快取與 summary 全部僅留在被忽略的 artifacts 目錄。

整合後 Release 完整 HistoLens **488／488**、必要宿主測試 **94／94** 通過，0 失敗、0 略過；其中包含 15 個市場／快取及 5 個上櫃 UI 案例。三語 UI 驗證明確選市場後才下載、來源與庫存市場、最新本益比、原價掃描及事件標籤、離線重開與失敗保留前次資料。隔離預覽已更新，見 [啟動檔](../artifacts/histolens-similarity-preview/Start-HistoLens.cmd)。

## 使用範圍

這次個人版已依使用者明確授權採用網站來源，沒有把確認商用權利設為本輪實作的前置門檻。[TPEx 使用條款](https://www.tpex.org.tw/zh-tw/gtsm_disclaimer.html?l=zh-tw)及[政府開放資料 11370](https://data.gov.tw/dataset/11370)仍是不同精確資源的權利依據；本實作不代表已確認付費產品或對外轉供的授權。未購買資料、未申請帳號／方案、未建立對外行情服務。

仍未逐檔驗證五年／十年完整性；完整停止／恢復交易、分割／反分割、減資、事件修訂／取消目錄亦未完成。這些狀態保持可見，不藉由還原、補 0 或偽造停牌資料讓掃描通過。
