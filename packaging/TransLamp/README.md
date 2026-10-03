# TransLamp — Offline Language Translator

工具番 007 的離線應急翻譯開發版。目標是協助理解大意，譯文可能有語意、術語與數字錯誤，請對照原文。

## 使用方式

1. 將整個 Offline Kit 資料夾複製到 Windows x64 電腦的本機磁碟。
2. 執行 `TransLamp.exe`。首次啟動會驗證並準備隨附的中英語言包。
3. 選擇 English → 中文或中文 → English，貼上文字，按「翻譯」或 `Ctrl+Enter`。
4. 完成後可複製譯文；長文翻譯時可按「取消」或 `Esc`。
5. 若出現數字、位址或識別字差異提示，請逐項對照原文。沒有提示也不代表語意正確，模型仍可能誤譯術語、遺漏內容或插入無關文字。

翻譯在本機 CPU 上執行，無須帳號、雲端 API、系統 Python 或另行安裝 .NET。若目標電腦尚無 Microsoft Visual C++ x64 執行元件，需先執行 `Prerequisites/VC_redist.x64.exe`；這是 Microsoft 官方安裝程式，TransLamp 不會自行安裝。此開發版每次翻譯重新載入模型，首次與後續執行都會有載入時間。

介面使用工具番共用竹紋視窗，可切換風格及中／英／日介面；日文介面不代表已提供日文翻譯模型。中文輸出目前依模型以簡體為主。

## 語言包

語言包位於 `LanguagePacks/*.tlpack`，可單獨透過 USB 搬移，在「語言包管理」匯入。必須使用相容的 TransLamp 格式；不直接載入 `.argosmodel`。

匯入時會檢查中英方向、版本、固定 runtime、每個檔案的大小及 SHA-256，並要求 LICENSE／NOTICE；在取代舊版本前還會實際載入 tokenizer 與模型。缺少 runtime 或驗證失敗時會保留原有模型。載入成功不代表翻譯品質已驗收。SHA-256 用於完整性驗證，並非發行者數位簽章。

已安裝資料預設存放於 `%LOCALAPPDATA%\ToolKeeper\TransLamp\LanguagePacks`；介面偏好位於同層的 `ui.json`。原文與譯文不寫入歷史記錄。移除語言包後不會因重啟而自动再安裝，仍可手動重新匯入。

模型與 runtime 的第三方授權保留在語言包及 Runtime 中，請與程式一併保留。此包尚未完成正式 Store／網站發布與簽章、舊電腦效能認證。

## 開發與離線包重建

在有網路的開發電腦：

```powershell
./scripts/Prepare-TransLampResources.ps1
./scripts/Publish-TransLamp.ps1
```

發布預設輸出至附產品版本號的 `artifacts/TransLamp-OfflineKit-<版本>`。腳本驗證資源清單後，建立新的暫存輸出並執行真實中英雙向檢查；成功才替換先前由同一腳本產生的目錄。不要指定任意已有個人檔案的資料夾作為輸出。

公開資源下載僅發生於準備步驟，翻譯介面不呼叫線上翻譯服務。已準備好資源後，整包可搬至完全離線的目標 PC。

本機驗收可使用 `TransLamp.exe --data-dir <資料目錄> --runtime-dir <Runtime目錄>`。產品接受 `translamp://open`，此開發版不自行寫入 Windows Protocol 註冊；工具番可偵測同 checkout 的完整開發建置或相鄰完整程式目錄。
