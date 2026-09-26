# CabiDock

002 的獨立 .NET 10／WPF 開發原型。

```powershell
dotnet run --project src/CabiDock/CabiDock.csproj
```

啟動後顯示設定視窗，成功掃描後自動嘗試桌面接管：分類群組附掛桌面，受管理的原生檔案圖示以區域裁切隱去，系統圖示保留。「暫停桌面接管」或結束程式會恢復原生顯示；開啟操作預覽會先暫停接管。指定 `--desktop-directory` 時只提供預覽，不變更真實桌面。關閉設定視窗縮至系統匣，從系統匣「結束程式」退出。

檔案變更會更新既有群組；滑鼠離開 3 秒後收合。項目右鍵使用 Windows 傳統 Shell 選單，支援重新命名及 F2；「CabiDock 分類」子選單可手動指定。設定提供所有分類區的 30–100% 不透明度，按「儲存並套用」生效。

目前屬桌面接管實驗實作，已在本機 Windows 11 實測接管與暫停恢復，Win+D、混合 DPI 等仍待驗收。裁切只改顯示，Explorer 的鍵盤選取仍包含受管理檔案；使用桌面全選等操作前可先暫停接管。詳見下列桌面整合說明。

- [產品規格](../../docs/products/002_CabiDock.md)
- [已實作範圍、啟動與測試](../../docs/CABIDOCK-IMPLEMENTATION.md)
- [桌面整合原型與待驗證門檻](../../docs/CABIDOCK-DESKTOP.md)
