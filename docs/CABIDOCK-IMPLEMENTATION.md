# CabiDock 實作狀態

更新日期：2026-09-23。版本：0.1.0 開發預覽。

已建立可執行的分類與互動原型。正式桌面接管尚未啟用，**不是完成 V1 驗收的桌面整理器**。群組放在標有「群組操作預覽」的一般視窗內，保留原生桌面與系統圖示。

## 已實作

- 獨立 `src/CabiDock/CabiDock.csproj`，.NET 10、WPF，WinForms 僅用於系統匣；未增加第三方執行時套件或共用專案。
- 七個預設分類，由內嵌 JSON 提供副檔名。分類使用固定 ID，名稱可修改；資料夾及兜底分類具有獨立種類。
- 手動指定、檔名關鍵字順序、自訂副檔名、預設規則與兜底的分類優先順序。
- 分類衝突驗證，自訂分類可覆蓋預設副檔名；不同自訂分類衝突會顯示副檔名與既有分類名稱。
- 自動與手動分類皆保存。正常啟動沿用有效紀錄；修改規則重算自動分類，保留有效手動分類。刪除分類後重新判斷失效的紀錄。
- 解析 Windows 的使用者與公共桌面位置，僅掃描頂層；同名但不同路徑的項目分開處理。目前略過 Hidden/System 項目，尚未跟隨 Explorer 的「顯示隱藏項目」設定。
- `FileSystemWatcher` 觀察新增、移除與重新命名，另以定期掃描補足漏事件；移除事件立即清除紀錄。監看失效後重新建立，桌面路徑改變時重新解析。
- 可辨識的同一檔案重新命名保留分類。以檔案識別碼與磁碟識別資訊核對，無法取得時退回路徑；不追查程式關閉期間的移動。
- 完整掃描失敗時保留全部既有分類，不把掃描失敗當成空桌面。
- 群組收合預覽、點擊展開、離開延遲收合、內容捲動、雙擊開啟、右鍵手動分類、群組間拖曳、拖曳標題與調整展開大小。空群組隱藏但保留配置。
- 設定視窗管理分類與關鍵字，支援規則拖曳排序；視窗關閉縮至系統匣。系統匣只有「設定」與「結束程式」。同一資料目錄只啟動一個實例，再次啟動喚起設定。
- JSON 同目錄原子替換與健康備份。損壞主檔可讀備份；主檔與備份均無法讀取時停用保存並保留檔案。規則指紋可在設定已保存、分類狀態尚未寫入的中斷後重套自動分類。

## 建置與執行

在 Windows 安裝 .NET 10 SDK：

```powershell
dotnet restore src/CabiDock/CabiDock.csproj
dotnet build src/CabiDock/CabiDock.csproj
dotnet test tests/CabiDock.Tests/CabiDock.Tests.csproj
dotnet run --project src/CabiDock/CabiDock.csproj
```

主介面是設定視窗；按「開啟群組操作預覽」使用群組。手動分類只改 CabiDock 的紀錄，不移動、複製或重新命名檔案。雙擊項目會交由 Windows 預設程式開啟。

開發時可以指定獨立的測試目錄與狀態目錄，避免使用真實桌面：

```powershell
New-Item -ItemType Directory -Force artifacts/cabidock-demo/desktop
Set-Content artifacts/cabidock-demo/desktop/Welcome.md '# CabiDock'
dotnet run --project src/CabiDock/CabiDock.csproj -- --desktop-directory artifacts/cabidock-demo/desktop --data-directory artifacts/cabidock-demo/profile
```

## 本機資料

正常執行時儲存在 `%LOCALAPPDATA%\ToolKeeper\CabiDock`：

| 檔案 | 用途 |
| --- | --- |
| `configuration.json` | 分類 ID、名稱、種類、副檔名與有序關鍵字規則 |
| `state.json` | 項目完整路徑、可用識別資訊、分類 ID、來源、群組配置、設定指紋 |
| 同名 `.bak` | 上一次健康版本 |

設定使用陣列與固定分類 ID，避免改名使手動分類失效；此格式是產品文件所允許的內部設計。首次未保存設定時，從 `Defaults/categories.json` 載入七個預設分類。

可恢復的寫入失敗保留記憶體變更並重試；未恢復前關閉仍可能失去該次變更，介面會顯示未儲存狀態。全部檔案都無法讀取時，需修復資料檔後重啟。

## 驗證與限制

本機建置環境是 Windows 11 x64（10.0.26200）、.NET SDK 10.0.401；`net10.0-windows` 未因 UI 加上 17763 以上的 Windows SDK 編譯門檻，但不代表已驗證 Windows 10 或更舊版本。

自動測試涵蓋分類優先順序、規則衝突與變更、保存與重啟、重新命名、移出返回、完整與失敗掃描，以及備份／損壞／寫入失敗。介面測試使用 STA 及離屏 WPF 渲染，不操作使用者桌面。

2026-09-23 最終驗證：`dotnet test ToolKeeper.sln --no-restore -c Release` 成功，CabiDock **49／49**、MarkPad **64／64** 通過，編譯無警告。介面渲染檢查已確認預設／較小設定視窗與展開群組，測試輸出位於 `artifacts/cabidock-ui/`。未以離屏測試取代真實桌面操作驗收。

以下仍需完成，不能視為已驗收：

- 逐項隱藏已管理的原生檔案圖示，並保留系統圖示的原生操作。
- 群組附掛桌面後的層級、「顯示桌面」、Explorer 重啟與異常結束恢復。
- 真實多螢幕、主螢幕切換與混合 DPI。目前預覽內配置會限制在預覽範圍，尚不等同正式主螢幕桌面配置。
- 真實滑鼠拖曳、右鍵選單、系統匣、登出／關機與長時間監看的人工驗收。
- Windows 10 各版本與處理器架構、重新導向／網路桌面、極大量項目的效能。

桌面部分的具體證據與下一步門檻見 [桌面原型驗證](CABIDOCK-DESKTOP.md)。
