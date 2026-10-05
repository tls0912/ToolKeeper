# HistoLens 上櫃下載診斷

> 歷史查證／舊版紀錄：0.3.16 已依使用者決策只保留 FinMind。本文的官方連線及操作不再是現行功能，舊官方下載器與線上驗證腳本已移除；保存本文作證據追溯。目前流程與限制見 [FinMind 接入紀錄](HISTOLENS-FINMIND-IMPLEMENTATION.md)。

> 歷史診斷紀錄：本文描述加入 TPEx 提供者之前的狀態。使用者隨後確認目前個人版允許 TWSE／TPEx 網站來源；後續實作與驗證見 [個人版 TPEx 下載](HISTOLENS-TPEX-IMPLEMENTATION.md)，現行產品決策見 [規格 15.9](products/006_HistoLens.md#159-個人版-twsetpex-網站來源)。

查證日期：2026-10-04。這份紀錄區分產品尚未支援、歷史來源可連線、驗證程式解析問題與來源使用權；不把其中一項成功當成整套下載已完成。

## 結論

- **現行產品尚未接入上櫃歷史提供者。** 不是所有 TPEx 網路來源都不能使用，也不只是更換一個 URL 就能修好。
- 本次正常、序列 GET 已取得 6488 環球晶 2025 年 1 月歷史行情與獨立上櫃市場月報，均為 HTTP 200。前次 HTTP 520 是當時的存取結果，本次不再沿用為目前狀態。
- 既有驗證程式不認得現在的 TPEx 欄位及表格形狀；已修正並離線重播通過。**只代表本月解析及日期對齊通過，未代表精確成交股數、五年完整性、證券身分沿革或使用權已驗證。**
- TPEx 一般網站的歷史查詢資源尚未確認符合本產品自動下載及商業使用權。本輪沒有新增正式 TPEx 歷史提供者，也沒有把最新開放行情的授權延伸到網站歷史查詢。

## 現行程式根因

| 位置 | 已確認行為 | 影響 |
| --- | --- | --- |
| `src/HistoLens/Data/TwseHistoricalDataProvider.Evidence.cs` | 身分限定 `Stocks / TWSE LISTED / ESVUFR`，再核對上市公司主檔 | 上櫃代碼不會被當成支援的上市普通股 |
| `src/HistoLens/Data/IHistoricalDataProvider.cs` | `HistoricalDataRequest` 只有代碼及起迄日，沒有市場 | 目前不能以明確市場路由到另一提供者 |
| `src/HistoLens/SupportedResearchData.cs` | 真實資料限 TWSE 普通股 | 不接受冒標為 TWSE 的上櫃資料 |
| `src/HistoLens/MarketDataStore.cs` | 更新查找依 TWSE 市場及代碼 | 加入上櫃前要保持市場及穩定身分隔離，不能只改代碼 |
| `src/HistoLens/Data/CachedMarketDataDownloader.cs`、`MarketDataMerge.cs` | 缺月更新及部分來源標記限定 TWSE | 新提供者仍需最小範圍整合與回歸驗證 |

產品 UI 已標示上市普通股。若要改善失敗說明，可以在身分未通過時明確說明「目前下載器僅支援已核對的上市普通股，上櫃尚未接入」；不能僅憑上市名冊找不到，就自行宣稱輸入代碼一定是上櫃。

## 本次實際請求與結果

使用一般 `Invoke-WebRequest`，未登入、未解驗證碼、未更換代理、未模擬瀏覽器授權狀態。最初 web 讀取工具收到 TPEx 頁面 HTTP 403；正常原生 HTTP 客戶端成功取得公開頁面及 JSON。這是不同客戶端的實測結果，不是保證所有使用者端都會成功。

| 目的 | 實際資源 | 台灣時間 | 結果 |
| --- | --- | --- | --- |
| 6488 月行情 | `https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock?date=2025/01/01&response=json&code=6488` | 14:12:46 | HTTP 200；`application/json; charset=UTF-8`；15 筆；2025-01-02 至 2025-01-22 |
| 獨立市場交易日期 | `https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingIndex?date=2025/01/01&response=json` | 14:13:50 | HTTP 200；`application/json; charset=UTF-8`；15 筆，日期與行情逐筆對齊 |
| 官方個股表單契約 | [個股日成交資訊](https://www.tpex.org.tw/zh-tw/mainboard/trading/info/stock-pricing.html) | 14:14:29 | HTTP 200；表單 `code/date`、月份查詢；action 為 `afterTrading/tradingStock`；頁面宣告自民國 83 年 1 月提供 |
| 精確資源授權查證 | [TPEx 使用條款](https://www.tpex.org.tw/zh-tw/gtsm_disclaimer.html?l=zh-tw) | 14:13:47 | HTTP 200；一般網站自動下載及權利條款，見下一節 |

另外取得一份[上櫃股票行情頁面](https://www.tpex.org.tw/zh-tw/mainboard/trading/info/pricing.html)作為官方頁面連結及報表範圍核對。沒有再下載全市場逐日行情，也沒有重跑五年或十年。

行情 JSON 是 `tables[0]` 表格：

```text
root: stat="ok", date="20250101", code="6488", name="環球晶"
table: title="個股日成交資訊", subtitle="6488 環球晶 114年01月"
fields: 日 期、成交張數、成交仟元、開盤、最高、最低、收盤、漲跌、筆數
totalCount: 15
```

市場月報為「日成交量值指數」，欄位包括日期、成交張數、金額（仟元）、筆數、櫃買指數及漲跌。它是獨立市場交易日證據，不是用 6488 有行情的日期反推開市日；本月缺行情日期 0、行情落在市場日曆外日期 0。

**成交量限制：** 本次個股月報的來源單位是「張」，並以整數呈現。固定測試標的 6488 為普通股，驗證器以一張一千股換算，但保留 `sourceVolumeText`、`originalVolumeUnit`，且 `exactShareVolumeVerified=false`。換算不會補回來源沒有揭示的零股或更細精度，不得標成已核對的精確每日成交股數。官方報表亦註明不含上櫃股票鉅額交易；不能與不同口徑的行情偷偷混接。此換算不適用所有 ETF。

## 使用權與未完成項目

[資料集 11370](https://data.gov.tw/dataset/11370) 明確登錄「上櫃股票行情」、每日更新、免費及政府資料開放授權第 1 版，其實際資源是：

```text
https://www.tpex.org.tw/web/stock/aftertrading/DAILY_CLOSE_quotes/stk_quote_result.php?l=zh-tw&o=data
```

該頁另連結 [TPEx OpenAPI](https://www.tpex.org.tw/openapi/) 與 [Swagger](https://www.tpex.org.tw/openapi/swagger.json)。這不足以證明前述 `afterTrading/tradingStock` 與 `tradingIndex` 的歷史查詢也在相同授權範圍。

本次實讀 [TPEx 一般網站使用條款](https://www.tpex.org.tw/zh-tw/gtsm_disclaimer.html?l=zh-tw)：第 5 條限制未經其同意方式的自動化下載；第 7 條保留網站內容權利，並另列已授權政府資料開放平臺供公眾使用的資料例外。因此目前無法僅靠網頁可查、JSON 可取或同一機構有開放資料，就確認可整合進付費桌面產品。

**這是公開來源條款與精確資源授權證據不足，不是工具的自動核准審核拒絕。** 本輪沒有申請、購買或宣稱取得新授權，也沒有把這些原始行情提交到 repository。

仍未完成：完整五年／十年日線、精確股數口徑、歷史證券身分、全部停復牌類型、完整公司行動、歷史修訂、五年市場日曆，以及歷史資源的產品使用權。官方頁面寫有歷史起年，不代表每一證券自該年存續或每月完整。

## 實際修改與重現

僅修改驗證工具：

- `scripts/histolens-source-verification/Verify-History.ps1`：對 TPEx 正規化日期及短 OHLC 欄名；核對唯一日期表、代碼／字幕、主表與子表月份、`totalCount`；保存來源量單位及精度限制。
- `scripts/histolens-source-verification/Test-Verification.ps1`：保留原有 7 個離線案例，新增 TPEx 7 個合成案例，包含張數、缺價、錯代碼、錯月份、錯筆數、未知單位與重複正規化欄位。

離線測試命令及本次實際結果：

```powershell
./scripts/histolens-source-verification/Test-Verification.ps1
# 14 個合成案例全部通過；沒有網路請求。

./scripts/histolens-source-verification/Verify-History.ps1 `
  -Market TPEx -Offline -Start '2025-01-01' -End '2025-01-31' `
  -OutputDirectory './artifacts/histolens-source-verification/tpex-replay-20261004-1420'
# 已保存的實際回應重播：15 筆行情、15 個市場日、缺日 0、區間外 0。
```

原始證據與重播輸出僅留在被忽略的 `artifacts/histolens-source-verification/`。重播目錄的市場日報下載時間及 HTTP 狀態由當次探針輸出重建，已標示 `metadataReconstructedFromProbeOutput=true`，不冒充另一筆網路下載。

本次只執行上述 PowerShell 驗證，未執行產品 Build／.NET 測試；沒有修改行情提供者、研究引擎或快取。**完成狀態：診斷與驗證程式修正完成；產品上櫃歷史下載仍未完成。**
