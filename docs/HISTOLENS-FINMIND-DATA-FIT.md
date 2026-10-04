# HistoLens：FinMind 資料適用性驗證

> 歷史查證／舊版紀錄：0.3.16 已依使用者決策只保留 FinMind。本文的官方連線及操作不再是現行功能，舊官方下載器與線上驗證腳本已移除；保存本文作證據追溯。目前流程與限制見 [FinMind 接入紀錄](HISTOLENS-FINMIND-IMPLEMENTATION.md)。

驗證日：2026-10-04。本文記錄接入前的資料與核心驗證；當時未接入下載器、CSV 匯入或 UI。後續個人版下載、保存、事件圖表及兩市場獨立日曆核對已另行完成，最新狀態見 [FinMind 接入紀錄](HISTOLENS-FINMIND-IMPLEMENTATION.md)，本文保留各階段實測證據。

**結論：FinMind 原價 OHLCV 足以計算現行十項相似度指標，兩檔五年／十年行情取得成功。但日線檔本身不是完整可匯入快照，仍需來源轉換、身分、獨立日曆及事件資料。** 事件覆蓋未知只警示，已知事件按日期註記，不做還原或填造缺價。

## 1. 下載與品質

前輪已核對[官網歷史資料頁](https://finmindtrade.com/analysis/#/data/document)使用 JSON 再於瀏覽器產生 CSV。本輪驗證同一公開資料端點，未帶 Token、Cookie 或 `device=web`；**沒有宣稱已驗收瀏覽器保存的 CSV 檔、中文表頭或檔案編碼**。

`GET https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockPrice&data_id=2330&start_date=2016-01-01&end_date=2025-12-31`；6488 僅替換 `data_id`。每股一次十年請求，五年結果從中切出。回應 JSON／UTF-8，HTTP/API 均 200。

| 股票／市場 | 五年 2021-01-01..2025-12-31 | 十年 2016-01-01..2025-12-31 |
| --- | --- | --- |
| 2330 台積電／TWSE | 1,214 列；2021-01-04..2025-12-31 | 2,438 列；2016-01-04..2025-12-31 |
| 6488 環球晶／TPEx | 1,214 列；2021-01-04..2025-12-31 | 2,438 列；2016-01-04..2025-12-31 |

沿用之前的官方身分證據：2330 ISIN `TW0002330008`、上市日 1994-09-05；6488 ISIN `TW0006488000`、上櫃日 2015-09-25，均早於十年起日。見 [TWSE 身分證據](HISTOLENS-TWSE-SOURCES.md)、[TPEx 身分證據](HISTOLENS-TPEX-IMPLEMENTATION.md)。不是由 FinMind 代碼推定身分。

兩股均日期遞增、无重複／錯股／越界日期、無負量或非整數股數；與另外取得的 FinMind 交易日表比對沒有缺列或日曆外日期。每年列數依序：2016 **244**、2017 **246**、2018 **247**、2019 **242**、2020 **245**、2021 **244**、2022 **246**、2023 **239**、2024 **242**、2025 **243**。來源內一致不等於全部官方日曆已獨立核實。

兩股五年 OHLC 均有效。6488 十年中的 **2016-08-18** OHLC 全 0、量 0，故有 2,438 列、**2,437 列有效價格**。原因待確認，不稱停牌、不當作下載漏日。

共 **14 次序列請求**、間隔 1 秒、無並行或重試：13 成功，最後停牌公告 HTTP 400 免費權限不足，隨即停止。節流是探針設定，不是官方限額。回應、實際 URL、UTC 下載／最後成功時間、HTTP/API 狀態、SHA-256 存在 Git 忽略目錄 `artifacts/histolens-source-verification/finmind-fit-20261004/`；未提交原始行情。

## 2. 欄位、單位與官方比對

| FinMind | HistoLens | 必要處理 |
| --- | --- | --- |
| `date` | `DailyBar.Date` | 公曆交易日，與下載時間分開 |
| `stock_id` | `Instrument.Code` | 字串；另核對市場、種類、ISIN／存續期間 |
| `open/max/min/close` | `Open/High/Low/Close` | 樣本為 TWD 元、原價；零價轉缺值 |
| `Trading_Volume` | `Volume` | 成交股數，**不再乘 1,000** |
| `Trading_money` | `Turnover` | 成交金額、TWD 元 |
| `Trading_turnover` | 原始成交筆數留存 | **不能**填入 HistoLens `Turnover` 金額 |
| `spread` | 原始價差留存 | 掃描不依賴，不用來猜公司行動 |

- 2330 五年 **1,214 列**與先前保存、經雜湊再核對的官方 `STOCK_DAY` 比對：OHLC、股數、金額、成交筆數全部 **0 差異**。沒有重新抓官方月報。
- 6488 2025-07 官方月報 **23 列**的 OHLC、成交筆數全部一致。月報整數張／仟元乘千後，與 FinMind 整數股數／元最大差 **498 股／490 元**，樣本符合月報取整精度，不能稱精確相等。例如 2025-07-01 FinMind **1,984,549 股**、月報 **1,985 張**。尚未以未取整官方逐股檔驗證上櫃所有年份及交易型態口徑。
- 邊界小樣本實測：2317 2025-07-30 零量零價；9929 2025-07-31 **有 451 股但 OHLC 全 0**。不能只靠量大於零篩有效行情。原始資料保留，標準化價格為 `null`，不補前收、不刪市場日、不自行標停牌。[FinMind 技術資料說明](https://finmind.github.io/tutor/TaiwanMarket/Technical/)

## 3. 輔助資料

均使用相同 `/api/v4/data` 端點，`dataset` 指定資料表；完整參數保存在探針及 `requests.json`。

| 資料表 | 實測結果 | 限制 |
| --- | --- | --- |
| `TaiwanStockInfo` | 4,329 列，有名稱／市場；2330 兩列、6488 一列 | 2330 產業分類不同，不能對代碼直接取唯一列；沒有 ISIN／歷史身分區間，日期還包含 `None` |
| `TaiwanStockTradingDate` | 6,936 日期，1999-01-05..2026-12-31 | 含未來預定日期，不能當資料截至日。五年 1,214 日期與先前官方 TWSE `FMTQIK` 60 月完全相同；TPEx 分市場歷史及 2016..2020 待獨立全面核實 |
| `TaiwanStockDividendResult` | 五年 2330 **20 筆**、6488 **11 筆**，全部事件日期有行情 | 可做除權息日期註記；不代表全部行動、取消／修訂完整 |
| `TaiwanStockPER` | 兩股查 2026-09-28..10-02 各 4 筆，最新 10-02 | 可顯示分析當下最新公布 PER，須標資料日；不參與歷史相似度 |
| `TaiwanStockCapitalReductionReferencePrice` | 2327、2017 年得一筆，2017-08-18 | 證明單股減資表可取得，尚未驗證兩市場全期間與修訂完整性 |
| `TaiwanStockParValueChange` | 2021..2025 得 14 筆 | 額外面額變更事件候選，不等於全部分割／反分割 |
| `TaiwanStockSplitPrice` | 35 筆，2019-09-09..2026-09-07 | 有分割／反分割；不能據最早日期推論此前無事件；ETF 案例不因此納入普通股支援 |
| `TaiwanStockSuspended` | 查 2025-07-30，HTTP/API 400，回覆免費等級不足 | 本匿名途徑無法取得；沒有繞過限制，不能用缺價替代停复牌證據 |

**事件對齊實例：6488 2025-07-16 除息**，與已保存的官方 TPEx `exDailyQ` 比對，類型、現金股利 **6 元**、前收 **310 元**、參考價 **304 元**皆一致，且有當日行情。FinMind 此事件的 `after_price=304` 是參考價，**不是當日實際收盤 322.5**；`open_price` 也不能直接當成交開盤。這些欄位不能覆寫 OHLC。

入口以[官網資料頁](https://finmindtrade.com/analysis/#/data/document)為主；欄位／候選另參考[官方技術文件](https://finmind.github.io/tutor/TaiwanMarket/Technical/)與[基本資料文件](https://finmind.github.io/tutor/TaiwanMarket/Fundamental/)，能力以實測回應為準。未以 SDK 文件推定商業授權。

## 4. 現有核心實跑

驗證器直接連結現有 `SimilarityEngine`、模型、驗證器及指紋程式，沒有重寫算法。原價、20 個交易日、全部十項指標、門檻 60%、截止 2025-12-31；`SourceId=FinMind`，事件覆蓋標未知。

| 樣本 | 證據層級 | 候選／可計算窗口 | 去重後結果 |
| --- | --- | --- | --- |
| 2330 五年 | 獨立官方日曆核對，掃描完成 | 1,175／1,175 | 40 |
| 2330 十年 | 明示日曆假設的計算相容性測試 | 2,399／2,399 | 83 |
| 6488 五年 | 同上，完整 TPEx 日曆待核實 | 1,175／1,175 | 32 |
| 6488 十年 | 同上，零價轉缺值 | 2,399／2,379 | 69 |

後三組在嚴格模式保留 `Calendar.IsVerified=false`，如實回報 `CalendarUnverified`。另做明確標名的暫時記憶體條件測試，**不是來源驗收，不保存假定已確認的日曆**。不能為了跑完正式把日曆填成已驗證。

已驗證：十項指標可計算；2330 五年有 11 個保留窗口跨已知除息，近期窗口含 2025-12-11 事件，仍完成；6488 條件測試近期窗口含 2025-12-31 事件亦可完成。6488 十年零價只排除跨該日的 **20 個歷史窗口**。所有完成案例通過計數恆等式、分數範圍、事件／未知覆蓋警示與輸入雜湊不變檢查。沒有還原、補價或修改行情。

**未驗收正式 FinMind UI、CSV 匯入及圖表畫面。** 現有圖表讀取快照事件的機制可供接入，核心出現警示不等於圖表已接好。

## 5. 最小接入缺口與分類

1. 正式來源／欄位轉換：`SupportedResearchData` 現只認 TWSE／TPEx 來源，開檔只認自有封裝 JSON。FinMind 需有自己的來源標示及入口，不能冒標官方來源，也不能混入原官方快取。
2. 補足上櫃及十年日曆核實、歷史證券身分；主檔不是完整歷史名冊。
3. 行情、事件與日曆分開保存；已知事件按實際日期供圖表註記。事件未知只警示；缺價原因未知不冒充停牌。
4. 本次無 ETag、Last-Modified 或逐筆修訂版本；保存本機成功時間、查詢及雜湊。之後應重查中間缺口／重疊期間，不只看 `MAX(Date)`；修訂、取消及刪除事件仍未驗證。

分類：**已驗證可用**＝樣本日線及個別輔助表；**部分符合**＝身分、日曆及事件完整性；**匿名途徑無法使用**＝停牌公告；**尚未確認**＝全證券歷史身分／全部事件修訂、正式檔案匯入與 UI。

本輪資料適用性不代替授權判斷。先前將 Free 條款直接延伸至所有付費 BYOK 桌面整合的文字不作最終結論；使用者自行註冊及本機下載模式，仍依[官網條款](https://finmindtrade.com/analysis/#/Sponsor/terms_of_use)及[方案](https://finmindtrade.com/analysis/#/Sponsor/sponsor)釐清。

## 6. 重現方式

需要 PowerShell 7、.NET SDK 10；於 repository 根目錄執行。固定少量樣本，最多 14 次序列請求，首次失敗即停、不重試。原始資料只存 Git 忽略目錄，請用新目錄以保留既有證據。

```powershell
pwsh -NoProfile -File scripts/histolens-source-verification/Probe-FinMind.ps1 `
  -OutputDirectory artifacts/histolens-source-verification/finmind-rerun

dotnet run --project scripts/histolens-source-verification/FinMindFit/FinMindFit.csproj `
  -p:NuGetAudit=false -- artifacts/histolens-source-verification/finmind-rerun `
  artifacts/histolens-source-verification
```

第一步保存 `requests.json` 及原始回應；第二步離線計算 `fit-summary.json`。第二參數下官方快取不存在時跳過外部核對，不宣稱日曆已驗證；存在但雜湊不符則失敗。新 checkout 沒有原始資料，不能直接重現官方逐筆比對，須合法另備證據。本輪驗證器建置及實跑成功；未修改正式產品，因此未重跑全產品測試。
