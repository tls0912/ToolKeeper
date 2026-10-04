# HistoLens：免費歷史資料替代來源查證

查閱日期：2026-10-04。範圍：台灣上市／上櫃個股五年（2021-01-01 至 2025-12-31）與十年（2016-01-01 至 2025-12-31）原始每日 OHLCV，以及付費桌面軟體的使用權。排除 TWSE 網站歷史查詢下載系統；正式開放 API 的既有驗證見 [官方來源紀錄](HISTOLENS-DATA-VERIFICATION.md#9-官方-openapi-是否提供個股歷史回補2026-10-04-續查)。

**結論：有免費取得多年台股日線的個人研究方案；目前未找到同時明確允許免費整合付費 HistoLens、長期離線保存及所需匯出的完整來源。** Fugle 的行情範圍值得優先續查；FinMind 的 Free 方案則已有明確非商用限制。這不是宣稱所有其他來源都不存在，亦不是判定使用者既有下載違法。

本輪只查第一方方案、API 文件、條款與公開資料庫說明，沒有下載這些供應商的行情、註冊帳號、取得金鑰或購買服務。以下期間均為文件宣告，**不是上市／上櫃兩檔五年下載驗證成功**；事件、停牌、日曆及修訂完整性也未通過驗證。沒有新增正式下載功能。

## 1. 候選對照

| 來源 | 歷史與免費能力 | 權利及本案判定 | 證據 |
| --- | --- | --- | --- |
| Fugle 富果行情 API | 上市櫃股票日線自 2010 年起；免費基本方案歷史行情 60 次／分鐘，須帳號及 API key；單次區間少於一年 | 五年／十年日線有文件支持；免費方案為個人方案，商業產品整合、永久本機保存、原始／統計匯出權利尚未確認。公司行動 API 不在免費方案 | [方案](https://developer.fugle.tw/docs/pricing/)、[歷史 K 線](https://developer.fugle.tw/docs/data/http-api/historical/candles/) |
| FinMind | 單一股票日線列 Free，文件稱上市／上櫃／興櫃自 1994-10-01 起；無 token 300 次／小時、註冊後 600 次／小時 | Free 明列非商業，方案表不允許整合商業產品販售；使用者自帶 token 未找到豁免。可作符合條款的個人研究候選，不能認定付費 HistoLens 免費可用 | [日線文件](https://finmind.github.io/tutor/TaiwanMarket/Technical/)、[方案](https://finmind.github.io/Pricing/)、[資料授權聲明](https://finmind.github.io/Disclaimer/) |
| 永豐 Shioaji | 券商客戶 API；股票歷史自 2020-03-02 起，歷史 K 線單次最多 30 日。零交易量帳戶也有每日 500 MB 額度 | 有條件的五年候選，不能涵蓋本案十年；須證券帳戶、簽署及金鑰。商用整合、長期保存／匯出尚未確認，不能從 SDK 開源推定 | [歷史行情](https://sinotrade.github.io/zh/tutor/market_data/historical/)、[流量規則](https://sinotrade.github.io/tutor/limit/)、[API 約定](https://www.sinotrade.com.tw/newweb/signCenter/S_openAPI/) |
| FinLab | 有台股資料，但免費歷史截止日的官方頁面有不同版本，未確認能免費涵蓋 2021..2025 | 使用條款限制未經書面同意的商業 API 應用；不列為免費正式來源 | [平台](https://studio.finlab.finance/)、[條款](https://studio.finlab.finance/terms) |
| Yahoo Finance／yfinance | 有上市及上櫃歷史頁；Yahoo 官方歷史 CSV 下載現需 Gold 訂閱；五年／十年完整性未驗證 | yfinance 是套件，不能取代 Yahoo 的資料授權；未確認免費商用。Yahoo 的 Close 含分割調整，不能直接當作未調整原價 | [下載說明](https://help.yahoo.com/kb/finance/certain-amounts-sln2311.html)、[台灣條款](https://legal.yahoo.com/tw/zh-hant/yahoo/terms/otos/index.html)、[yfinance](https://github.com/ranaroussi/yfinance) |
| Alpha Vantage | 免費日線 compact 僅最近 100 點，完整 full 需 premium；台股兩市場覆蓋未確認 | 免費不足五年／十年，且條款限個人非商業，除非另有書面約定 | [API 文件](https://www.alphavantage.co/documentation/)、[條款](https://www.alphavantage.co/terms_of_service/) |
| Twelve Data | 官方上市 XTAI、上櫃 ROCO 頁皆標 Pro+／Venture+，不是完整台股免費方案 | Free Tier 明文禁止商業使用；不能以泛用 time_series 文件推定台股免費 | [上市](https://twelvedata.com/exchanges/XTAI)、[上櫃](https://twelvedata.com/exchanges/ROCO)、[條款](https://twelvedata.com/terms) |
| TWMD | 免費方案歷史深度一個月；資料集總起點更早不等於免費帳戶可下載完整歷史 | 免費非商業，且歷史長度不足；不列為五年／十年免費候選 | [免費方案](https://twmarketdata.com/en/pricing/free)、[方案比較](https://twmarketdata.com/en/pricing) |
| Stooq | 本次官方歷史下載頁無可讀正文，台股覆蓋及原始／調整口徑未確認 | 商用、長期保存及匯出授權未確認；不因公開 CSV 網址就判定可用 | [歷史下載頁](https://stooq.com/db/h/) |

## 2. 最值得續查的兩條路徑

### Fugle：技術文件符合多年日線需求，產品權利待確認

正式介面為 `GET https://api.fugle.tw/marketdata/v1.0/stock/historical/candles/{symbol}`，使用 `X-API-KEY`，參數有 `from`、`to`、`timeframe=D`、`adjusted=false`。回應包含 OHLC、volume 與市場欄位；日／週／月線量的單位為股，分鐘線單位不同，不可混用。文件宣告每日 16:30 更新。

可按完整曆年拆成五次或十次單股查詢，是依文件提出的最小驗證方案，尚未實際執行。先驗證 2330／6488、原價口徑、成交量、獨立交易日及修訂策略；HTTP 404 也可能表示期間無資料，不能直接判為停牌。[歷史 K 線契約](https://developer.fugle.tw/docs/data/http-api/historical/candles/)

公司行動屬開發者／進階方案，先前免費體驗已於 2026-02-12 結束；不能把有日線等同事件也免費齊全。[公司行動文件](https://developer.fugle.tw/docs/data/http-api/corporate-actions/dividends/)

待確認的具體問題：**免費帳戶持有人是否可透過付費第三方桌面軟體，使用自己的 key 直接取得行情，永久本機快取、離線顯示原價圖表、計算統計，並匯出個人使用的統計或原始 CSV？** 尚未找到涵蓋這一整組情境的公開授權；沒有向供應商發送詢問，也沒有自行推定禁止或允許。

### FinMind：免費個人研究可評估，免費商用已有明確限制

正式日線介面為 `GET https://api.finmindtrade.com/api/v4/data`，參數 `dataset=TaiwanStockPrice`、`data_id`、`start_date`、`end_date`。條款與方案直接限制 Free 非商業，不因程式庫 Apache-2.0 或上游部分資料採政府開放授權就自動解除。[方案](https://finmind.github.io/Pricing/)、[授權聲明](https://finmind.github.io/Disclaimer/)、[程式庫授權](https://github.com/FinMind/FinMind/blob/master/LICENSE)

免費事件候選包括除權息結果（文件起於 2003-05-01）、減資參考價（2011-01-01）、分割／反分割、變更面額（2020-01-01）與交易日清單；暫停交易公告不在 Free。這些項目仍不足以證明歷史事件及修訂完整。[資料集文件](https://finmind.github.io/tutor/TaiwanMarket/Fundamental/)

日線文件明示來源 `--` 可能轉成 OHLC=0，即使有成交量亦可能如此。匯入時必須識別為價格無效／未知，不能照當日有效價格使用，也不能以前值填補偽造行情。[日線欄位及例外](https://finmind.github.io/tutor/TaiwanMarket/Technical/)

## 3. 公開 CSV 與沙盒線索

- [voidful/tw_stocker](https://github.com/voidful/tw_stocker) 提供逐股 CSV 及日期範圍 manifest，但聲明要求使用者自行核對資料授權。本次未取得足以支持付費產品、永久保存及匯出的明確資料權利，不下載行情或當作已驗證來源。
- [tw-stock-data-release](https://github.com/yukishirotsubasa/tw-stock-data-release) 提供上市櫃 CSV 年包／週包，README 指來源為 TWSE MI_INDEX 與 TPEX OTC。所讀頁面未給出足夠的歷史原始資料再授權依據；改從 GitHub 下載不會自動解決上游權利，也不能繞過本案對網站查詢來源的限制。
- [FinTechSpace 數位沙盒](https://www.fintechspace.com.tw/zh-hant/digital-sandbox-distance-zone/) 列有時報資訊股票歷史 API，但未確認現行可免費普遍使用、五／十年覆蓋及正式商用保存權。舊活動文件或第三方 Swagger 不是現在的服務授權，保留為未確認線索。

## 4. 對 HistoLens 的最小方案

1. **若目標為付費 HistoLens：** 優先釐清 Fugle 免費帳戶的第三方桌面整合權利。確認前維持候選，不實作正式來源；FinMind Free 不列為免費商業方案。不購買或申請付費方案。
2. **若為使用者自己的非商業研究：** FinMind 與 Fugle 免費方案具備多年行情的文件基礎，但仍須分別遵守帳戶／方案條款並實測；不能把個人使用權直接延伸給產品銷售。
3. **可保留合法 CSV 匯入提案：** 使用者從明確允許該使用情境的來源取得檔案，附市場、證券身分、原始／調整口徑、量單位、期間及來源權利。匯入不是規避授權的方法。
4. **研究流程維持既有決定：** 原價掃描完成、事件依日期標示、覆蓋未知如實呈現；不做還原，不把事件未知當成沒有事件，也不以缺價填零。行情 API 可用仍不代表停牌、日曆及公司行動已齊全。

本輪交付為可核對的來源篩選及規格同步，不是歷史行情實測成功或正式供應商採用決定。僅文件變更，未執行 Build／Test。
