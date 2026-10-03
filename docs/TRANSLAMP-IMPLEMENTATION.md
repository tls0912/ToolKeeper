# TransLamp 007 實作與待辦紀錄

更新：2026-10-03。目前開發版：0.1.1。此文件保留原型能力及 0.1.0 的驗證紀錄；本輪修正與新版驗收集中於[優化實作紀錄](TRANSLAMP-OPTIMIZATION-IMPLEMENTATION.md)。

後續檢查：[全面優化檢查](TRANSLAMP-OPTIMIZATION-REVIEW.md)，包含 12 項已確認問題、隔離重現證據、效能量測與修正順序。

## 本次交付

已建立可實際執行的 Windows x64 離線中英翻譯產品，使用真實 OPUS-MT／Argos 模型及 CTranslate2 CPU 推論，不使用示範字典或線上翻譯服務。

- 獨立 WPF 程式：`src/TransLamp`。
- 共用介面：直接繼承 `ToolKeeper.UI.AppWindow`，竹紋頂列、Ink 預設風格、風格／語言／關於入口與工具番一致。
- 純文字雙欄、調整欄寬、中英方向交換、貼上、清除、翻譯、取消、複製。
- `Ctrl+Enter` 翻譯、`Esc` 取消；上限 20,000 個字元，超過時明確提示，不靜默截斷。
- 中／英／日介面與偏好保存；本版翻譯模型只提供中英雙向。
- 語言包版本、大小、來源與授權可見；支援 `.tlpack` 離線匯入、移除。
- 翻譯與模型驗證不阻塞 UI；忙碌時限制不相容操作，取消／關窗後不寫回過期結果。
- 工具番目錄加入 007，平台入口 `toolkeeper://run/007`，獨立產品入口 `translamp://open`。

## 執行與重建

Offline Kit 的預設輸出已改為 `artifacts/TransLamp-OfflineKit-<產品版本>`。0.1.0 原型保留於 `artifacts/TransLamp-OfflineKit-0.1.0`；0.1.1 產物與當次驗證見優化實作紀錄。完整資料夾需一起搬移，不能只複製 EXE。

原型 0.1.0 可執行檔：[`TransLamp.exe`](../artifacts/TransLamp-OfflineKit-0.1.0/TransLamp.exe)。原型壓縮包：[`TransLamp-OfflineKit-0.1.0.zip`](../artifacts/TransLamp-OfflineKit-0.1.0.zip)。原型解壓後約427 MiB，另需約162 MiB存放首次安裝的兩個語言包。

```powershell
# 開發環境需 .NET 10 SDK
dotnet build src/TransLamp/TransLamp.csproj

# 有網路的準備電腦下載固定版本資源、驗 SHA256、製作語言包
./scripts/Prepare-TransLampResources.ps1

# 已有完整下載快取時，可禁止下載並重建資源
./scripts/Prepare-TransLampResources.ps1 -Offline

# 發布附带 .NET 的 Windows x64 可攜包
./scripts/Publish-TransLamp.ps1
```

資源準備完成後，正常 `dotnet build` 會將 `artifacts/translamp` 中的 runtime／語言包複製到開發輸出，使工具番的既有 checkout 偵測可啟動具翻譯能力的 007。資源尚未準備時仍可編譯 UI，介面會明確提示缺少引擎，不提供假翻譯。大型二進位檔只位於已忽略的 `artifacts`／`bin`，不加入 Git。

目标 PC 不需要安裝 Python 或 .NET。CTranslate2 的 Microsoft Visual C++ x64 執行元件若未安裝，需使用離線包 `Prerequisites/VC_redist.x64.exe`；本次不自動修改系統或執行安裝器。

預設資料位置：`%LOCALAPPDATA%\ToolKeeper\TransLamp`。語言包放 `LanguagePacks`、UI 偏好放 `ui.json`。支援明確本機 CLI `--data-dir`、`--runtime-dir` 供驗收隔離，URI 不接受任意路徑參數。

## 翻譯與語言包設計

App 將單次請求透過 UTF-8 JSON stdin 傳給隨附的 `Runtime/python.exe -I translate.py`，接收逐行進度與最後的完整結果。每次只處理一個翻譯工作；取消會終止該推論程序及子程序。翻譯前重新驗證模型檔案雜湊，使用中的語言包會鎖定，其他實例不能同時移除或更新。

Runtime 使用固定 Windows x64 embedded Python、CTranslate2、SentencePiece 及必要相依。CPU int8 推論，不使用 GPU 或一般大型語言模型。按換行、句子與 tokenizer 邊界分段，保留換行與縮排；未完整結束的模型輸出視為失敗，不顯示半成品為完成結果。

`.tlpack` 是資料專用 ZIP，包含 manifest、模型、tokenizer、LICENSE、NOTICE。安裝器檢查：

- 語言方向、版本、runtime、來源及授權欄位。
- 明確的資料檔案白名單，拒絕路徑穿越、執行碼、重複／未列出項目、symlink。
- 檔案大小與 SHA-256，全包上限 2 GiB。
- 先驗證至暫存目錄再替換；失敗或取消保留原有正常包。
- 手動移除的隨附包會留下移除標記，避免重啟後又自動安裝；手動匯入可恢復。

SHA-256 是內容完整性與固定版本重現的檢查，**尚不是發行者簽章／信任鏈**。只應使用可信來源提供的正式資源。

原文和譯文不保存為歷史、log 或遙測。翻譯 worker 不含 HTTP 下載／雲端翻譯程式，Python audit hook 額外拒絕 socket 連線。模型下載僅在明確執行資源準備脚本時發生。此措施不等同已完成作業系統封包監測或實體斷網機驗收。

## 0.1.0 原型驗證紀錄

最終 TransLamp C# 回歸 **45／45 通過、無跳過**，包含真實 CPU 中英雙向推論、特殊符號回歸、推論中取消與包鎖定。指令：

```powershell
$env:TRANSLAMP_RESOURCES = "$PWD/artifacts/translamp"
dotnet test tests/TransLamp.Tests/TransLamp.Tests.csproj --no-restore --verbosity minimal -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
```

- 共用 AppWindow、預設 Ink、三語／主題切換保留原文、方向切換清除失效譯文、輸入限制與 URI 驗證。
- 語言包成功匯入、來源 archive 移除後仍可使用、損壞更新保留舊包、大小／hash／metadata／symlink／路徑穿越拒絕、取消清理與重啟移除狀態。
- 真實模型 process 取消及跨實例包鎖定另行通過。
- 最終離線包獨立再跑兩項真實模型整合測試，**2／2 通過**；確認隨附 .NET 10.0.12／WindowsDesktop framework、worker 與原始碼 SHA256 一致。
- 12 張最小視窗尺寸 900×720 的三語／竹亮／竹深渲染圖：`artifacts/translamp-ui`。
- 實際啟動 Offline Kit 視窗，自動準備兩個語言包並完成英翻中；確認換行保留與完成狀態。此過程發現 tokenizer 特殊符號 `▁` 外洩，已修正解碼並以相同例句通過 runtime 與 C# 回歸。
- 桌面後续驗收收到使用者實體 Esc 停止訊號，已停止所有 Computer Use；不繞過停止訊號操作桌面。後續採程式測試與檔案驗證。

007 接入時 ToolKeeper.Tests **165／165 通過**；共享工作目錄後續加入 006 後，重新還原並執行產品目錄／URI 相關契約 **67／67 通過**，確認 007 仍可共存。既有 WPF 並行測試偶發共享集合例外，驗證命令暫停 xUnit collection parallelization，未擅改全專案測試策略。

Runtime 有 **11／11 單元測試**與 **10 項真實 worker 檢查**。結果保存於 `artifacts/translamp/verification.json`。單台開發機的例句實測（含新 worker 載入；非低階硬體承諾）：

| 輸入 | 時間 | 實際結果 |
| --- | --- | --- |
| 英文59字元：`Please restart the device and check the network connection.` | 約0.28秒 | `请重新启动设备并检查网络连接。` |
| 繁中17字：`請重新啟動設備，然後檢查網路連線。` | 約0.71秒 | `Please restart the device and check the network connection.` |
| 1,079字元英文／18段 | 約1.04秒 | 18段均完成 |

### 已確認的模型品質缺陷

輸入 `Please check the network cable.` 時，模型可能產出：

> 请检查access-date=中的日期值 (帮助) 网络电缆.

`access-date=中的日期值 (帮助)` 並不存在於原文。調整為 Argos 官方 beam／length 設定仍可重現，屬目前模型的品質缺陷。**未以例句特判或字串刪除掩蓋，也未標記為已修正；正式發布前必須評估替代模型或改善方案。** 技術流程測試通過不代表所有譯文正確。

另已執行 **40 組自行撰寫的中英工程／一般例句**，40 個 process 正常完成，總計15.806秒。來源與譯文在 `artifacts/translamp/quality-samples.json`，檢視紀錄在 `quality-review.json`；此兩檔也隨離線包 `Verification` 交付。人工抽看確認：

| 原文內容 | 譯文缺陷 |
| --- | --- |
| `設備 D100 在 3000 ms 內沒有回應。` | `3000 ms` 被改為 `30000 ms` |
| IP `192.168.1.10`、port `502` | IP 被改為 `192168.1.10`，port 遺漏 |
| `C:\Logs\alarm_2026.txt` | 路徑內插入空白 |
| `retry_count` | 被翻譯為 `重试_ 计数` |
| `motor`、`韌體` | 分別誤譯為「运动」、`body` |
| 「我明天早上會到」 | 遺漏「明天」 |

目前版本可用於產品流程、離線部署與模型替換的開發驗證，**尚未通過正式翻譯品質驗收**。工程參數、位址及程式識別字仍須以原文為準。

### 可重現資源與大小

`Prepare-TransLampResources.ps1 -Offline` 已實跑成功，從固定 SHA256 快取重建並完成驗證。這是建置流程禁止下載，不代表已完成乾淨目標機的全機斷網測試。

| 元件 | 版本／實際大小 |
| --- | --- |
| Runtime | Python 3.12.10、CTranslate2 4.8.2、SentencePiece 0.2.2；131,975,734 bytes（約126 MiB） |
| English → 中文 pack | 70,271,470 bytes（約67 MiB），安裝後約81 MiB |
| 中文 → English pack | 73,453,779 bytes（約70 MiB），安裝後約81 MiB |
| Microsoft VC++ x64 | 14.44.35211.0；25,635,768 bytes，簽章驗證為 Microsoft Corporation／Valid；未執行安裝 |

Runtime 主要空間來自 CTranslate2 native libraries 與 NumPy，未引入 PyTorch／Stanza 整套環境。Python 固定版本與 native 相依的安全維護更新、完整散布授權盤點仍須在正式發布前處理。

同一工作目錄另有先前 UI／MarkPad 修改，以及其他工作加入的 HistoLens 006；本次保留並配合既有內容，未將其他工作計入 007 交付。

## 尚未完成與待使用者檢視

| 項目 | 現況／後續必要工作 |
| --- | --- |
| 正式發行 | 目前為未簽章開發版；尚未建立 Store MSIX、正式網站下載、產品 Store ID、發行者簽章。沒有虛構下載入口。 |
| Protocol 安裝 | App 接受 `translamp://open`，Launcher 會優先使用已登記 protocol；尚無正式 Installer 自動註冊或跨通路衝突處理。本次沒有寫入 Windows registry。 |
| 純離線目標機 | 此機可完成 local CPU 推論；未在從未連網、沒有既有 VC++ runtime 的乾淨機器做完整驗收。需在乾淨 Windows 10／11 x64 以隨附先決元件验证。 |
| 低階硬體 | 4 GB RAM、舊 CPU 指令集與多代 Windows 10 相容性尚未實測，不宣稱已達正式最低需求。 |
| 翻譯品質 | 中文輸出以簡體為主；已確認上方無關文字插入問題。專有名詞、型號、數字、單位與否定語意可能翻錯。尚無繁簡切換或術語保護引擎。正式發布前需人工檢視 30–50 組現場例句並決定可接受模型。 |
| 語言包下載 UI | 本版提供隨附中英包、離線匯入与可重現的資源準備脚本；尚未建立正式線上包目錄、UI下載、斷點續傳與發行者簽章。 |
| 更多語言 | 日文選項明確標示未提供，不能翻譯；不列出尚未驗證的模型作為已支援。 |
| 載入效能 | 每次翻譯啟動隔離 process 並載入模型，尚無常駐模型快取；優先選擇簡單的取消與資源回收流程。 |
| 產品封裝 | 現在是 portable開發包；是否以 Store＋官網安裝器／Offline Kit正式發行，依原規格仍需後續封裝驗證。 |
| 授權發布審查 | 模型原README標示 OPUS CC-BY 4.0，包內保留模型及 Argos MIT 授權和來源署名；正式對外散布仍需依實際發行方式再核對完整第三方義務。 |

上述項目沒有阻止本次可執行原型，也沒有被標為已完成的正式產品能力。
