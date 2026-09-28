# CabiDock 桌面接管

更新日期：2026-09-26。一般啟動在完成桌面掃描後自動嘗試接管。已在本機 Windows 11 實測接管、暫停恢復、檔案變更時沿用群組，以及原生不透明度套用，仍屬實驗實作，各 Windows 版本與完整互動尚待驗收。

## 使用方式

- 啟動 `dotnet run --project src/CabiDock/CabiDock.csproj`，設定視窗顯示目前接管狀態與失敗原因。
- 「暫停桌面接管」會撤下群組並恢復原生圖示；「啟用桌面接管」重新嘗試。此暫停只影響本次執行。
- 開啟群組操作預覽會先暫停接管。`--desktop-directory` 自訂掃描目錄僅提供預覽，不操作真實桌面。
- 最小化或關閉設定視窗會縮至系統匣，不佔工作列；從系統匣「設定」恢復視窗。「結束程式」或登出會恢復桌面。
- 展開後滑鼠離開 3 秒才收合；移回取消。選單、改名與拖曳期間保持展開，結束後重新計時。
- 檔案右鍵使用 Windows Shell 傳統完整選單（Windows 11「顯示其他選項」類型），附加「CabiDock 分類」子選單；選擇重新命名或按 F2 可修改檔名。改名由 Shell 執行，分類記錄交由監看流程更新。
- 設定中的「分類區不透明度」提供 30–100%，儲存後套用所有群組，預設 100%。只改不透明度不重新分類。

## 實作與恢復

`DesktopProbe` 以 Shell COM 讀取完整項目與桌面 view；`DesktopIconClipper` 以 MSAA 讀取原生 `SysListView32` 的圖示名稱與螢幕範圍。`DesktopClipPlan` 核對名稱數量與已分類完整路徑，拒絕同名管理／保留歧義、遺失項目、無效座標及重疊幾何。未管理與非檔案系統圖示保留。

`DesktopAccessibilityReader` 接受整數 child ID 與獨立 `IAccessible` 圖示，僅將 `ROLE_SYSTEM_LISTITEM` 計入圖示。Windows 額外列出的隱藏標題控制項，必須核對為同一 ListView 的原生隱藏 `SysHeader32` 才略過；未知或可見控制項仍拒絕接管。

`DesktopGroupPresenter` 將群組附掛為 Explorer view 的子視窗，先全部附掛成功才顯示，不使用置頂視窗。群組配置限制在主螢幕工作區，避開保留的原生圖示。跨程序 DPI awareness 不一致會拒絕附掛。`DesktopTakeoverService` 每秒重新核對 Shell、群組與恢復程序；檔案事件以 120ms 合併掃描，WinEvent 取得新幾何映射後在既有 session 更新，不再每次拆除接管並等待 5 秒。分類 membership 使用完整掃描副本，避免 watcher 先更新路徑、掃描稍後完成時誤刪群組。原生選單操作中的群組延後替換內容與撤下視窗，裁切只包含實際已呈現的檔案。

若 Shell 與 MSAA 幾何暫時不一致，先恢復原生 region、暫藏並保留群組 HWND 與展開狀態，以 100ms 間隔重試；持續 2 秒仍失敗或 Explorer／恢復程序失效才完整停止。這保留安全恢復，並不承諾所有桌面異常均無閃動。首次變更前仍會在恢復程序及群組就緒後重新核對圖示快照。

幾何核對成功後，每次更新都以原生 `SetWindowPos` 恢復群組的顯示旗標與桌面內子視窗層級，展開群組排在前方。暫藏使用原生 API，不能只依賴 WPF 的顯示快取或「曾顯示」紀錄，否則檔案異動後可能一直隱藏到手動重新接管。原生測試 HWND 已驗證隱藏後恢復顯示及子視窗層級，Presenter 測試驗證保留群組與展開尺寸、核對完成前不提前顯示。

分類區透明度透過 `SetLayeredWindowAttributes` 設定自有子視窗的整體 alpha；窄範圍的 `HwndSource` hook 防止 WPF 在 extended-style 更新時移除必要的 `WS_EX_LAYERED`，回到 100% 或結束附掛時移除 hook 與分層樣式。不設定滑鼠穿透。[Microsoft 原生透明度文件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes)、[WPF HwndTarget 原始碼](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndTarget.cs)。

僅在受管理圖示範圍裁切原生 ListView 的 region，不裁切群組所在的父 view；不移動檔案、不設 Hidden 屬性、不搬移原生圖示、不改自動排列。原生有自訂 region 時，以其原始區域為基礎計算。

`DesktopRecoveryGuard` 在變更前啟動同一程式的私有恢復模式，以當前使用者限定的具名管線交握。獨立程序先持有恢復互斥鎖，主程序才擷取原始 region，避免崩潰後立即重啟將舊裁切誤認為原始顯示。雙方核對管線程序身分、目標 HWND 類別、PID 與程序起始時間。

正常退出由主程序還原；主程序死亡或管線中斷由獨立程序還原，暫時失敗則重試。Explorer HWND 已失效時不把舊 region 套到不同程序。恢復程序死亡但主程序仍存活時，接管檢查會撤回顯示並由主程序還原。若兩個程序同時被強制結束，可重新啟動 Explorer 恢復原生視窗；檔案位置與屬性始終不變。

## 已知限制

**區域裁切是顯示控制，不是從 Explorer 移除項目。** Explorer 的鍵盤導覽、Ctrl+A 等仍包含受管理檔案；在原生桌面執行全選或檔案操作前，先暫停接管以看見完整選取。這項語意限制仍需後續解決，不能宣稱已完成產品規格的完整逐項隱藏。

Shell 項目與 MSAA 幾何讀取並非原子快照。程式會核對數量／名稱、監看變更並於失敗時恢復，但桌面變動期間可能短暫回到原生顯示。第三方 Shell、無法讀取的圖示、DPI 模式不一致、使用者已隱藏桌面圖示時，不強行接管。

`SetParent` 與 `SetWindowRgn` 是公開 API，但將 WPF 子視窗附掛 Explorer 不是 Microsoft 保證的桌面擴充契約；Win+D、虛擬桌面、Explorer 重啟、真實多螢幕與混合 DPI 仍需互動驗證。[SetParent 文件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)、[SetWindowRgn 文件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn)、[ListView MSAA 文件](https://learn.microsoft.com/en-us/windows/win32/winauto/list-view-control)。

## 驗證

2026-09-26：完整 Release 測試 **126／126** 通過。新增原生 Shell 選單命令、Unicode `.txt/.lnk/.url` Shell 改名、實際 Dispatcher 3 秒收合與重入、忙碌群組／分類快照競態，以及真實 apphost 子視窗 alpha 來回切換測試。原生選單在獨立測試預覽中已實際開啟，確認包含本機 Shell 擴充項目。

實際桌面以唯一名稱的暫存檔驗證新增、改名、移除，三個既有群組 HWND 在變更前後一致；測試檔已移除。最終核對 22 個檔案圖示全部裁切、3 個系統圖示保留。70% 不透明度儲存後三個實際群組回讀 alpha 178／255，恢復 100% 後回讀完全不透明。報告位於 `artifacts/cabidock-interaction-20260926/`。新增瞬間的唯讀診斷曾因 Shell 與檔案掃描不同步無法取得完整統計，沒有將這個瞬間當成空桌面或成功驗證。

自有隱藏子視窗的透明度診斷：`CabiDock.exe --diagnose-group-opacity artifacts/cabidock-native-opacity.json`。此模式不附掛 Explorer，也不操作真實桌面檔案。

自動測試使用未顯示的測試 HWND 與 STA WPF，包含區域計畫、群組附掛失敗回復、配置與 DPI 換算、父視窗失效、原始 region 還原、管線中斷後獨立恢復，以及恢復程序死亡後主程序還原；不變更真實桌面。

只讀診斷：

```powershell
& ./src/CabiDock/bin/Debug/net10.0-windows/CabiDock.exe --diagnose-desktop artifacts/cabidock-desktop-probe.json
```

診斷須使用 `.exe`，使 DPI manifest 與一般啟動一致；透過 `dotnet CabiDock.dll` 啟動的宿主可能使用不同 DPI 模式。報告包含 Shell 項目、MSAA 幾何是否可用、DPI 模式比較，以及 `VisibilityAudit` 的實際原生裁切／保留圖示與可見附掛群組數。

一般沙箱環境回報 `Available = false`、`ShellWindowHandle = 0x0`；這表示無資料，不能當作空桌面。2026-09-24 在實際桌面工作階段執行唯讀診斷，重現 `IOleWindow.GetWindow` 的 `0x80004002`：桌面物件可取得 `IShellView`，卻不支援直接查詢其 `IOleWindow` 基底介面。改用 `IUnknown_GetWindow` 後，診斷成功讀取 19 個檔案系統項目與 3 個系統項目。Microsoft 的這個函式明確處理此相容性問題。[IUnknown_GetWindow 文件](https://learn.microsoft.com/en-us/windows/win32/api/shlwapi/nf-shlwapi-iunknown_getwindow)。

其後直接操作實際設定視窗，重現持續顯示「桌面項目正在變更」：MSAA 比 Shell 多列了一個隱藏 `SysHeader32`，造成原始數量核對永遠失敗。修正後，實際接管核對 20 個受管理圖示全部裁切、3 個系統圖示範圍完整保留、2 個可見群組附掛 Explorer。按下暫停後，核對原生 ListView 無自訂 region、0 個圖示被裁切、0 個附掛群組，3 個系統圖示仍完整保留。最後重新啟動 Release 版，桌面已變為 21 個檔案項目，全部成功接管至 3 個群組，3 個系統圖示仍完整保留。相關唯讀報告位於 `artifacts/cabidock-audit-paused.json` 與 `artifacts/cabidock-audit-active.json`。

診斷失敗時會記錄 `FailureStage`、`ErrorCode`，設定視窗也會顯示失敗的步驟。以下操作仍待實機驗收：

1. 群組內檔案、資源回收筒與未管理圖示的雙擊／右鍵操作。
2. Win+D、一般應用程式覆蓋、群組拖曳／收合與系統匣操作。
3. 接管中重啟 Explorer、強制結束主程序、登出與重新啟動。
4. 主螢幕切換、解析度與混合 DPI 調整後的位置與可操作性。
5. Windows 10 各版本、重新導向或網路桌面與大量項目效能。
