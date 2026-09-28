# CabiDock

002 桌面能力已整併至 [ToolKeeper.Desktop](../ToolKeeper.Desktop/README.md)，由 `ToolKeeper.exe` 管理，沒有獨立的 CabiDock 執行檔或啟動入口。只有獨立上架的產品保留自己的執行檔。

桌面設定、分類清單與群組位置沿用 `%LocalAppData%\ToolKeeper\CabiDock`。測試與診斷也使用 ToolKeeper 宿主；指定 `--desktop-directory` 與 `--data-directory` 可使用自訂目錄驗證，不接管真實桌面。

- [產品規格](../../docs/products/002_CabiDock.md)
- [桌面模組](../ToolKeeper.Desktop/README.md)
- [已實作範圍、啟動與測試](../../docs/CABIDOCK-IMPLEMENTATION.md)
