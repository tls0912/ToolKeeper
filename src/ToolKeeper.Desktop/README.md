# ToolKeeper.Desktop

供 ToolKeeper 宿主使用的 WPF 類別庫，保留 `CabiDock` 命名空間、原資料目錄與 JSON 格式。桌面模組不建立系統匣、不呼叫 `Application.Shutdown`，也不設定 `Application.MainWindow`。

宿主在 UI dispatcher 上建立 `DesktopModule(dispatcher, dataDirectory, roots)`，先以 `SetEnabled` 套用啟停偏好，再呼叫 `Start()`。不提供 `roots` 時啟用真實桌面整合；指定自訂目錄時只提供掃描／預覽。`ShowSettings()` 開啟獨立設定視窗，關閉設定僅隱藏。`PrepareExit()` 先停止新工作、監看與計時器，再還原桌面並保存；`Dispose()` 關閉模組視窗。

`SetTools(tools, activateTool)` 注入由宿主管理的工具入口與啟動 URI。點擊時將 `DesktopTool.ActivationUri` 原值交回宿主路由；桌面模組不解析產品或啟動程序。此專屬群組固定展開，點標題、滑鼠離開、展開其他分類時都不收合，仍可拖曳、調整大小並保存配置。一般分類維持原有展開與延遲收合行為。工具資料不進入分類清單、fingerprint、Shell 選單或拖放分類。

宿主的第一個啟動分支必須呼叫 `DesktopRecoveryGuard.TryRun(args)`，因為復原程序會使用目前宿主執行檔啟動。由 `dotnet` 啟動時使用 entry assembly，不執行本類別庫。ToolKeeper 使用 `SingleInstance` 避免同一資料目錄同時寫入；CabiDock 沒有獨立執行入口，測試與診斷均使用 ToolKeeper 宿主。
