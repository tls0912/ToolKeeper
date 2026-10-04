# TransLamp 離線安裝檔

版本：0.1.6（2026-10-04）。一般 Windows EXE 安裝程式，不透過 Microsoft Store。

## 安裝

將 `TransLamp-Setup-0.1.6-win-x64.exe` 複製到 Windows 10／11 x64 電腦後執行。程式、.NET、翻譯引擎、預載中英雙向語言包與 Microsoft Visual C++ x64 執行元件均已包含，安裝不需網路。

預設安裝到目前使用者的 `%LOCALAPPDATA%\Programs\ToolKeeper\TransLamp`，建立開始功能表入口，可選擇桌面捷徑。若電腦缺少所需的 Visual C++ 執行元件，安裝器會從包內啟動 Microsoft 安裝程式並要求管理員授權；取消或安裝失敗時停止。安裝器不會自行重新啟動電腦。

安裝後直接開啟 TransLamp，預載中英雙向可離線翻譯。其他語言在「資料管理」按需下載，或在翻譯頁同意缺包下載提示，之後同樣可以離線使用。沒有直譯時會自動經英文中轉；缺少的方向可一次確認、依序下載。

這是未簽章的開發版安裝檔；Windows 可能顯示未知發行者或 SmartScreen 提示。旁附 `.sha256` 供完整性核對，不等同發行者簽章。

## 解除安裝與資料

0.1.6 建置已檢查 Offline Kit 清單與所有檔案雜湊，並通過包內引擎的中英雙向 CPU 翻譯；一般程式測試及真實中文↔法文的英文中轉驗證亦通過。新版安裝檔為 276,879,051 bytes，SHA-256 為 `df50ac3ffb49135514bad85045268c6f0a61d7a07a66b81fc0c72dfd6dc067af`。本次實際安裝／移除回歸在前置檢查發現既有的開始功能表 TransLamp 捷徑，因此依保護規則拒絕執行，未覆寫捷徑或改動既有安裝；建置 JSON 的 `installUninstallTestPassed` 保持 `null`。下方的完整安裝驗收紀錄屬於 0.1.5。

可從 Windows「已安裝的應用程式」移除 TransLamp，或執行安裝目錄內的 `Uninstall.exe`。解除安裝只刪除隨包安裝的程式檔與專屬捷徑，保留使用者額外放入的檔案。

已下載／匯入的語言包及介面偏好保留在 `%LOCALAPPDATA%\ToolKeeper\TransLamp`；升級安裝也不清除此資料。Microsoft Visual C++ 是共用元件，不隨 TransLamp 解除安裝。

## 重建

先準備或更新完整 Offline Kit：

```powershell
./scripts/Publish-TransLamp.ps1
```

編譯工具採用 [NSIS 3.13 官方可攜式 ZIP](https://sourceforge.net/projects/nsis/files/NSIS%203/3.13/nsis-3.13.zip/download)，解壓至 `artifacts/installer-tools/nsis-3.13`。ZIP 的 SHA-256 為 `ba63dffc4410ee89193e1cb5a41989991bd77c61068da17e3156d136b7b0b3d8`，已與 [NSIS 官方釋出資料](https://nsis-dev.github.io/release-data/versions.json) 核對。

```powershell
./scripts/Publish-TransLampInstaller.ps1
# 編譯器位於其他位置時：
./scripts/Publish-TransLampInstaller.ps1 -NsisCompiler 'C:\Tools\nsis-3.13\makensis.exe'
```

預設輸出於 `artifacts/installers`，包含單一 EXE、SHA-256 及建置紀錄 JSON。腳本檢查 Kit 版本、完整檔案清單與雜湊，再執行實際中英雙向引擎檢查，才編譯安裝檔。解除安裝清單由該 Kit 的精確檔名產生，未使用遞迴刪除安裝目錄。編譯期間的驗證證據保留在輸出目錄的 `.translamp-build-*`。

安裝腳本：`packaging/TransLamp/TransLamp.nsi`。安裝行為與安裝後驗收紀錄另外寫入同目錄的 `.installation-test.json`；建置通過不代表已在全新、缺少前置元件的電腦上完成驗收。

```powershell
./tests/packaging/TransLampInstaller.Tests.ps1
```

此驗證會實際在專案 `artifacts` 暫存目錄安裝、重新安裝、解除安裝，期間建立目前使用者的程式登錄與捷徑，再由解除安裝程式移除；若偵測到既有 TransLamp 安裝或同名捷徑則拒絕執行。驗證電腦須已有相容的 VC++ 元件，避免測試改動共用系統元件。

0.1.5 於 2026-10-04 本機驗收通過：拒絕覆寫未管理的非空資料夾、首次安裝、開始功能表捷徑、1,471 個發佈清單檔案的 SHA-256、安裝後真實中英雙向翻譯、重複安裝及解除安裝。額外新增檔案在重裝和移除後均保留，測試建立的登錄與捷徑已清除。尚未在缺少 VC++ 的全新電腦測試管理員授權／前置元件安裝分支。
