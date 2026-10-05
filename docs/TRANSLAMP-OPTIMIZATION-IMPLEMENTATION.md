# TransLamp 007 優化實作紀錄

更新：2026-10-03。基準已先提交為 `a617617`（`feat(translamp): add offline translator prototype and optimization review`），本輪版本為 0.1.1。

此文件接續[優化檢查](TRANSLAMP-OPTIMIZATION-REVIEW.md)，保留原檢查的重現結果，另外記錄修正、驗收與未解決事項。

## 本輪交付

- [TransLamp 0.1.1 可執行檔](../artifacts/TransLamp-OfflineKit-0.1.1/TransLamp.exe)，請保留整個資料夾一起使用。
- [完整離線 ZIP](../artifacts/TransLamp-OfflineKit-0.1.1.zip)：279,951,744 bytes，約 267 MiB。
- ZIP SHA256：`AFB059B18204CFB2F6194D1C3EDF75E2E9F3C05C90FA03CD918541ACAD517163`。
- [當次發布驗證及完整檔案清單](../artifacts/TransLamp-OfflineKit-0.1.1/Verification/publish-verification.json)、[ZIP 逐檔驗證結果](../artifacts/translamp-fixes/final-artifact-verification.json)。

基準提交 `a617617` 已完成；此輪 0.1.1 優化以獨立 Git 提交保存，提交與遠端同步狀態以 Git 紀錄為準。其他工作同時修改的 001–006／MarkPad 檔案不計入本輪交付，也沒有批次重設或提交它們。

## 範圍

- 模型包：安裝前實際載入、失敗回復、重啟復原、移除保護、鎖內判斷、支援方向及逐包結果。
- 翻譯：長句分段、技術文字差異提示；保留原始譯文，不以硬編碼取代模型結果。
- 介面：採用既有工具番共用 UI，更新多實例語言包狀態與編輯後提示。
- 封裝：固定資源清單、SHA256、最終產物雙向驗證、暫存發布及目錄保護。

資源建置新增完整 Runtime 檔案清單，清單來自固定下載檔與專案來源。未知 DLL 不會因為存在於舊目錄就被納入新清單。只清理可重新產生且位於 Runtime 內的 Python bytecode；含未知內容的快取目錄會保留並使準備失敗。

## 修正與證據

| 項目 | 本輪處理 | 驗證重點 |
| --- | --- | --- |
| O01 技術文字錯譯 | 增加精確文字差異警示；模型品質仍未解決 | 比對來源已存在的數字、位址、路徑與識別字，不將 `ten → 10` 一概判錯；不改寫譯文 |
| O02 無效模型覆蓋 | staging 中實際載入模型成功才提交 | 有效 SHA256 的壞 model.bin 被拒，原模型前後仍輸出相同譯文；缺 runtime、取消及驗證錯誤保留原包 |
| O03 移除半毀 | 先改名再提交移除狀態，失敗回復 | 實際 Windows 檔案占用與 marker 寫入失敗不留下半毀的可見模型 |
| O04 中斷復原 | 小型交易紀錄；有 journal 就回復原狀，刪除 journal 才完成提交 | 模擬移動前後、首次安裝、移除及舊版 `.previous-*` 的磁碟狀態 |
| O05 自動安裝競態 | 已安裝／手動移除判斷放入同一寫入鎖 | 交錯匯入、自動準備與移除，不覆寫手動版本或撤銷移除意圖 |
| O06 方向不一致 | 匯入僅接受 `en↔zh` | 拒絕日文及未支援語言變體；介面語言與翻譯方向分開 |
| O07 單包阻擋批次 | 各包獨立處理，回傳成功數與失敗項目 | 部分成功、全部失敗及取消可區分 |
| O08 發布資源不足 | 全 Runtime 清單、來源／worker hash、語言包內容、Microsoft prerequisite 簽章與新產物 smoke | 當次報告綁定實際檔案 SHA256，不接受舊 verification.json 作為通過證據 |
| O09 發布目錄污染 | 禁止資源與輸出同路徑／互為父子，使用新的 staging 與受管輸出標記 | 失敗保留舊包；成功交付不夾帶舊 DLL；非空未標記目錄保留且拒絕覆寫 |
| O10 長句切斷關係 | 超過 256 tokens 時優先安全子句邊界，保留縮寫及編號上下文 | 331 tokens 全部保留，10 步驟數字計數一致；40 句舊輸出未變 |
| O11 跨視窗包狀態 | 閒置 Activated／開啟管理時刷新 | 原文保留，匯入／移除後按鈕狀態更新 |
| O12 編輯後舊狀態 | 編輯原文清除失效譯文、警示與完成／錯誤狀態 | 忙碌時仍保留當前進度與取消狀態 |

核心隔離測試 **50／50 通過**（47 一般／故障＋3 真模型，無跳過），見 [TRX](../artifacts/translamp-review/core/isolated-tests/results/core-fixed-all-tests.trx)。逾時保留舊包使用注入錯誤驗證，未實等 10 分鐘；中斷復原為精確磁碟狀態模擬，未實測斷電。已提交移除的隱藏殘留若因占用而清理失敗，後續管理操作會重試。

Runtime **19／19 單元、5／5 真模型回歸**通過；5 項包含 40 句診斷對照，見 [native-report.json](../artifacts/translamp-fixes/runtime/native-report.json)。原有錯譯仍在，40 句沒有變化僅代表這組樣本沒有輸出回歸。

封裝驗證器 **21／21 Python 測試**、發布流程 **18／18 PowerShell 檢查**通過，見 [流程結果](../artifacts/translamp-publish-tests/fd2030fedae644439e3c31f7378f9daf/results.json)。這 18 項的 dotnet 是測試替身，但使用 1,060 個真 Runtime 檔、兩個真語言包及真實雙向 worker；實際產品發布另記下方。測試涵蓋官方空白檔案、空白 native 拒絕、舊 DLL、前置／建置失敗保留舊包、長路徑與新報告綁定。

主代理實跑 `Prepare-TransLampResources.ps1 -Offline`：19 worker＋7 inventory 單元測試、10 真實 worker／協定檢查及雙包清單驗證通過。準備後 Runtime 大小為 129,817,758 bytes；相較先前紀錄移除了可重新產生的 bytecode，未刪除 native 相依。來源與產物 worker SHA256 同為 `BB7A17F7CF649F1AC121A0DC4D761B0D2EF52AB4856D9B6B285D1B1731306699`。

正式 C# 全套 **108／108 通過，0 跳過**，包括三項真模型整合，見 [整合 TRX](../artifacts/translamp-fixes/tests/translamp-0.1.1.trx)。建置成功，無警告或錯誤。

人工查看離屏圖後另發現中文／日文頁腳在最小視窗、展開語言包且有警示時裁切。已局部將該情況的清單最大高度 160→140 DIP，其他情況維持 160；測試加查每層祖先容器的可見邊界。最後變更後 **27／27 介面測試重跑通過**，見 [最終 UI TRX](../artifacts/translamp-fixes/tests/translamp-ui-final.trx)；已目視確認 [中文圖](../artifacts/translamp-ui/literal-warning-zh-TW-Ink-packs.png)與[日文圖](../artifacts/translamp-ui/literal-warning-ja-InkDark-packs.png)。圖片使用明確合成資料，未執行實體桌面操作，也不代表真實翻譯結果。

實跑 `Publish-TransLamp.ps1` 成功，產物為 self-contained Windows x64，EXE 產品版本 `0.1.1+a61761702cc4f60e9cce758bc54bb9213afe59f3`、組件版本 `0.1.1.0`。實際發布清單包含 1,471 個檔案，其中 Runtime 1,060 個；額外兩個為清單本身與受管目錄標記。包內 worker 真實中英雙向 smoke 通過；再以此包的 Runtime／LanguagePacks 執行 C# 雙向翻譯、取消／鎖定與壞模型保留舊包，**3／3 通過**，見 [最終包 TRX](../artifacts/translamp-fixes/tests/translamp-final-kit.trx)。本輪未啟動產品 GUI。

ZIP 內 1,473 個檔案均核對數量與 SHA256，沒有把舊準備報告當成當次通過證據。發布清單列出的內容約 426 MiB，另加兩個驗證檔；首次安裝兩個模型仍需約 162 MiB。大型產物保留於忽略的 `artifacts`，沒有加入 Git。

重跑命令（需先準備固定資源）：

```powershell
./scripts/Prepare-TransLampResources.ps1 -Offline
$env:TRANSLAMP_RESOURCES = "$PWD/artifacts/translamp"
dotnet test tests/TransLamp.Tests/TransLamp.Tests.csproj --no-restore --verbosity minimal -- xUnit.ParallelizeTestCollections=false xUnit.MaxParallelThreads=1
artifacts/translamp/Runtime/python.exe -I -B tests/runtime/test_translamp_quality.py
artifacts/translamp/Runtime/python.exe -I -B tests/runtime/test_translamp_publish.py
./scripts/Publish-TransLamp.ps1
```

## 尚未解決的品質與發布條件

目前 OPUS-MT／Argos 1.9 模型仍會誤譯術語、遺漏時間、交換角色或插入原文沒有的文字。技術 literal 差異提示只能指出部分可機械比對的問題；沒有提示也不代表語意正確。完整品質範例見[原實作紀錄](TRANSLAMP-IMPLEMENTATION.md)。

本輪保留逐段推論。先前 batch＝4 的小樣本有輸出改變，尚不足以證明品質無回歸；常駐模型也延後，以免同時改變取消、資源回收與語言包鎖定流程。

乾淨離線 Windows、舊 CPU、4 GB RAM、實體高 DPI／可及性、完整 native 授權盤點、正式安裝與簽章仍待驗收。實作與驗證階段未操作使用者桌面、未執行 VC++ 安裝器、未註冊 protocol；離線包未對外發布。

發布腳本對一般例外提供回復；如果在兩次資料夾改名之間被強制終止或斷電，先前版本可能保留於同層 `.<輸出名稱>.translamp-backup-<識別碼>` 目錄，需要人工確認後復原。本輪沒有把這種情況宣稱為完全自動復原。
