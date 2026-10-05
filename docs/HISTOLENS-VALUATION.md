# HistoLens：開始分析時查詢本益比

更新：2026-10-04，0.3.16。只使用 FinMind，原 TWSE／TPEx 本益比提供者已移除。

按「尋找相似區間」或進階「執行研究」時，另外取得所選股票最新公布本益比；開啟軟體、選股及修改日期不查詢，合成資料不連線。顯示實際資料日、上市／上櫃，提示中保留 FinMind 來源、URL 與取得時間，不冒稱今日即時值。

實際端點為 `https://api.finmindtrade.com/api/v4/data`，參數 `dataset=TaiwanStockPER&data_id=股票代號&start_date=近14天起日&end_date=台灣今日`。UTF-8 JSON，讀取 `date/stock_id/PER`，按最新資料日期取值；最新列為 0、空值或不適用時顯示不可用，不倒退挑舊的有效倍數。

每次分析重新查詢，只有單股一次匿名請求，無重試或其他網站備援；提供者逾時 45 秒，UI 工作另有 12 秒上限。取消、切換股票、工作失效及關閉視窗會取消舊請求，晚到結果不覆蓋新股票。查詢失敗或無值不阻止掃描、不清除歷史結果。

本益比不參與相似度分數、不代表歷史當時已知值、不覆寫 OHLC，也不寫入行情快取或研究快照。回應無市場／ISIN／名稱，不能冒稱獨立核實完整證券身分；使用目前選定的市場／代號，錯股、日期異常、重複或無效數字回應拒絕接受。

2026-10-04 實際查詢皆 HTTP 200：

| 股票 | 最新資料日 | PER | 來源 |
| --- | --- | --- | --- |
| 2330 上市 | 2026-10-02 | 28.98 | FinMind/TaiwanStockPER |
| 6488 上櫃 | 2026-10-02 | 57.77 | FinMind/TaiwanStockPER |

實作為 `Data/FinMindValuationProvider.cs`、`MainWindow.Valuation.cs`。合約及介面測試涵蓋空值、日期、錯股、取消、逾時、大小限制、晚到回應及不阻擋分析，已納入 Release **529／529** HistoLens 測試。實測證據位於 `artifacts/histolens-source-verification/finmind-only-20261004/`。

使用條件依 [FinMind 官網條款](https://finmindtrade.com/analysis/#/Sponsor/terms_of_use) 與方案，不套用舊官方資源的政府開放授權。更多限制見 [FinMind 接入](HISTOLENS-FINMIND-IMPLEMENTATION.md)。
