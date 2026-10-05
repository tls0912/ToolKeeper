# 006 HistoLens：官方歷史資料取得驗證

> 歷史查證／舊版紀錄：0.3.16 已依使用者決策只保留 FinMind。本文的官方連線及操作不再是現行功能，舊官方下載器與線上驗證腳本已移除；保存本文作證據追溯。目前流程與限制見 [FinMind 接入紀錄](HISTOLENS-FINMIND-IMPLEMENTATION.md)。

驗證日期：2026-10-04（Asia/Taipei）。範圍：股票識別、OHLCV、市場交易日、停復牌、價格不連續事件、逐資源授權與最小重跑工具。本輪不新增分析指標、不接入正式上櫃下載、不購買資料、不架設轉供服務。

**同日後續限制：使用者排除 TWSE 網站歷史查詢下載系統，改查官方 OpenAPI。第 9 節是此限制下的新結果；前文網站月報的成功紀錄僅保留為既有測試事實，不再作為採用方案或後續下載指示。正式開放 API 不因同屬 TWSE 網域而一併排除，須依明確資源及授權判斷。**

**結論：尚不能確認只使用這些政府開放資料，就已能完整支援首次指定五年或十年下載並進行完整覆蓋研究。** 開放行情主要是最新單日快照；TWSE 官網歷史月報在技術上完成本輪五年下載，但其權利不能由快照資料集自動繼承。TPEx 歷史月報本輪失敗；歷史停復牌、完整公司行動與修訂仍有缺口。這些限制不要求把已取得且有效的原價相似度掃描全部停用。

相關交付：

- [股票識別與逐資源授權](HISTOLENS-IDENTITY-LICENSE.md)：三份主檔、原始資源、OAS 對應、六種使用情境與顯名。
- [日曆、交易狀態與公司行動實測](HISTOLENS-EVENTS-VERIFICATION.md)：其餘六個指定資料集及歷史補充來源。
- [產品規格第 15 節](products/006_HistoLens.md#15-資料来源授權與發布門檻)：已確認、待確認與方案提案。
- 驗證腳本（舊工具已移除）、資源探針（舊工具已移除）、資源清單（舊工具已移除）、離線解析測試（舊工具已移除）。

## 1. 測試標的、期間及結果

五年固定為 **2021-01-01 至 2025-12-31（含首末日）**；十年範圍為 **2016-01-01 至 2025-12-31**。2330 上市日 1994-09-05；6488 上櫃日 2015-09-25，兩股均有足夠存續期間。掛牌日期由公司 OAS 與證券市場識別交叉核對；仍不等於完整身分沿革或行情覆蓋。

| 驗證項目 | TWSE 2330 台積電 | TPEx 6488 環球晶 |
| --- | --- | --- |
| 五年行情實際下載 | 60/60 月成功，**1,214 筆** | 首月正常請求及間隔複查均 HTTP 520；**0 個成功月份、0 筆五年行情**，隨即停止後續月份 |
| 實際行情首末日 | 2021-01-04..2025-12-31 | 無，不能以當期快照代替五年 |
| 日曆交叉核對 | 獨立 FMTQIK 60/60 月、1,214 個市場成交日；与個股日期完全一致 | 歷史市場日未完成驗證，不以 TWSE 日曆冒充 TPEx |
| 行情品質 | 重複日期、缺行情、市場外日期、空 OHLC、零成交量皆 0 | 未驗證；空集合不是零缺口 |
| 交易狀態 | 五年 TWTAWU 全市场表有回聲、3,110 列；2330 無匹配。它只證明該表沒有匹配，不證明所有類型停牌皆不存在 | 48665 OAS 只有當年度記錄；11736 當期狀態。完整五年停復牌未取得 |
| 除權息對齊 | 分年 TWT49U 得 20 筆2330除息；20/20 有行情且與日線不比價日期一致 | 發行公司官方頁可確認 2025-07-16 除息，但 TPEx 歷史結果與當月行情未完成對齊，不能算政府資料路徑成功 |
| 其他公司行動 | 2021..2025 減資表116列、面額變更表7列；2330沒有匹配。仍不宣稱所有行動完整 | 分割／減資歷史覆蓋未確認 |
| 五年總結 | 行情逐月下載與市場成交日對齊通過；逐資源商業權利、完整事件／停牌及修訂仍未通過 | 本輪歷史回補未通過，不是「已證明沒有歷史資料」 |

TWSE 每年行情／市場成交日均為：2021 **244**、2022 **246**、2023 **239**、2024 **242**、2025 **243**。合計1,214，來源含月份與股票回聲、欄位、資料形狀、官方 total（有提供時）、日期與 OHLC 邊界檢查。120 次月請求序列執行、每次完成後間隔3秒，保存內容合計306,631 bytes；台灣時間11:20:40開始、11:27:19最後下載完成。這是探針自訂保守節流，不是官方公布限額。

**十年沒有做全量下載驗收。** 額外抽查2330的2016年1月：21筆、2016-01-04..2016-01-30，市場月報同為21日；2020年12月：23筆、2020-12-01..2020-12-31，也與市場月報一致。2016-01-30是週六且有實際交易，不能把所有週末直接判為休市。TWSE [個股日成交資訊頁](https://www.twse.com.tw/zh/trading/historical/stock-day.html)宣告自2010-01-04提供；TPEx [官方月查詢頁](https://www.tpex.org.tw/zh-tw/mainboard/trading/info/stock-pricing.html)宣告自1994年1月提供。頁面宣告與兩個邊界月份成功，都不能替代120個月完整驗證；6488十年歷史能力本輪仍未確認。

## 2. 行情資源與實際契約

資源頁面：[11549](https://data.gov.tw/dataset/11549)、[11370](https://data.gov.tw/dataset/11370)。來源機構分別為臺灣證券交易所、證券櫃檯買賣中心；平臺提供機關列金管會證期局。OAS 路徑是沿資料集頁面備註所連官方 schema 查得，未把任意相鄰路徑當作相同授權。

平臺詮釋資料更新時間分別是11549的 **2025-05-01 11:23**、11370的 **2024-12-05 09:28**；本輪行情資料日是2026-10-02、本機下載日是2026-10-04，三者不能混用。

| 資源、端點 | 格式／欄位／單位 | 查詢與期間 | 更新、授權、實測 |
| --- | --- | --- | --- |
| 11549 原始 CSV：`https://www.twse.com.tw/exchangeReport/STOCK_DAY_ALL?response=open_data` | UTF-8 CSV；日期、證券代號、證券名稱、成交股數、成交金額、開盤價、最高價、最低價、收盤價、漲跌價差、成交筆數；量為股、價格TWD元 | 單日全市場，沒有文件化個股或歷史參數 | 每日；精確資源 OGDL-1；HTTP200，1,380列，全部民國1151002＝2026-10-02 |
| 11549 連結 OAS：`https://openapi.twse.com.tw/v1/exchangeReport/STOCK_DAY_ALL` | UTF-8 JSON陣列；`Date, Code, Name, TradeVolume, TradeValue, OpeningPrice, HighestPrice, LowestPrice, ClosingPrice, Change, Transaction`；量為股 | OAS沒有參數；加 `date=20210104&stockNo=2330` 實測仍為同一完整快照 | HTTP200，1,380列。兩次保存內容 SHA256 相同，不能回補2021年；來源對應及條款見授權報告 |
| 11370 原始 CSV：`https://www.tpex.org.tw/web/stock/aftertrading/DAILY_CLOSE_quotes/stk_quote_result.php?l=zh-tw&o=data` | 平臺宣告CSV、日期／代號／名稱／OHLC／成交股數等；本輪未取得資料內容，不假定回應編碼 | `l, o` 是語言及輸出格式，沒有本輪證明可用的歷史參數 | 每日；精確資源OGDL-1；直連HTTP520，16 bytes錯誤本文已保存 |
| 11370 連結 OAS：`https://www.tpex.org.tw/openapi/v1/tpex_mainboard_daily_close_quotes` | UTF-8 JSON陣列18欄；`Date, SecuritiesCompanyCode, CompanyName, Open, High, Low, Close, TradingShares`，另均價、量值、筆數、末買賣價及次日參考／漲跌停等；`TradingShares`為股 | OAS沒有參數；加 `date=20210104&code=6488` 仍回同一完整快照 | HTTP200，11,928列，全部2026-10-02，涵蓋不只普通股。兩次內容雜湊相同；6488僅該日1列，不是五年 |
| 官方歷史月報：`https://www.twse.com.tw/rwd/zh/afterTrading/STOCK_DAY?date=20210101&stockNo=2330&response=json` | JSON UTF-8；`stat,date,title,fields,data,notes,total`；日期、成交股數、成交金額、OHLC、漲跌價差、成交筆數、註記；量股、價TWD元 | `date=yyyyMMdd`選月份，`stockNo`選股票；每請求一月，不是單次五年或十年 | 60月成功；一般官網歷史來源、**不能繼承11549授權**；無修訂串流契約、未提供ETag或Last-Modified |
| TWSE獨立市場成交日：`https://www.twse.com.tw/rwd/zh/afterTrading/FMTQIK?date=20210101&response=json` | JSON UTF-8；日期、成交股數、成交金額、成交筆數、加權指數、漲跌；`hints`明示元、股 | 按月的已發生市場成交日，非個股行情推導，也非休市原因公告 | 60月成功；官網歷史資源權利另確認；不能單凭不存在一列就判定休市原因 |
| TPEx歷史月報：`https://www.tpex.org.tw/www/zh-tw/afterTrading/tradingStock?date=2021/01/01&code=6488&response=json` | 官方頁 `API_PATTERN=/www/{LANG}/{ACTION}` 及 `afterTrading/tradingStock` 確認路由；實際回520，尚未驗證資料格式及歷史量單位 | 表單按股票及年月；頁面宣告歷史起點不等於成功回補 | 正常直連及複查失敗；未繞過驗證碼／封鎖。不能拿最新OAS的「股」去推定歷史月表的單位 |

初次TWSE月份原價 `X` 不比價與 `註記` 原文均保留，不把`X0.00`當一般零價差。原始回應與標準化JSON分開，原始OHLC沒有被調整價格覆寫。民國字串另留 `sourceDate`，公曆日期寫入 `date`。探針只對精確認識的「成交股數」或「成交仟股」做單位處理；後者乘1,000，未知單位拒絕解析，不猜「張」。TPEx月表 parser 是未經成功真實回應驗證的候選分支，不能宣稱已支援正式下載。

## 3. 其餘必要資料來源對照

各列完整欄位、授權頁、實測日期及失敗訊息，分別見識別、事件兩份報告；下表保留整體缺口，避免只列有行情的來源。

| 必要資料／候選資料集 | 真正開放資源 | 參數、更新、格式及結果 | 缺口 |
| --- | --- | --- | --- |
| 證券代號、名稱、市場：11425 | `https://opendata.tdcc.com.tw/getOD.ashx?id=1-1` | 每日UTF-8 BOM CSV；144,861列，更新20261004；含市場、證券狀態等；OGDL-1 | 含95,730終止上市櫃記錄，但無歷史有效起迄、ISIN或完整代碼沿革；不能單靠名稱／代碼串接 |
| 上市公司：18419 | `https://mopsfin.twse.com.tw/opendata/t187ap03_L.csv`；OAS `https://openapi.twse.com.tw/v1/opendata/t187ap03_L` | CSV逾時，OAS UTF-8 JSON成功1,095公司；上市日期、出表日期等；平臺每月、實际日級出表；OGDL-1 | 現行公司主檔、含非普通股公司，非完整歷史名冊 |
| 上櫃公司：25036 | `https://mopsfin.twse.com.tw/opendata/t187ap03_O.csv`；OAS `https://www.tpex.org.tw/openapi/v1/mopsfin_t187ap03_O` | CSV逾時，英文欄位JSON成功892公司；每日；OGDL-1 | 同上；DateOfListing欄中文schema名稱與市場語義有差異，需結合市場解讀 |
| 市場休市：11761 | `https://www.twse.com.tw/holidaySchedule/holidaySchedule?response=open_data` | CSV只當年2026，27列；按年更新；另官網歷年表2021..2025共117特殊日期列 | 不是交易日全集；2024臨時颱風休市未包含；2016查詢OK但空陣列不能認證完整。TPEx對應歷年API520 |
| 上市盤中暫停／恢復：11677 | `https://www.twse.com.tw/exchangeReport/TWTAWU?response=open_data` | 當期CSV；另歴史JSON可查五年3,110列；時間欄可表達盤中停復；不定期／OGDL-1僅精確開放資源 | 盤中有停復仍可能正常成交；不等於財務、減資或組織變更的整日停止名冊；歷史端點授權另核對 |
| 上櫃歷史公布停復：48665 | `https://www.tpex.org.tw/web/stock/aftertrading/spendi/sprc_result.php?l=zh-tw&o=data`；OAS `/openapi/v1/tpex_spendi_history` | CSV失敗；OAS JSON362列，全為115年度；不定期；OGDL-1 | 名稱有「歷史」但本輪非五年；查不到6488不等於無事件 |
| 上櫃方式／分盤／管理／停止：11736 | `https://www.tpex.org.tw/web/stock/aftertrading/cmode/chtm_result.php?l=zh-tw&o=data`；OAS `/openapi/v1/tpex_cmode` | CSV失敗；OAS22列，資料日1151002；每日；OGDL-1 | 當期狀態快照；變更、分盤不等於沒有行情 |
| 上市除權息預告：89748 | `https://www.twse.com.tw/exchangeReport/TWT48U_ALL?response=open_data` | CSV當期預告；日期、代號、名稱、事件類別／配息等（詳事件報告）；不定期；OGDL-1 | 預告可能修訂／取消／延後，不可直接列為實際已發生。另TWT49U歷史結果仍排除若干減資併案 |
| 上櫃除權息計算：11633 | `https://www.tpex.org.tw/web/stock/exright/dailyquo/exDailyQ_result.php?l=zh-tw&o=data`；OAS `/openapi/v1/tpex_exright_daily` | CSV失敗；OAS9列、1151002..1151005（当期／次營業日）；每日；OGDL-1 | 不等於完整歷史；沒有本輪證明可用的股票／日期參數；6488測試事件不在快照內 |
| 分割／反分割／減資補充 | TWSE `/rwd/zh/reducation/TWTAUU`、`/rwd/zh/change/TWTB8U`；TPEx官方減資／面額變更歷史頁 | `startDate,endDate,response=json`；TWSE五年116減資＋7面額變更列成功；事件日單位／參考價詳事件報告 | 公司行動完整性、各市場所有分割類型、取消／修訂與逐資源權利仍待確認。不能只靠價格跳動自行推斷 |

`/openapi/...` 的TPEx相對路徑均屬 `https://www.tpex.org.tw`。不可把全市場快照列數當普通股數。資料集平臺的詮釋更新時間与行情日期不同，並保存在相應報告和頁面快照；未提供的來源版本以未知表示。

## 4. 日曆、事件與交易狀態的檢查方法

本輪五年價格日期是與**另一張市場成交月報**核對，沒有以2330自身有行情的日期生成日曆，也沒有用週一至週五推算。2024年7月25日、10月2日／3日等臨時休市須與實際市場紀錄及公告核對；年度例假表不能單獨回答。日期不存在時仍須区分未取得月份與市場沒有交易，市場月報不是所有休市原因的文字來源。

事件對齊用本機原始TWT49U檔逐年解析股票代號与資料日期，再比對日線。結果为20個事件都有有效行情／`X`，沒有「事件無價格」「事件無不比價」「不比價無除權息記錄」的剩餘日期。2025-03-18為具體正例：前一市場日、除息生效日、下一市場日皆有日線，當日公司行動資料與不比價註記一致。事件與原價數值證據留在忽略目錄，不把權利尚未確認的原始行情列提交到repository。

上述零差集只適用本輪2330與這組報表，**不能推廣為公司行動全類型完整或未來零漏報保證**。查詢結果最新財報附加欄甚至可能是115年第2季，不能當成2021年時已知版本；本輪不使用財報欄。

最小資料分工：

| 欄位／維度 | 保存與判斷來源 |
| --- | --- |
| `InstrumentId / market / code / validity` | 明確市場、證券類型與存續期；需要歷史識別證據才能跨市場或跨存續期連接 |
| `DailyBar.date / OHLC / volumeShares` | 該股票實際價格回應；原始量單位與轉換版本保留 |
| `MarketSession` | 市場日曆／實際市場成交日／臨時休市公告，狀態可未知 |
| `SecurityTradingEvent` | 有官方證據才記盤中暫停／恢復、整日停止、分盤／變更交易方式；不是由無行情推論 |
| `FetchCoverage` | 下載成功月份、失敗月份、解析結果、期望與實際日期差集；與上兩欄獨立 |
| `NoValidTrade` | 官方零成交或價格無有效值可另標；空值／`--`不轉0，也不產生休市OHLCV |
| `CorporateAction` | 公告／預告時間、實際生效日、類型、數值、來源與確認程度；另存覆蓋範圍，未知不當無事件 |
| `SourceProvenance` | 機構、名稱、dataset ID、實際URL、請求參數、所屬日期／期間、本機下載與最後成功時間、ETag／Last-Modified／來源版本、內容hash |
| `DatasetMetadata` | data.gov.tw詮釋資料更新時間，與行情日期、本機下載時間分開 |

這是取得及資料品質方案，不宣告现有儲存結構已全部支援。

## 5. 授權與後續更新

對精確適用[政府資料開放授權條款第1版](https://data.gov.tw/license)的資源，條款允許不限目的及時間、免授權金的重製、利用與衍生產品。因而在履行顯名等條件下，可用於付費桌面軟體、使用者直接下載、本機長期保存、行情／圖表展示、歷史統計及原始／統計匯出。這是該條款文字與本產品使用方式的對照，不表示額外API穩定性或請求速率得到保證。

顯名需包含提供機關／單位、年份、資料名稱与版本、依OGDL釋出的聲明及條款連結；沒有版本號時寫來源未提供，不自行編造官方版本。下載日期不能拿來冒充資料年份。多來源混用逐一顯名，不宣稱機構背書。

[TWSE一般網站條款](https://www.twse.com.tw/zh/terms/use.html)及[TPEx一般網站條款](https://www.tpex.org.tw/zh-tw/gtsm_disclaimer.html?l=zh-tw)有保留權利及開放資料例外；`STOCK_DAY_ALL`和`STOCK_DAY`不是同一資源。這次只做使用者明確要求的低量技術驗證與本機證據，不據此發布正式歷史下載、原始資料轉供或商業權利承諾。

未找到可確認的歷史修訂日誌、版本列或「自某版本起的差異」接口。TWSE本輪120個歷史月回應未提供ETag／Last-Modified；當期OAS有些提供，但HTTP快取版本不是逐列事件修訂歷史。

後續最小更新方案（提案）：

1. 以已核對市場日曆及已取得月份找中間缺口，不只看`MAX(Date)`；失敗月份保持未驗證。
2. 更新時另取近期月份；明確完整性檢查才按月重取較早資料，以內容雜湊与同股票同日期逐欄比較修訂。尚無證據可承諾固定N天修訂窗，不能將「只補缺漏」寫成已能處理所有修訂。
3. 事件、日曆亦可被修訂；原始版本與本機取得時間一起保存。取消、解析失敗或空回應不能替換最後有效資料。
4. 切換CSV／OAS／歷史端點時重新核對身分、日期、成交量範圍與單位、價格口徑及授權。TPEx歷史月表是否排除鉅額等口徑需依報表說明驗證，不與當期快照偷偷拼接。

## 6. 可重複執行

本節原有線上命令保留為前次驗證紀錄；其中網站歷史月報已被使用者排除，不應再執行。本次僅限官方 OpenAPI 的重跑方式見第 9 節。

採repository既有PowerShell慣例，使用PowerShell 7與內建JSON／HTTP，無新套件、資料庫或服務。從repo根目錄執行：

```powershell
# 少量開放行情快照與歷史參數檢查（6次序列請求）
./scripts/histolens-source-verification/Probe-Resources.ps1 -Names twse-daily-csv,twse-daily-json,twse-daily-history-parameter-check,tpex-daily-csv,tpex-daily-json,tpex-daily-history-parameter-check

# 真實五年：TWSE 60個股月份＋60市場月份；預设2330
./scripts/histolens-source-verification/Verify-History.ps1 -Market TWSE

# TPEx 預設6488；遇HTTP/傳輸錯誤立即停止後續月份，不重試風暴
./scripts/histolens-source-verification/Verify-History.ps1 -Market TPEx

# 十年下界，只抽一個月；不是十年全量成功宣告
./scripts/histolens-source-verification/Verify-History.ps1 -Market TWSE -Start 2016-01-01 -End 2016-01-31

# 離線重播本輪既有證據並對齊事件，不重新連線
./scripts/histolens-source-verification/Verify-History.ps1 -Offline -OutputDirectory "$PWD/artifacts/histolens-source-verification/history-twse-five-year" -EventsDirectory "$PWD/artifacts/histolens-source-verification/events"

# 純合成解析邊界測試，無網路
./scripts/histolens-source-verification/Test-Verification.ps1
```

`resources.json`明列各原始CSV、對應OAS及歷史抽查URL，可用`-Names`指定必要項目；未指定時僅取得兩市場4個當期CSV／JSON，不重抓全清單。Probe保存內容、HTTP狀態、格式、大小、SHA256、URL、時間及失敗訊息；JSON會摘出字段／筆數。CSV及複雜報表需依實際欄位解析，未解析不填假筆數。來源文案／schema來源與本次檔案，另存於identity、events子目錄。重抓事件時可選`twse-exright-2021`至`twse-exright-2025`，將新輸出目錄傳給`-EventsDirectory`；不要把同年多份回應混在一起，來源版本必須可區分。

所有輸出強制位於已被`.gitignore`忽略的`artifacts/`。新線上run使用新目錄，拒絕覆寫已有request manifest；離線重播先驗HTTP成功與保存內容hash，再另寫`*-replay.json`。hash對應保存的解碼內容或原byte[]，不宣稱網路壓縮傳輸位元組。沒有保存權利的原始行情不提交；本輪沒有stage、commit或push原始資料。

本輪證據路徑：`artifacts/histolens-source-verification/{identity,events,price-snapshots,history-twse-five-year,history-tpex-five-year,history-twse-ten-year-boundary,history-twse-202012-boundary}/`。這些是本機證據，乾淨clone不會帶入，需依腳本重新取數；外部回應會隨時間變動。

存取紀錄：預設sandbox網路與某些寫入遭拒後，在授權範圍使用正常升權執行；這是本機權限，並非官方資料封鎖。web讀取工具另有unsupported CSV、TPEx403，正常shell直連部分成功；TPEx歷史520及CSV520均未繞過。公司基本資料原CSV30秒逾時，改用資料集已連結且schema確認的OAS；歷史除權息五年一次請求逾時後改有界年度取得。無登入、金鑰、購買、付費方案、驗證碼解答或代理IP；本輪未遇429，仍不能推定沒有官方限流。

驗證程式最初終端摘要對ordered dictionary選屬性顯示null，保存的`summary.json`內容正確；已改以PSCustomObject輸出並離線重播成功。初次離線重播因sandbox寫入拒絕未保存重播摘要，正常升權後成功，並未重抓行情或改寫raw。7項合成測試通過：有效資料、空價保留、仟股轉股、未知單位拒絕、重複日期、錯誤月份回聲、hash不符；五年重播標準化檔hash與首次結果一致，20個事件交叉對齊無差集。

## 7. 可行性分類及最小替代路徑

| 分類 | 本輪結果 |
| --- | --- |
| 已驗證可用 | 精確開放主檔與當期行情OAS；TWSE2330五年原始OHLCV技術下載與獨立市場日對齊；已知20除息的日期對齊。各自的有效範圍不可互相替代 |
| 只能用於每日更新，不能首次補齊歷史 | 11549／11370最新行情、11736當期交易方式、89748當期預告、11633當期／次營業日結果；錯過的日期不保證仍可由原開放快照補回 |
| 部分符合，但缺必要資訊 | TDCC含終止記錄但缺身分有效期間；年度日曆缺臨時休市；TWTAWU／48665未覆蓋所有停止類型；TWSE三張事件結果仍不證明所有公司行動完整 |
| 無法使用（限本輪實測） | TPEx歷史API HTTP520、部分原始CSV逾時或520，不能作本輪已驗證可用路徑。這不是永久不可用判斷 |
| 尚未確認 | 十年全量連續性、TPEx五年歷史、全類型公司行動／停牌、歷史身分沿革、修訂／取消版本、一般歷史端點的產品化使用權與穩定限流契約 |

缺的最小集合：**可合法產品化的首次歷史OHLCV路徑（尤其TPEx）、兩市場完整歷史交易日與個股狀態、價格不連續事件的完整覆蓋及修訂證據、需要跨存續期時的歷史證券身分。** 不還原股價能降低計算實作範圍，不能讓來源未知自動變成完整。

最小替代路徑先選兩者之一：取得其他**官方歷史CSV／JSON檔**的精確權利及覆蓋確認，再按單一使用者單一股票按月下載；或匯入使用者**合法取得的CSV**，附市場、代號／身分、期間、OHLCV單位、事件與日曆來源及覆蓋聲明。匯入亦不能自動證明授權或事件完整。現有開放快照可後續累積，本地缺口與修訂检查仍獨立。暫不引入付費第三方、全市場資料庫或行情伺服器。

## 8. 本輪同步的掃描修正

使用者已明確要求第一版不還原股價且應完成掃描。相似度引擎現在把已知公司行動、不比價與事件覆蓋未知當作提示，不再因參考窗口跨事件而中止；歷史候選也保留原價計算並附提醒。圖表標示歷史與近期各自事件日期、類型、來源，原始OHLC不變。真正無有效行情、停牌／缺價、日曆、來源身分、快照結構與hash問題仍檢查；不以補0或挪動日期假造可算窗口。

修改限於相似度與圖表／提示及相關測試，舊進階條件研究仍維持既有保守政策，沒有擴充其他分析功能。HistoLens完整測試 **394/394通過、0略過，Build成功**；原先一條期待舊「無法研究」提示的測試已按新政策更新並加掃描成功斷言，進階研究阻擋斷言保留。合成資料離屏圖已目視驗證；它不是官方行情證據。未完成真實桌面互動／DPI驗收、未發布正式下載功能或更新商店套件。

效率紀錄：主任務969，子代理1631／1632／1633；實際採multi。設定角色為兩個agt_ba及一個agt_dev，角色設定模型gpt-6.1-sol、推理xhigh；實際執行模型未經工具確認，保留未知。已收集、驗收並確認三個代理completed狀態；環境沒有獨立close-agent工具，沒有冒稱資源已關閉，ClosedNormally保留NULL。沒有自動產生效率報表。

## 9. 官方 OpenAPI 是否提供個股歷史回補（2026-10-04 續查）

**目前公開契約沒有提供可指定股票及歷史期間、回補五年或十年每日 OHLCV 的 API。** 有名稱包含「歷史」的 API，但本次取得的是最新月份的指數或近期交易限制紀錄，不能替代個股日線。結論限定於當次公開的 TWSE OpenAPI 規格與下列實測，不宣稱所有官方服務皆不存在歷史資料。

### 9.1 完整規格盤點

重新下載 [官方 Swagger](https://openapi.twse.com.tw/v1/swagger.json)，共 **143 個 GET 路徑**；路徑層及 GET 操作都沒有宣告 `parameters`，也沒有全域共用參數定義。因此沒有文件化的股票、起訖日期、月份、年份或分頁輸入，不能把未公開參數當成契約。規格檔 SHA256 為 `06E1CEA82448361E733A0AD1AE16E52F5D5D6B71905ACD078472F852C32C0EB0`。

規格明列歡迎程式介接，並連結 [TWSE 使用條款](https://www.twse.com.tw/zh/terms/use.html)及[政府資料開放授權](https://data.gov.tw/license)。對 11549 等明確對應的開放資源，可依 OGDL-1 利用並履行顯名；不因一般網站的自動擷取限制就將正式開放 API 一律判為不可用，也不把 API 授權延伸至網站 `STOCK_DAY` 等歷史查詢。

### 9.2 真實回應

以下路徑前綴皆為 `https://openapi.twse.com.tw/v1`。2026-10-04 台灣時間 **13:36:08 至 13:36:47**，共 10 次資料 GET（9 個不同端點，日行情多一次參數比較），依序間隔 3 秒；全部 HTTP 200 且可解析為 JSON 陣列。3 秒是探針自訂節流，不是官方額度。無登入、金鑰、驗證碼或繞過存取控制；未使用被排除的網站歷史查詢。筆數是全表，不是普通股數或單一股票的歷史筆數。

| 路徑／資料集 | 本次全表筆數及資料期間 | 對個股每日 OHLCV 的適用性 |
| --- | --- | --- |
| `/exchangeReport/STOCK_DAY_ALL`／[11549](https://data.gov.tw/dataset/11549) | 1,380 筆，全為 2026-10-02；2330 僅 1 筆 | 有 OHLC、成交股數，但僅最新日 |
| `/exchangeReport/FMSRFK_ALL`／[11550](https://data.gov.tw/dataset/11550) | 33,596 筆，月份全為 2026-09；2330 僅 1 筆 | 月最高／最低、量值及均價等彙總，不是逐日日線 |
| `/exchangeReport/FMNPTK_ALL`／[11551](https://data.gov.tw/dataset/11551) | 72,048 筆，年度全為 2025；2330 僅 1 筆 | 年最高／最低及其日期、量值和均價，不是逐日日線 |
| `/indicesReport/MI_5MINS_HIST`／[11755](https://data.gov.tw/dataset/11755) | 2 筆，2026-10-01..2026-10-02 | 加權股價指數 OHLC，非個股；官方說明限定最新月份 |
| `/indicesReport/TAI50I`／[11670](https://data.gov.tw/dataset/11670) | 2 筆，2026-10-01..2026-10-02 | 臺灣 50 收盤／報酬指數，非成分股行情；官方說明限定最新月份 |
| `/indicesReport/FRMSA`／[11669](https://data.gov.tw/dataset/11669) | 2 筆，2026-10-01..2026-10-02 | 寶島收盤／報酬指數，非個股；官方說明限定最新月份 |
| `/exchangeReport/FMTQIK`／[11672](https://data.gov.tw/dataset/11672) | 2 筆，2026-10-01..2026-10-02 | 市場每日成交統計，亦不足以建立五年交易日曆 |
| `/exchangeReport/BWIBBU_d` | 1,082 筆，全為 2026-10-02；2330 僅 1 筆 | 名稱含「依日期查詢」，但無日期參數；僅當日收盤／估值，缺完整 OHLCV |
| `/exchangeReport/TWTBAU2` | 18 筆，開始日 2026-09-29..2026-10-02 | 暫停先賣後買當沖的交易限制，非整日停牌清單或價格歷史 |

一次比較請求 `STOCK_DAY_ALL?date=20210104&stockNo=2330` 仍回相同 1,380 筆、相同最新日，與不帶參數的保存內容 SHA256 完全一致：`FD632F1EC3058E354D8D9D319CEC8A683E2EFB82A26338D6A3D0FFBDD5E4319D`。HTTP 200 不代表歷史參數被接受。

HTTP 類型皆為 `application/json`，未宣告 charset，內容可依 UTF-8 解析。多數日期為民國字串（例 `1151002`）、月為 `11509`、年為 `114`；`BWIBBU_d.Date` 則是公曆 `20261002`，不可統一套用單一曆法。原始回應保留，沒有把月／年彙總偽造成日線。ETag／Last-Modified 已逐請求保存，但不等於有逐列歷史修訂清單。

### 9.3 重跑與缺口

沿用原探針，新增僅含官方 OpenAPI 的驗證清單（舊工具已移除）。PowerShell 7 從專案根目錄執行：

```powershell
$catalog = './scripts/histolens-source-verification/openapi-history-resources.json'
$names = @((Get-Content -LiteralPath $catalog -Raw | ConvertFrom-Json).name)
./scripts/histolens-source-verification/Probe-Resources.ps1 -Catalog $catalog -Names $names
```

明確指定名稱可避免回落到原探針預設的其他資源；每次產生新的忽略目錄。本次保存內容共 25,444,469 bytes，主要是只有一期的月／年全表，不宜為同一問題反覆下載。

本機證據位於 `artifacts/histolens-source-verification/openapi-history-20261004-1334/`：`swagger.json`、完整 `catalog.json`、`requests.jsonl`、10 份 `.raw` 與日期彙整 `summary.json`。已確認全部檔案雜湊符合 manifest，且 artifacts 被 Git 忽略；原始行情不提交。11669／11670 編號於下載後依官方資料集補入清單，原始 manifest 的兩個 dataset=null 保留不改寫。

可行性：`STOCK_DAY_ALL` 可供當日取得與後續本機累積，但不能用它補齊錯過的多年歷史，或保證補回中間缺日。首次五年／十年仍缺每日個股 OHLCV 來源。月／年摘要與指數不能填補缺口。本次沒有新增正式下載功能，亦未修改行情／研究引擎。
