# HistoLens：日曆、交易狀態與公司行動來源實測

> 歷史查證／舊版紀錄：0.3.16 已依使用者決策只保留 FinMind。本文的官方連線及操作不再是現行功能，舊官方下載器與線上驗證腳本已移除；保存本文作證據追溯。目前流程與限制見 [FinMind 接入紀錄](HISTOLENS-FINMIND-IMPLEMENTATION.md)。

查證日期：2026-10-04（Asia/Taipei）。本文件是本輪實際取得證據，不以既有 [TWSE 來源紀錄](HISTOLENS-TWSE-SOURCES.md) 的舊結果冒充重新驗證。行情下載與市場交易日逐筆比對由本輪主驗證報告整合。

## 1. 分析範圍與證據

- 樣本：上市 `2330` 台積電、上櫃 `6488` 環球晶；共同期間 `2021-01-01` 至 `2025-12-31`。十年下界查詢 `2016`，不把單一年下界成功說成十年全部驗證。
- 範圍：11761、11677、48665、11736、89748、11633，以及官方歷史除權息、減資、變更面額、財務停止買賣報表。未實作正式下載器、未建立價格還原算法。
- 原始回應存於忽略版控的 `artifacts/histolens-source-verification/events/`；`git check-ignore` 已確認該目錄內回應被忽略。原始來源權利未完整確認的歷史內容不提交。
- 本子任務經升權執行 **51 次外部正常請求嘗試，41 次 HTTP 200 並保存回應，10 次未取得成功資料**：HTTP 520 六次、送出失敗兩次、DNS 失敗一次、25 秒逾時一次。升權前另有一次本機沙箱拒絕通訊端及建立目錄，不算來源拒絕。
- 請求為有限、按序正常網址；沒有登入、金鑰、購買、驗證碼操作、改換身分或繞過限制。未見已公布可依賴的速率上限；少量成功不能推出無限額存取。取得 HTTP 520 不代表已證明該來源全球不可用。
- CSV `.raw` 保存 HTTP 解碼後的回應串流；其餘文字由 PowerShell 讀取後以 UTF-8 存檔，並有結尾換行。檔案不是線上壓縮傳輸位元組的宣稱。來源日期、本機取得時間及資料集詮釋更新時間分開記錄。

### 政府資料集及真正資源

下表的授權／更新欄是**資料集頁面的明示資訊**；成功取得欄才是**本輪實際觀察**。同機構其他歷史路徑的授權仍須個別核對。

| 資料集與用途 | 資料集頁面真正連結的資源 | 實測格式、主要欄位／單位 | 頁面更新／授權／詮釋時間 | 實際取得與限制 |
| --- | --- | --- | --- | --- |
| [11761 集中市場開休市](https://data.gov.tw/dataset/11761) | `https://www.twse.com.tw/holidaySchedule/holidaySchedule?response=open_data` | CSV，`text/csv; charset=utf-8`；名稱、日期、星期、說明。日期為民國 `yyyMMdd`，無行情單位。 | 每年；政府資料開放授權第1版、免費；2026-08-21 15:31。頁面明示最新一版。 | HTTP 200，27 列，2026-01-01..2026-12-25；內容包含開始交易、最後交易、僅交割及假日等特殊日期，**不是每天一列的市場交易日全集**。 |
| [11677 上市暫停交易證券](https://data.gov.tw/dataset/11677) | `https://www.twse.com.tw/exchangeReport/TWTAWU?response=open_data` | UTF-8 CSV；編號、證券代號、名稱、暫停日期／時間、恢復日期／時間；民國 `yyyMMdd`、`HHmmss`。 | 不定期；同上開放授權、免費；2026-06-30 14:15。 | HTTP 200，1 列：1218 泰山，2026-08-13 08:00 至 2026-08-14 08:00。此無參數快照不能補齊 2021–2025。 |
| [48665 上櫃歷史暫停／恢復](https://data.gov.tw/dataset/48665) | `https://www.tpex.org.tw/web/stock/aftertrading/spendi/sprc_result.php?l=zh-tw&o=data` | 原連結請求送出失敗，未取得 CSV。官方 OpenAPI 另有同名 `/tpex_spendi_history`，實際取得 JSON 物件陣列。 | 不定期；同上開放授權、免費；2026-06-08 08:48。頁面註明官方 OpenAPI。 | OpenAPI HTTP 200，362 列，`Date` 全為 `115`（年度），暫停日2026-01-09..2026-08-12、恢復日2026-01-12..2026-08-13；不是五年全集。6488 未在此當年表出現，不能因此說歷史沒有暫停。 |
| [11736 上櫃變更／分盤／管理／停止](https://data.gov.tw/dataset/11736) | `https://www.tpex.org.tw/web/stock/aftertrading/cmode/chtm_result.php?l=zh-tw&o=data` | 原連結請求送出失敗。另實取 `/tpex_cmode` JSON；`Date`、代號、名稱、四種狀態、撮合循環、財務重點。撮合時間單位未逐筆正例確認。 | 每日；同上開放授權、免費；2024-12-05 08:55。 | OpenAPI HTTP 200，22 列，全為2026-10-02；為目前清單，非過往每日狀態。6488 不在清單不證明五年每一天正常。 |
| [89748 上市除權息預告](https://data.gov.tw/dataset/89748) | `https://www.twse.com.tw/exchangeReport/TWT48U_ALL?response=open_data` | UTF-8 CSV；除權息日期、代號、名稱、權／息、配股率、認購價、現金股利及認購股數等12欄。金額／配股率欄需保留原欄名，不把配股率當股數。 | 不定期；同上開放授權、免費；2026-06-30 11:18。 | HTTP 200，58 列，預定事件日2026-10-05..2026-10-28；含 ETF，不能只以數字代號判普通股。無歷史或取消、延期紀錄。 |
| [11633 上櫃除權息計算結果](https://data.gov.tw/dataset/11633) | `https://www.tpex.org.tw/web/stock/exright/dailyquo/exDailyQ_result.php?l=zh-tw&o=data` | 原連結 HTTP 520。另實取 `/tpex_exright_daily` JSON，21欄含前收、參考價、權值／息值、現金股利、每仟股配股與增資股數。 | 每日；同上開放授權、免費；2024-12-05 08:56。頁面描述次一營業日要除權息者。 | OpenAPI HTTP 200，9 列，除權息日期2026-10-02..2026-10-05；不是完整歷史，6488 未在當期回應中。 |

上櫃三個 OpenAPI 的完整網址為 `https://www.tpex.org.tw/openapi/v1/{名稱}`。實取 [官方 Swagger](https://www.tpex.org.tw/openapi/swagger.json) 均未為上述三項宣告查詢參數。回應 `Content-Type: application/json` 未附 charset，JSON 解析成功；本機存檔為 UTF-8。這證明替代表示可下載，**不自動替所有其他網站歷史端點確定開放授權**。

## 2. 已確認流程及歷史取得結果

### 上市歷史事件

| 報表／實際官方端點 | 參數、格式及回聲 | 本輪筆數／範圍 | 對2330與完整性的意義 |
| --- | --- | --- | --- |
| [除權息計算結果](https://www.twse.com.tw/zh/announcement/ex-right/twt49u.html)：`https://www.twse.com.tw/rwd/zh/exRight/TWT49U` | `startDate=yyyyMMdd&endDate=yyyyMMdd&response=json`；UTF-8 JSON；`stat, title, fields, data, strDate, endDate, notes`。 | 五年一次查詢25秒逾時。改逐年成功：2021=973、2022=1027、2023=1086、2024=1184、2025=1358，共5628事件列；2330每年4列，共20列，2021-03-17..2025-12-11。2016另成功734列，2330事件2016-06-27。 | 五年**本報表**已取得；含其他證券類型，須身份篩選。官方宣告自2003-05-05提供，不等於本輪已驗證全部十年。回應明示排除除息併退還股款減資或分割減資，不能當完整行動目錄。 |
| [減資恢復價](https://www.twse.com.tw/zh/announcement/reduction/twtauu.html)：`https://www.twse.com.tw/rwd/zh/reducation/TWTAUU` | 同上日期參數；UTF-8 JSON；範圍回聲 `strDate,endDate`；官方路徑拼字 `reducation`。 | 一次2021–2025成功116列，恢復日2021-01-04..2025-12-22；2330零列。 | 可補減資事件的恢復日期、原因、前收及參考價。查詢零列不能證明2330所有類型事件不存在；結果頂層沒有明列停止開始日、換股率，詳細連結尚未逐筆驗證。 |
| [變更面額恢復價](https://www.twse.com.tw/zh/announcement/change/twtb8u.html)：`https://www.twse.com.tw/rwd/zh/change/TWTB8U` | 同上；UTF-8 JSON；範圍回聲位於 `params.startDate,endDate`。 | 一次2021–2025成功7列，恢復日2021-10-18..2025-11-17；2330零列。 | 支持面額變更已知事件標記；結果頂層含前收／參考價而沒有完整分割比例與停止起日。不能由此證明所有分割、反分割、公司分拆、合併及其他不連續事件都涵蓋。 |
| [暫停交易歷史](https://www.twse.com.tw/zh/trading/historical/twtawu.html)：`https://www.twse.com.tw/rwd/zh/afterTrading/TWTAWU` | `startDate,endDate,querytype=1&stockNo=2330` 或 `querytype=3`（全部）；UTF-8 JSON，非空結果有 `params` 範圍回聲。頁面宣告自2011-10-03提供。 | 全部2021–2025成功3110列，暫停日2021-02-04..2025-12-30，2330無列；含大量權證。四碼代號列73只是字串形狀統計，不當普通股名冊。 | 單股2330回無符合資料訊息及查詢期間title，沒有 `fields/data`；不能套用非空形狀解析，也不能宣告所有停牌型態不存在。 |
| [停止買賣](https://www.twse.com.tw/zh/listed/violations/stop.html)：`https://www.twse.com.tw/rwd/zh/violation/stop?response=json` | UTF-8 JSON多表 `tables`，沒有歷史日期表單。 | 目前表2026-10-04，1公司1589；含原因與停止開始日2026-04-07。 | 官方頁明示只揭示財務業務異常造成停止，組織異動、改組、減資等另依MOPS。不能補五年全類型停止／恢復。 |

TWT49U實測15欄：資料日期、代號、名稱、前收、參考價、權值+息值、權/息、漲停、跌停、競價基準、減除股利參考價、詳細資料，以及最近申報財報三欄。其總表沒有獨立現金股利／配股率欄；詳細頁尚未實取。價格、權息值使用原報表的價格數值，不作還原。總表歷史事件的「最近申報財報」實際指向2026年資料，不能冒當事件當年的快照或來源版本。

TWTAUU實測11欄：恢復日、代號、名稱、停止前收、恢復參考價、漲停、跌停、競價基準、除權參考價、減資原因、詳細資料。TWTB8U為其中恢復日、代號、名稱、前收、參考價、漲停、跌停、競價基準、詳細資料9欄。`--`需保留缺值，不轉0。

### 日曆及臨時休市

[官方日曆頁](https://www.twse.com.tw/zh/trading/holiday.html) 表單實際使用同一資源 `/holidaySchedule/holidaySchedule`，歷史JSON參數為 `date=yyyy0101&response=json`。欄位改為日期、名稱、說明，日期是公曆 `yyyy-MM-dd`。實取2021=27、2022=20、2023=26、2024=20、2025=24，共117列特殊日期；不是117個市場交易日。年度開始／最後交易日不可誤算休市。

2024表沒有10月2日／3日；[證交所10月2日全日休市公告](https://www.twse.com.tw/staticFiles/news/news/tsecnews/8a8216d69236c2e301924806d5b4004e.pdf) 與 [10月3日全日休市公告](https://investoredu.twse.com.tw/Mobile_pages/..%2FFileSystem%2FFileUpload%2F2c6e50d4-a390-439c-a2c4-823937dee4b2.pdf) 是官方反例。後兩項透過web讀取官方公告查證，未另大量掃描公告檔案。故不能只用年度表、週末與固定國假推出完整歷史日曆；需要再核對實際市場交易日與臨時公告。

2016日曆請求HTTP200，`stat=ok,date=20160101,total=0,data=[]`；有回聲也不表示2016没有休市。官方目前表單起年為2021，十年日曆下界未驗證可用。

[櫃買日曆頁](https://www.tpex.org.tw/zh-tw/announce/market/holiday.html)／舊頁 `https://www.tpex.org.tw/web/bulletin/trading_date/trading_date.php?l=zh-tw` 可取得HTTP200。表單起年2006，官方 action `bulletin/tradingDate`。正常請求 `https://www.tpex.org.tw/www/zh-tw/bulletin/tradingDate?date=2024%2F01%2F01&response=json` 回HTTP520；未取得上櫃歷史日曆內容，不能拿上市日曆替上櫃全部日期背書。

### 上櫃其他歷史事件及樣本

| 官方頁面 | 頁面所示範圍／參數與API action | 實際請求結果 |
| --- | --- | --- |
| [除權息歷史](https://www.tpex.org.tw/zh-tw/announce/market/ex/cal.html) | 自2008-01-02，另連2000-09..2007-12舊資料；`startDate,endDate`，action `bulletin/exDailyQ`。 | 指定2025-07-16單日JSON請求HTTP520，未取得歷史事件列。 |
| [歷史公布暫停／恢復](https://www.tpex.org.tw/zh-tw/announce/market/halt/historical.html) | 表單 `date` 年度、`cate=1`上櫃股票；起日屬性2020-12-01；action `bulletin/sprcHis`。 | 頁面首次DNS失敗，正常重試HTTP200。指定2025年度上櫃股票API請求HTTP520，未取得歷史列。 |
| [減資恢復參考價](https://www.tpex.org.tw/zh-tw/announce/market/reduction/reference.html) | 宣告自2013-01；`startDate,endDate`；action `bulletin/revivt`。 | 2025全年JSON請求HTTP520，未取得事件。 |
| [面額變更恢復參考價](https://www.tpex.org.tw/zh-tw/announce/market/change/reference.html) | 日期表單起日2019-09-09；`startDate,endDate`；action `bulletin/pvChgRslt`。 | 2025全年JSON請求HTTP520，未取得事件。 |

上述API完整前綴為 `https://www.tpex.org.tw/www/zh-tw/`；日期值使用官方表單公曆年月日，例如 `startDate=2025%2F01%2F01&endDate=2025%2F12%2F31&response=json`。日期介面可選不等於內容已下載，也不等於2016下界保證完整。

[環球晶官方股東服務](https://www.sas-globalwafers.com/investor-page/shareholder-service/) 本次HTTP200，股利表明列**2025-07-16除息、每股新台幣6.0元**。這是發行公司官方正例，不是政府資料集授權資源，也不宣告政府開放事件已取齊；6488行情亦未由本子任務取得，事件／OHLC對齊仍未完成。本輪政府候選快照沒查到6488，已被此正例證明不能解讀為沒有歷史事件。

2330正例為2025-03-18除息：TWT49U類型「息」，前收、參考價與權息值保留於忽略版控的原始回應。本輪主代理另外回報已取得60月2330行情1214筆、獨立FMTQIK市場日1214筆，無缺口；20個行情`X`日期與本子任務20個除息日期相符。這是主驗證的對齊結果，由主報告保留明細，不冒稱本子任務獨立重抓行情。上市五年市場日可由該結果補齊；年度開休市表自身仍不完整。

## 3. 業務規則與驗收條件

下列以使用者本輪指示為已確認要求，不把來源現行實作當成需求：

1. **先不做股價還原；掃描要能執行完成，圖表在事件生效日註記。** 已知公司行動或公司行動覆蓋尚未確認，不能觸發整體「無法掃描／保留前次結果」。以原始OHLC研究時，需明示原價、跨事件及來源覆蓋狀況；不把完成掃描冒當調整後價格可比性已證明。
2. 市場開休市、個股交易事件、當日有效成交及行情下載完整度各自記錄；一個互斥狀態欄無法保留上述資訊。盤中暫停後恢復且有行情，不應刪除整日OHLCV；單純缺行情不得改成停牌。
3. 預告日、預定生效日、實際生效日與下載時間分開；只有預告資料時圖表／說明不得標成已發生。未找到事件列只代表查詢未找到，完整性需有範圍及事件類型證據。
4. 不同報表的 `Date` 不可共用無條件轉換：spendi_history實際是民國年度115；cmode為民國1151002；exright為事件日；TWSE年度JSON為公曆；TWT49U為民國中文字日期。
5. TPEx欄名存在易混值：`StockDividend`是**權值**，`CashDividend`是**息值**；現金股利是拼字 `CashDivdend`，每仟股無償配股是 `StockDivdendThousandShares`。保留原欄名，不把權值當配股股數；21欄參考[官方Swagger](https://www.tpex.org.tw/openapi/swagger.json)。
6. 驗收須區分「報表取得成功且該股零列」「無符合資料訊息」「HTTP失敗」「來源無此類型完整性承諾」。中間缺口與修訂應可重查既有期間，不能只看最大日期。
7. 對現金股利、配股率、股數、價格及換股率逐欄記錄原單位；本次沒有還原用途，尚未驗證的詳細頁／比例不能自行推算補齊。尤其不能把最新行情OpenAPI股數單位套到尚未取得的上櫃歷史月行情。

## 4. 例外流程與可重現性

- 五年TWT49U首次請求25秒逾時後，按年度取得五份結果；逾時只證明本次沒有收到，不能證明API規定最多一年。尚未確認任意五年／十年範圍一次取得的能力。
- TPEx舊CSV端點兩次送出失敗、一次520；官方網站歷史JSON五項本輪正常單次請求亦520。官方頁與OpenAPI仍可讀，不把此結果寫成機構完全不可用；未繞過限制。
- 空結果與多表回應各造成一次**本機摘要解析錯誤**（Cannot index into a null array）：2330暫停單股空訊息、停止買賣 `tables`。兩次網路實際HTTP200且原始內容已存；後續只對保存回應本機重新讀取，沒有隱藏為全部驗證成功。
- 當來源下載或驗證失敗，保留既有有效資料、保留失敗原因、以失敗或未知覆蓋標示。事件同步失敗不能抹除舊事件或把空回應覆蓋成「確定無事件」。
- JSON未見逐筆事件取消／延期／歷次版本或歷史修訂日誌欄。本輪不宣稱已確認來源有完整版本歷史；重新查同一期間只能取得當時公開狀態，仍須保存取得時間及差異。

可重跑資源及實際回應索引存於 `artifacts/histolens-source-verification/events/request-manifest.csv`。主驗證程式使用此正常官方網址與參數；本文件不指定新的產品技術方案。成功body檔案可離線核對，不需要重新請求全部資源。

部分最初請求沒有保存精確請求時間，該欄維持空值。另有主代理產生的`request-manifest-with-save-times.csv`，`EvidenceSavedAtUtc`取自既有證據檔落盤時間；它不冒充伺服器更新、事件生效日或精確請求起時。圖表註記與掃描的程式驗收不在本來源子任務執行，整合後測試結果見[主報告第8節](HISTOLENS-DATA-VERIFICATION.md#8-本輪同步的掃描修正)。

## 5. 衝突、缺口與可行性結論

| 分類 | 本子任務結論 |
| --- | --- |
| 已驗證可用 | 上市指定五年TWT49U按年度歷史結果；上市減資與面額變更五年結果；上市暫停交易特定報表五年查詢；兩交易所最新快照／部分OpenAPI。這些只證明各自報表範圍。 |
| 只能累積當期更新，不能首次回補 | 六候選資料集無參數資源／替代OpenAPI本次回應均沒有完整五年；TWSE日曆CSV為最新年度，TWSE除權預告為未來表，TPExcmode／除權為當期表，TPEx歷史暫停OpenAPI只回當年。 |
| 部分符合但缺必要資訊 | 上市歷史日曆特殊日期表遺漏臨時休市；暫停報表不是全類型整日停止；除權息明示排除部分減資併案；面額報表／減資總表詳細比例與停止期間未逐項驗證。 |
| 本輪無法取得 | 所記六次TPEx HTTP520正常請求與兩個舊CSV送出失敗；限定為此次存取結果，不說永久無法使用。 |
| 尚未確認 | 完整2016–2025公司行動／日曆；上櫃五年歷史事件與狀態；所有類型分割／反分割、分拆減資、其他價格不連續；來源修訂／取消／延期履歷；其他歷史端點完整使用權。 |

**只使用目前這些政府開放資源，本輪尚不能宣告已完整支援首次五年或十年研究。** 尚缺可驗證的上櫃歷史資料、上櫃完整市場交易日與臨時休市核對、兩市場全類型停止／恢復與事件完整性，以及歷史查詢資源的權利確認。上市五年行情及市場日已由主驗證取得，但不因此補足其他缺口。

最小候選路徑是繼續個別確認上述官方歷史報表權利及正常存取；上櫃存取仍失敗時，可研究使用者合法取得的CSV及事件／日曆匯入，保留來源、單位、身份與覆蓋資訊。這只是未實作的方案提案，不包含購買、付費帳號、外部轉供或全市場資料平台。

使用者最新要求已修正既有文件中「公司行動／不可比邊界導致無法掃描」行為：資料研究可以完成並展示已知事件與未知覆蓋，但不能同時宣告價格已還原或完整事件已驗證。跨事件的具體相似度解讀口徑應在主規格明說，不能由本次來源查證自行替需求方定義。
