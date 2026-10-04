# 006 HistoLens：TWSE 真實資料契約與可比性證據

> 歷史查證／舊版紀錄：0.3.16 已依使用者決策只保留 FinMind。本文的官方連線及操作不再是現行功能，舊官方下載器與線上驗證腳本已移除；保存本文作證據追溯。目前流程與限制見 [FinMind 接入紀錄](HISTOLENS-FINMIND-IMPLEMENTATION.md)。

> 後續政策註記（2026-10-04）：本文件保留2026-10-03契約查證與舊保守研究背景。主流程相似度掃描現已改為原價掃描加事件提醒，不再因跨事件全域中止；本轮五年實測、授權与缺口以[官方歷史資料驗證](HISTOLENS-DATA-VERIFICATION.md)為準。

查證日期：2026-10-03（Asia/Taipei）。範圍是單一現行上市普通股的原始日線研究。本文件記錄來源契約與技術判斷；實作、建置及整體驗收由主任務另行記錄。

**三張公司行動報表目前不足以宣告「所有公司行動完整覆蓋」。可用的保守方案是另外驗證官方日線的原價可比性：排除不比價、面額變更、未知欄位、價格差不一致與缺交易日的窗口。** `ActionCoverage` 與 `PriceComparisonCoverage` 必須分別表達，不得把後者冒充完整公司事件目錄。

## 官方來源與回應契約

日期參數採西元 `yyyyMMdd`。下列歷史查詢是官網報表使用的端點，並非所有路徑都列於 OpenAPI；不能把 OpenAPI 的每日資料或某資料集授權一概套用到相鄰歷史端點。

| 用途 | 官方端點 | 已確認契約與限制 |
| --- | --- | --- |
| 個股日線 | `https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date=20250301&stockNo=2330&response=json` | 一次一個月份。`stat`、`date`、`title`、`fields`、`data`、`notes`、`total`。官網[個股日成交資訊](https://www.twse.com.tw/zh/trading/historical/stock-day.html)提供歷史查詢；欄位詳見下節。 |
| 市場比價定義 | `https://www.twse.com.tw/rwd/zh/afterTrading/MI_INDEX?date=20250318&type=MS&response=json` | 多表結構 `tables`；本次第 8 張表的 `notes` 說明漲跌與無比價。此端點只用於查證定義，不要求每下載一檔日線便重抓市場全表。 |
| 除權息結果 | `https://www.twse.com.tw/rwd/zh/exRight/TWT49U?startDate=20250101&endDate=20251231&response=json` | [官方頁](https://www.twse.com.tw/zh/announcement/ex-right/twt49u.html)明示自 2003-05-05 提供。非空成功回應有 `strDate`、`endDate` 精確回聲；結果混合股票、ETF 等，須依證券代號篩選。 |
| 減資恢復價 | `https://www.twse.com.tw/rwd/zh/reducation/TWTAUU?startDate=20250101&endDate=20251231&response=json` | 官方路徑拼字是 `reducation`。[官方頁](https://www.twse.com.tw/zh/announcement/reduction/twtauu.html)明示自 2011-01-01 提供。非空成功回應有 `strDate`、`endDate`。 |
| 面額變更恢復價 | `https://www.twse.com.tw/rwd/zh/change/TWTB8U?startDate=20250101&endDate=20251231&response=json` | [官方頁](https://www.twse.com.tw/zh/announcement/change/twtb8u.html)表單年份下限為 2019。回聲在 `params.startDate`、`params.endDate`，不在頂層。頁面沒有可見的完整歷史起日承諾；不能引用 HTML 註解中的舊說明當作承諾。 |
| 上市證券身分 | `https://isin.twse.com.tw/isin/e_C_public.jsp?strMode=2` | [官方 ISIN 名錄](https://isin.twse.com.tw/isin/e_C_public.jsp?strMode=2)提供種類區段、ISIN、上市日、市場、CFI。HTML 仍宣告 `charset=big5`；英文頁也不能默認 UTF-8。這是現行名錄，不是歷史身分時間線。 |
| 中文名／上市日交叉核對 | `https://openapi.twse.com.tw/v1/opendata/t187ap03_L` | [官方 OpenAPI](https://openapi.twse.com.tw/)列上市公司基本資料。[政府資料集 18419](https://data.gov.tw/dataset/18419)列免費、政府資料開放授權第 1 版。含 TDR 公司，不能單獨判定普通股。 |

三張公司行動頁面使用前端分頁，沒有 `data-server-side`；本次 JSON 回應直接包含結果列，沒有總筆數或下一頁游標。應檢查固定欄位、回聲範圍及全部列，不能把 HTML 畫面顯示的前 10 列當成完整結果。未驗證任意大區間均無上限，整合宜採有界日期範圍並快取原始回應。

## 原始價格可比性的官方依據

[STOCK_DAY 報表](https://www.twse.com.tw/exchangeReport/STOCK_DAY?date=20250301&response=html&stockNo=2330)明示 `X` 代表不比價，`**` 註記代表面額變更或 ETF 分割／反分割後存在價格比例轉換。[MI_INDEX 報表](https://www.twse.com.tw/exchangeReport/MI_INDEX?response=html)則說明數字價差比較當日與前一日收盤；沒有可比前收、除權息、新上市、恢復交易等情況屬無比價。

本次也取得 `MI_INDEX` 2025-03-18 的原始 JSON，相關定義位於 `tables[7].notes`；索引只描述本次證據，正式 parser 不應無條件假定每種查詢都有相同表序。

`STOCK_DAY.fields` 實測順序：

```text
日期, 成交股數, 成交金額, 開盤價, 最高價, 最低價,
收盤價, 漲跌價差, 成交筆數, 註記
```

| 真實正例 | 日線回應 | 其他官方表交叉核對 |
| --- | --- | --- |
| 2330 台積電，2025-03-18 除息 | 收盤 971.00，漲跌價差 `X0.00`；前一市場日收盤 970.00。3/19 收盤 952.00、價差 -19.00。 | `TWT49U` 有當日除息列。 |
| 2025 千興，2025-02-12 減資後恢復 | 收盤 16.65，價差 `X0.00`，註記空白。 | `TWTAUU` 有當日彌補虧損減資列。 |
| 2327 國巨，2025-08-25 面額變更後恢復 | 收盤 143.00，價差 `X0.00`，註記 `**`。 | `TWTB8U` 有當日恢復列，前停牌收盤 546.00、恢復參考價 136.50。 |

**不得將 `X0.00` 去掉 `X` 後當作普通的零價差。** `**` 也不是可忽略的展示文字。數字差額核對應使用 decimal 與明確格式，不以浮點容忍誤差放過不一致。

主代理已採納的整合方向：

1. 官方月表的月份、代號、欄位、列數、日期與資料形狀驗證通過，並由獨立市場交易日資料界定應有交易日。
2. 每個交易日只與前一個**市場交易日**比較。缺日不可跳過並改找最近一筆有行情的日期；首日無可核對前收時保留缺口。
3. `X`、`**`、非空未知註記、未知價差、數字價差不等於相鄰收盤差、缺日或缺必要行情，皆形成不可跨越的日期或邊界。
4. 只有在已確認範圍內、沒有穿越任何缺口或已知公司行動的研究窗口，才能計算原價比較；原始 OHLC 不做猜測性還原。
5. 三張行動表補充已知事件；下載失敗、空回應缺少期間回聲、未知類型，仍須顯示診斷並保留 `ActionCoverage.IsVerified=false`。另以 `PriceComparisonCoverage` 表達原價可比性。

這是針對來源明示的價格比較契約所做的保守工程判斷，並非證明公司沒有其他行動，也不是股利再投資或總報酬研究。合成資料既有的公司行動覆蓋規則應維持相容；正式資料的新增可比性證據須進入保存及快照指紋。

## 公司行動目錄仍未證明的部分

- `TWT49U` 自身說明明確排除某些除息與退還股款／分割減資併案；只下載該表不夠。
- `TWTAUU` 官方說明涵蓋退還股款、彌補虧損，以及減資合併現增的計算。本次 2011-01-01 至 2025-12-31 得 389 列：退還股款 206、彌補虧損 183。**未找到分割減資正例，只能說未驗證，不能反推該表不包含或期間內沒有。**
- [證交所投資人問答](https://investoredu.twse.com.tw/pages/TWSE_InvestmentQA.aspx?ID=1)把分割減資列為不同參考價格計算情形；這證明類型存在，但不是報表完整性的保證。
- ETF 分割有獨立官方報表；本階段身分驗證限定普通股，不將 ETF 納入後再沿用普通股涵蓋判斷。
- `TWTB8U` 2019 年全年及 2025 年第 1 季都回 `stat=OK`、完整欄位、`data=[]` 與期間回聲。這只能驗證該報表的空結果契約，不能證明所有公司行動皆不存在。
- `TWTAUU` 2025-01-01 至 2025-01-03 只回「很抱歉，沒有符合條件的資料!」，沒有欄位或期間回聲。不可將這種回應轉為「已驗證無事件」。

## 普通股身分與上市期間

ISIN 英文名錄每列七欄為：證券代號及名稱、ISIN、上市日、市場、產業、CFICode、備註。必須同時確認種類區段及市場，不能只靠四碼字串。最小保守範圍可採 `Stocks` 區段、`TWSE LISTED` 市場、`ESVUFR` 分類，並保留 ISIN 作識別；遇到新分類應明示不支援再查證，而非猜測。

| 代號 | ISIN／CFI 實測 | 判斷 |
| --- | --- | --- |
| 2330 | `TW0002330008`／`ESVUFR`，上市日 1994-09-05 | 可與上市公司資料交叉核對。 |
| 2881A | `TW0002881A00`／`EPNRAR` | 特別股，排除。 |
| 0050 | `TW0000050004`／`CEOGEU` | ETF，排除。 |
| 9103 | `TW0009103002`／`EDSDDR` | TDR，雖為四碼且列於上市公司基本資料，仍須排除。 |

公司基本資料 2330 實測欄位：`公司代號=2330`、`公司簡稱=台積電`、`上市日期=19940905`、`出表日期=1151002`。注意出表日期為民國年月日，而上市日期是西元年月日；不能共用只靠字數猜測的轉換。

現行名錄對已下市證券、重新上市前歷史、代碼再使用及歷史名稱不提供完整時間線。本階段應把請求限制在可核對的現行上市身分與上市日之後；不應由今天的名錄替所有過去日期背書。官方也註明上市日以正式公告為準，兩份來源日期不一致時應保留衝突診斷。

## 實測證據與重跑

原始回應和探針均保存在忽略版控的 `artifacts/histolens-twse/evidence/`。儲存的是 HTTP 解碼後內容，以 UTF-8 寫檔，SHA-256 對應所保存檔案，不宣稱等同線上壓縮傳輸位元組。

| 證據 | 實際結果 |
| --- | --- |
| `probe-source-contracts.json`、`page-*.response.txt` | 三個官方頁、ISIN 名錄、Swagger 皆成功讀取；ISIN 回應 `charset=big5`。 |
| `probe-actions.json`、`actions-*-2025.response.txt` | 2025 全年除權息 1,358 列、減資 17 列、面額變更 4 列；均 HTTP 200、`stat=OK`。另保存公司基本資料及官方報表 JS。 |
| `probe-coverage.json` | 減資 2011–2025 共 389 列；面額變更 2019 全年及 2025 第 1 季的空結果皆有期間回聲。 |
| `probe-twtauu-empty.json` | 減資短期間空結果只有訊息，沒有可驗證期間。 |
| `probe-comparability.json`、`compare-*.response.txt` | 三種已知事件的日線正例全部 HTTP 200、`stat=OK`。 |
| `comparability-mi-index-contract.json` | 從保存的 MI_INDEX 多表 JSON 核對比價定義；首次探針摘要誤以為它有根層 `data` 而失敗，原始回應已保存，已修正探針並以本機解析確認。此錯誤未隱藏為網路成功紀錄。 |

```powershell
./artifacts/histolens-twse/evidence/Probe-TwseSourceContracts.ps1
./artifacts/histolens-twse/evidence/Probe-TwseActions.ps1
./artifacts/histolens-twse/evidence/Probe-TwseCoverage.ps1
./artifacts/histolens-twse/evidence/Probe-TwseComparability.ps1
```

探針使用正常官方網址、每次請求之間間隔 2 秒，沒有建立帳號、使用 API 金鑰、繞過阻擋、購買服務或上傳資料。個別失敗原因保留；網站未承諾的速率上限不能由本次少量成功推定。這個查證子任務未修改產品程式、未跑 Build/Test、未提交或推送；實際執行模型未經工具確認，保留未知。
