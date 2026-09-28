# src

每個產品資料夾代表一個可獨立建置、獨立上架的產品；`ToolKeeper.UI/` 提供本體、002 與 003 共用的 WPF 主視窗及右上「風格、語言、關於」，汗青則在自訂視窗中使用同一組基礎元件。

目前：

- `ToolKeeper/` — 免費本體（Hash Checker、Image → ICO；目錄與 Launcher 合併版面預覽，操作暫緩）
- `MarkPad/` — 001 汗青（Microsoft Store：汗青 - Markdown Writer；執行檔 `Hanqing.exe`，保留原專案資料夾名稱）
- `CabiDock/` — 002 CabiDock（分類、群組操作與桌面接管實驗實作）
- `ConvAnvil/` — 003 ConvAnvil（文字編碼診斷、轉檔預覽與 Text ↔ Bytes 轉換）
- `ToolKeeper.UI/` — 共用 `AppWindow`，以及可獨立引用的語言、介面風格、字型與關於內容；詳見[接入說明](ToolKeeper.UI/README.md)

新增產品時使用：

```text
src/<ProductName>/
```

不要因為可能共用而預先建立 Shared / Common / Core。2026-09-28 依使用者要求，將 ToolKeeper 本體、CabiDock 與 ConvAnvil 的共同主視窗外觀抽成 `ToolKeeper.UI`；此專案只負責介面，不共用分類、轉檔、雜湊等業務邏輯。語言、風格與關於內容先由汗青接入，再於共用標題行右側提供三個入口；三個共用視窗的功能區同步切換，既有輸入與結果保留。

主視窗的 `MainName` 與 `SubName` 組成 `MainName - SubName` 標題列；內容區另提供 30 DIP 主標題與 14 DIP 產品簡介，簡介不與副標題綁定。完整範圍見 [架構說明](../docs/ARCHITECTURE.md#2-依實際需求抽取共用介面)。
