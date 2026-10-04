# 汗青 1.0.3.0 Microsoft Store 更新封裝

封裝日期：2026-10-04。產品編號：001。以目前工作樹的汗青及共用 UI 原始碼建置，包含尚未提交至 Git 的修改。

## 更新檔

- 上傳檔：[Hanqing_1.0.3.0_x64.msix](../artifacts/msix/Hanqing-1.0.3.0-x64-store/Hanqing_1.0.3.0_x64.msix)
- 架構／組態：x64／Release，self-contained .NET/WPF 10.0.12。
- 大小：83,454,481 bytes（約 79.6 MiB）。
- MSIX／EXE 檔案版本：1.0.3.0；產品顯示版本：1.0.3。
- Store 產品：`9NHF764PXW9C`。
- SHA256：`0B40447AC7B4513A562531BA19B9067FB269217D6CFBBFF04CAFCD4505892F69`。
- [SHA256 檔](../artifacts/msix/Hanqing-1.0.3.0-x64-store/Hanqing_1.0.3.0_x64.msix.sha256)與 [PACKAGE-INFO.txt](../artifacts/msix/Hanqing-1.0.3.0-x64-store/PACKAGE-INFO.txt)位於同一資料夾。

沿用既有 Microsoft Store 產品身分、發布者、Application Id、檔案關聯及 URI 協定。版本從本機上一包 1.0.2.0 遞增；本次未登入 Partner Center 查詢後台最高版本。舊 1.0.2.0 套件保留，SHA256 與既有紀錄相同。

## 本次內容

依目前產品規格與封入的原始碼，本次更新包含：

- 八種介面語言：英文、繁體中文、日文、簡體中文、西班牙文、阿拉伯文、法文、韓文；同步補齊 MSIX 語言宣告。
- 無文件時輪替顯示三篇內建閱讀文章，各提供八種語言，合計 24 篇內嵌資源。
- 可選擇關閉時保留已開啟檔案，並在下次啟動還原。
- 章節大綱層級同時套用到預覽與 PDF 書籤。
- 近期文件、工作列標題、字型選單及啟動初始化等既有介面修訂。

本次打包工作僅補齊封裝語言、更新封裝腳本的預設版本與交付紀錄；程式內容取自當前工作樹。

## 驗證結果

| 驗證 | 結果 |
| --- | --- |
| Release publish、MakePri、MakeAppx 完整語意驗證 | 通過，未使用 `/nv` |
| `Test-MarkPadMsix.ps1` 身分、啟動宣告、runtime、第三方授權及禁止檔案檢查 | 通過 |
| 與 1.0.2.0 的 Name、Publisher、架構、Application Id 比對及版本遞增 | 通過 |
| MSIX／組件／EXE 版本、SHA256 檔與套件實際雜湊 | 通過 |
| 套件內 Hanqing.dll 的正式授權設定 | `StoreLicenseRequired=true`，StoreId／PFN 與本機正式識別一致 |
| 八語套件宣告、24 篇文章及五份新增語系字典 | 通過 |
| 套件內 Hanqing.dll／ToolKeeper.UI.dll 與 Layout 檔案雜湊 | 一致 |
| 汗青既有 .NET 測試 | 249 通過、0 失敗、0 略過 |
| 共用 UI 既有 .NET 測試 | 91 通過、0 失敗、0 略過 |
| 預覽 JavaScript 既有測試 | 15 通過、0 失敗、0 略過 |

測試使用隔離的建置輸出及使用者狀態目錄，三個測試命令均以 exit code 0 完成。證據：

- [封裝日誌](../artifacts/msix-validation/Hanqing-1.0.3.0/package-build.log)
- [套件核對結果](../artifacts/msix-validation/Hanqing-1.0.3.0/package-verification.json)
- [回歸測試摘要與命令](../artifacts/msix-validation/Hanqing-1.0.3.0/regression/README.md)

## 上傳及驗收範圍

將上述 `.msix` 上傳至原 Partner Center 產品的新提交。檔案刻意不簽章，由 Microsoft Store 通過認證後簽章；本次未安裝、上傳或提交。

WACK、實際安裝／更新／移除、乾淨 Windows 環境及真實 Store 正式／試用授權尚未驗收。套件身分比對與程式回歸測試不取代這些檢查。WebView2 Evergreen Runtime 沿用既有部署需求，未包入 MSIX。

## 重建命令

在專案根目錄執行，輸出目錄須為新的或空的資料夾：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-MarkPadMsix.ps1 -IdentityFile .\packaging\MarkPad\StoreIdentity.json -PackageVersion 1.0.3.0
```

若後續已提交此版本，再次更新應使用更高的新版本號。重建套件的 SHA256 以當次實際輸出為準。
