# MarkPad 開發與可攜式發佈

在 Windows 安裝 .NET 10 SDK。專案使用 WPF，編譯目標為 `net10.0-windows10.0.17763.0`。Preview 需要 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

從儲存庫根目錄執行：

```powershell
dotnet restore src/MarkPad/MarkPad.csproj
dotnet build src/MarkPad/MarkPad.csproj
dotnet test tests/MarkPad.Tests/MarkPad.Tests.csproj
dotnet run --project src/MarkPad/MarkPad.csproj -- "C:\Notes\README.md"
```

## Portable build

```powershell
.\scripts\Publish-MarkPad.ps1 -Runtime win-x64 -Zip
# ARM64：-Runtime win-arm64
```

輸出至 `artifacts/portable/MarkPad-<runtime>-<時間>/`，預設包含 .NET runtime，並附上註冊腳本與使用說明。整個資料夾需一起攜帶；ZIP 包含此完整資料夾。WebView2 Runtime 須另外安裝，發佈腳本不會下載或安裝它；Microsoft 的[部署說明](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)涵蓋線上與離線安裝方式。

可用 `-OutputDirectory` 指定空資料夾；腳本不會清除已有檔案。若使用 `-FrameworkDependent`，執行端須先安裝 .NET 10 Desktop Runtime。Portable 表示無安裝程式的資料夾發佈，設定、復原暫存與 log 仍儲存在 `%LOCALAPPDATA%\ToolKeeper\MarkPad`。

已完成相同 runtime 的 restore 時，可加 `-NoRestore`。發佈會附上 `THIRD-PARTY-NOTICES.md` 與 `licenses/`，包括實際 .NET runtime packs 的原始授權文件。套件版本與用途記錄在 [`packaging/THIRD-PARTY-NOTICES.md`](../packaging/THIRD-PARTY-NOTICES.md)；更新相依套件後，需一併核對授權原文並更新 `packaging/reviewed-packages.json`，否則發佈腳本會停止。

## 選用的 Windows 整合

將 portable 資料夾放到預計長期保留的位置後，再明確執行：

```powershell
.\scripts\Register-MarkPad.ps1 -ExecutablePath 'C:\Apps\MarkPad\MarkPad.exe' -WhatIf
.\scripts\Register-MarkPad.ps1 -ExecutablePath 'C:\Apps\MarkPad\MarkPad.exe'

# 搬移／刪除 portable 資料夾前移除註冊：
.\scripts\Unregister-MarkPad.ps1 -WhatIf
.\scripts\Unregister-MarkPad.ps1
```

註冊限目前使用者的 HKCU，加入 `.md`「開啟檔案」候選、應用程式 capabilities 與 `toolkeeper-markpad:` URI。以下為腳本管理的項目：

- `Software\Classes\ToolKeeper.MarkPad.Markdown`
- `Software\Classes\ToolKeeper.MarkPad.Protocol`
- `Software\Classes\toolkeeper-markpad`
- `Software\Classes\.md\OpenWithProgids` 下的 MarkPad 值
- `Software\ToolKeeper\MarkPad\Capabilities`
- `Software\RegisteredApplications` 下的 `MarkPad` 值

腳本不改 `.md` 預設值或 Windows `UserChoice`，也不需要系統管理員權限。預設程式由使用者在 Windows 設定選擇；[Microsoft 文件](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-register-a-file-type-for-a-new-application)說明 Open With 與應用程式檔案類型的註冊方式。解除註冊只移除帶有這組腳本 ownership marker 的項目，保留文件與個人設定；遇到其他安裝方式建立的同名項目時，註冊會停止，避免覆寫。

URI 目前只喚起 MarkPad，不從 URI 解析或執行任意命令。一般檔案參數透過單一實例協調送至既有程序。

## Store 發行尚待設定

目前輸出是開發用、未簽章的 portable build。Microsoft Store / MSIX 正式發行仍需真實 Partner Center package identity、publisher、視覺資產及簽章流程；尚未建立假 identity、憑證或聲稱可直接上架的套件。
