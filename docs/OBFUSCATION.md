# 全系列 Obfuscar 發佈

所有 `src` 下的產品專案在 **Release 的 `dotnet publish`** 自動執行 Obfuscar **2.2.50**。正常 `dotnet build`、`dotnet test` 與 Debug 發佈維持原始組件，方便除錯。既有發佈腳本不需增加開關。

## 涵蓋範圍

| 工具／模組 | 混淆組件 | 發佈入口 |
| --- | --- | --- |
| 工具番、HashChecker、ImageToIco | `ToolKeeper.dll` | `src/ToolKeeper/ToolKeeper.csproj` |
| CabiDock 桌面整理 | `ToolKeeper.Desktop.dll` | 隨工具番發佈 |
| HistoLens | `HistoLens.dll` | 隨工具番、HistoLens preview 發佈 |
| 共用 UI | `ToolKeeper.UI.dll` | 隨每一個產品發佈 |
| 汗青 MarkPad | `Hanqing.dll` | portable、MSIX 或專案 publish |
| ConvAnvil | `ConvAnvil.dll` | `src/ConvAnvil/ConvAnvil.csproj` |
| TransLamp | `TransLamp.dll` | 離線包或專案 publish |

TransLamp 的 Python worker、Python runtime、語言模型，以及汗青的 JavaScript／CSS／Markdown 資源不屬於 .NET IL，不由 Obfuscar 混淆。第三方 DLL 與 .NET runtime 保持原樣。

## 使用方式

從專案根目錄執行，例如：

```powershell
dotnet publish src/ToolKeeper/ToolKeeper.csproj -c Release -r win-x64 --self-contained true -o artifacts/release/ToolKeeper
dotnet publish src/ConvAnvil/ConvAnvil.csproj -c Release -r win-x64 --self-contained true -o artifacts/release/ConvAnvil
./scripts/Publish-MarkPad.ps1 -Runtime win-x64 -Zip
./scripts/Publish-TransLamp.ps1
```

TransLamp 完整離線包仍須先依既有流程準備 runtime 與語言包。`Publish-MarkPadMsix.ps1`、`Publish-HistoLensPreview.ps1` 也經過同一個 MSBuild 發佈步驟；TransLamp installer 使用已發佈的離線包。

只在診斷時明確停用：

```powershell
dotnet publish src/ConvAnvil/ConvAnvil.csproj -c Release -p:ObfuscateOnPublish=false -o artifacts/diagnostic/ConvAnvil
```

`--no-build`／`--no-restore` 仍會混淆，但必須已有同一組目標框架／RID 的建置及 restore。Obfuscar 透過固定版本 `PackageDownload` 還原，沒有全域安裝需求，也不列為產品 runtime 套件。

## 相容性與處理順序

1. `Directory.Build.targets` 僅對產品來源匯入 `build/Obfuscar.targets`。
2. 在 `ComputeResolvedFilesToPublishList` 完成後，將本次發佈的自有 DLL 複製到獨立工作目錄，一起交給 Obfuscar；解析相依組件使用本次發佈與 SDK 的實際路徑。
3. 成功後，MSBuild 改用混淆組件作為發佈輸入，再進行複製或 single-file bundling。原始 `bin` DLL 與編譯器輸出不覆寫。
4. 任一混淆失敗、缺少組件、產物未變更或缺少 mapping，都中止 publish。自有 PDB／XML API 文件不納入發佈。

目前不支援與 `PublishTrimmed`、`PublishAot` 或 `PublishReadyToRun` 同時啟用；設定時會明確失敗。混淆是增加逆向分析成本，並非加密程式或完全防止反編譯。

保留 public API、屬性／事件名稱、列舉及建構子參數，啟用 XAML 分析，以維持跨組件呼叫、WPF Binding、JSON 設定與內部 positional record DTO。私有方法、欄位與適用的型別名稱仍會改寫，IL 字串常數也會隱藏。汗青組件名稱為 `Hanqing`，既有 `MarkPad.Resources.*` 資源名稱保持不變。

共同規則在 `build/Obfuscar.xml`；`scripts/Invoke-Obfuscar.ps1` 為每個 Module 加入 `SkipEnums` 與 `.ctor` 排除。新增產品時，同時將組件名稱加入 XML 的 Module 清單與 targets 的 `_ObfuscarAssemblyNames`，避免漏掉保護。

## 名稱對照表與驗證

每次執行的私人證據放在產品的 `obj/<Configuration>/<TargetFramework>/[RID/]obfuscar/<run-id>/`：

- `Mapping.xml`：原始名稱與混淆名稱對照。
- `verification.json`：每個 DLL 的混淆前後 SHA-256。
- `obfuscar.xml`：本次使用的完整絕對路徑設定。
- `input`、`output`：本次改寫前後的組件。

這些檔案由既有 `obj/` Git 忽略規則排除，且不複製到產品發佈資料夾。若需留存以便追查正式版本，請保存在私人建置紀錄中。

一般單元測試檢查的是原始組件；發佈後另用 `tests/Obfuscation.Smoke` 載入實際產物，檢查 WPF、JSON、資源與核心功能。[Smoke 操作指令](../tests/Obfuscation.Smoke/README.md)。

## 2026-10-04 驗證紀錄

- 全方案 Release 測試：1,581 通過、0 失敗、7 略過；略過項目為需另行啟用的 TransLamp 真實模型／網路整合測試。
- ToolKeeper、MarkPad、ConvAnvil、TransLamp 四個 framework-dependent Release 發佈成功，合計涵蓋 7 個自有組件。
- 發佈產物 smoke：工具番／雜湊／ICO 視窗與功能、CabiDock recovery JSON、HistoLens 合成研究、汗青 Markdown／設定／啟動請求、ConvAnvil 編碼、TransLamp 路由及 pack transaction JSON。
- 7 個組件的 identity、資源名稱與 77 個 P/Invoke entrypoint／旗標／參數數量保留。混淆後 ToolKeeper 的唯讀桌面 COM 診斷及原生視窗 opacity 診斷通過。
- 既有 `Publish-MarkPad.ps1` 成功建立 self-contained win-x64 portable；`Publish-MarkPadMsix.ps1` 以 Windows PowerShell 5.1 成功建立未簽署 MSIX，且通過既有封裝驗證器。PowerShell 7 執行既有圖示產生器時遇到 `System.Drawing` 編譯相依問題，本次未更動該工具。
- `Publish-TransLamp.ps1` 成功建立完整離線包，檢查 1,060 個 runtime 檔案、兩個語言包及兩個翻譯 smoke。發佈 DLL smoke 也在汗青 portable 與 TransLamp 離線包重跑成功，共驗證六個產品目錄。
- `--no-build` 發佈仍混淆；`ObfuscateOnPublish=false` 產物與原始組件一致；single-file 發佈成功；ReadyToRun 防誤用規則已驗證。

本機驗證產物與報告位於 `artifacts/obfuscar-validation/`。這些檢查不等同 Microsoft Store 授權、乾淨電腦或全部 GUI 互動的驗收；smoke 不啟動 WebView2 或 Python，完整離線包的 Python worker 翻譯由既有發佈驗證器另外檢查。

## 參考來源

- [Obfuscar 官方設定文件](https://docs.lextudio.com/obfuscar/getting-started/configuration)。網站目前也含 v3 說明，本專案固定使用 2.2.50。
- [2.2.50 設定實作](https://github.com/obfuscar/obfuscar/blob/2.2.50/Obfuscar/Settings.cs)、[Module 與排除規則](https://github.com/obfuscar/obfuscar/blob/2.2.50/Obfuscar/AssemblyInfo.cs)。
