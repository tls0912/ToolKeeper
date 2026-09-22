# CabiDock 桌面整合原型狀態

查閱與實作日期：2026-09-23。

目前為分類與群組操作的開發預覽，**尚未通過 002 的桌面整合驗收**。一般啟動保留 Windows 桌面圖示，群組只在明確標示的預覽視窗內操作。預覽畫面不能作為「原桌面不重複顯示」或「顯示桌面正常」的驗收證據。

## 已實作的邊界

- `DesktopProbe.Capture()` 透過 `IShellWindows.FindWindowSW` → `IServiceProvider.QueryService` → `IShellBrowser.QueryActiveShellView` 取得桌面 `IFolderView`。只讀取 Shell 視窗、項目類型、位置、顯示模式與自動排列狀態；不改選取、位置、顯示、視窗樣式或檔案。
- 系統項目數以「非檔案系統 Shell 項目」計算，可能包含第三方命名空間，不只 Windows 內建圖示。診斷資料不可取代檔案掃描及分類清單。
- `DesktopWindowHost.TryAttach()` 為**未啟用的實驗 API**。僅附掛程式自己的隱藏 HWND，拒絕置頂、透明、有擁有者或 DPI 模式不同的視窗；失敗時保持隱藏，沒有一般視窗或置頂視窗的替代流程。
- 附掛前後檢查 Shell HWND、程序識別及父子關係；`IsAlive` 與 `TrySetBounds` 會檢查 Shell 是否仍有效，`Dispose` 將自己的視窗卸離並保持隱藏。呼叫者仍需安排 Explorer 重啟偵測、重建群組與位置約束；這些生命週期尚未整合成產品功能。
- 未實作逐項圖示隱藏、離屏搬移、變更 Shell 排列旗標、隱藏整個原生圖示視窗或修改檔案屬性。未啟用接管，因此結束或崩潰時沒有需還原的桌面圖示變更。

## 為何尚未啟用桌面接管

Microsoft 指定 `IFolderView.SelectAndPositionItems` 為支援的桌面圖示定位 API；直接操作桌面的 ListView 不可靠。這個 API 提供**定位**，文件沒有承諾逐項隱藏。把檔案圖示放到畫面外，無法由文件推定它們在自動排列、對齊格線、Explorer 重啟或顯示配置改變後仍不出現。因此本原型沒有把離屏定位當成可交付的隱藏方案。[Microsoft 說明](https://devblogs.microsoft.com/oldnewthing/20211122-00/?p=105948)、[定位 API](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-selectandpositionitems)。

`SetParent` 是公開 Win32 API，但把應用程式 HWND 加進 Explorer 的桌面 Shell view 並不是文件保證的桌面擴充契約。跨程序 DPI 模式不同可能重設子視窗程序的 DPI；原型因此直接拒絕這種組合。即使附掛成功，也不能單憑 HWND 關係宣稱「顯示桌面」、焦點、虛擬桌面或 Explorer 重啟已驗證。[SetParent 文件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)。

後續若評估任何會改圖示狀態的方案，必須先證明可逐項保留系統圖示、原子保存可對應項目的原始狀態、正常退出及獨立崩潰恢復、處理刪除與重新命名、顯示拓撲變更及 Explorer 重啟。單靠程式退出回呼不足以處理強制終止。本次未加入一個無法保證恢復的 watchdog。

## 本次只讀診斷

執行程序回報 Windows NT 10.0.26200.0、x64、.NET 10.0.11；這是診斷執行環境資訊，並非 Windows 10 相容性實測。

執行命令：

```powershell
dotnet src/CabiDock/bin/Debug/net10.0-windows/CabiDock.dll --diagnose-desktop artifacts/cabidock-desktop-probe.json
```

命令正常結束，報告 `Available = false`、`ShellWindowHandle = 0x0`，原因為執行環境無可取得的 Windows 桌面 Shell。沒有進入 COM 項目列舉或附掛，也沒有修改原生桌面。

報告中的兩個項目數 0 是**無資料**，不是「真實桌面沒有項目」。本次不能證明 Explorer 的檔案／系統項目計數、原始位置、桌面父視窗或 UI 互動有效。

## 尚待互動環境驗證

| 項目 | 狀態 |
| --- | --- |
| Shell 檔案與系統項目讀取 | 本次環境未提供 Shell；待驗證 |
| 逐項原生圖示隱藏且保留系統圖示 | 尚未找到已驗證方案；未實作 |
| 桌面群組附掛、一般應用程式層級、顯示桌面 | 僅有未啟用 API；待驗證 |
| Explorer 重啟後重建群組 | 未完成產品整合 |
| 接管狀態的正常／崩潰恢復 | 接管尚未啟用；不能宣稱已通過 |
| 主螢幕、DPI、解析度切換 | 桌面宿主尚未驗證 |
| Windows 10 最低版本與處理器架構 | 未測；不能從本機編譯成功推定 |

其他主要依據：[桌面圖示讀取與保存範例](https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933)、[GetShellWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getshellwindow)、[IFolderView.GetAutoArrange](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview-getautoarrange)。
