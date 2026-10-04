# HistoLens 股票選擇清單

更新：2026-10-04，0.3.16。只使用 FinMind，取代先前官方公司主檔下載。

實際端點：`https://api.finmindtrade.com/api/v4/data?dataset=TaiwanStockInfo`，UTF-8 JSON，讀取 `stock_id/stock_name/type/date/industry_category`。每次明確按更新才匿名 GET 一次；45 秒逾時、8 MiB 上限、無自動重試與其他來源備援。

四碼代號按最新日期群選市場及名稱，允許同股多產業列；最新市場／名稱有衝突則拒絕更新。保留 `twse/tpex` 候選，排除來源已識別 ETF、ETN、指數、存託與受益證券。這是 FinMind 選股索引，可能保留舊股票，**不是保證目前交易的公司名冊，更不是完整歷史證券身分表**。

2026-10-04 實際 GET HTTP 200，經上述過濾為 **2,142 筆**。證據在 `artifacts/histolens-source-verification/finmind-only-20261004/`。僅請求一次主檔，不按每股下載行情。本機清單 `UpdatedAtUtc` 是成功取得時間，不是交易日或來源版本日。

資料管理輸入完整代號會定位右側選單，直接選項目會回填代號及市場。同碼優先目前市場；未完成／未知代號保留輸入但清除選取。顯示代號、名稱與上市／上櫃；三語市場標籤已測試。沒有清單仍可手動輸入，下次只讀本機清單，選股與更新清單不觸發行情下載。

新版保存格式 version 2，只保存 FinMind 資源 URL；version 1 的官方 URL 僅供舊檔離線驗證，不連線，更新後採新版來源。取消、解析／身分衝突、寫檔失敗均保留舊清單及研究。

來源合約、保存、取消與 UI 雙向選股已納入完整 **529／529 HistoLens** 測試；三語 UI 定向 **9／9** 通過。更多下載、身分與授權界線見 [FinMind 接入](HISTOLENS-FINMIND-IMPLEMENTATION.md)。原官方清單取得數與測試證據保留於舊版實作紀錄及 artifacts，非目前來源。
