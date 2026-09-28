# 004｜Hash Checker

更新日期：2026-09-28。產品類型：ToolKeeper 內建模組。

Hash Checker 提供單一檔案的雜湊計算與比對，已從 ToolKeeper 主視窗拆成自己的模組視窗。由 `ToolKeeper.exe` 宿主開啟，不產生另一個獨立執行檔，不提供另外安裝或取得的入口。

## 入口與視窗

- 產品 ID：`004`；正式名稱：`Hash Checker`；副標題：`File Hash Verification`。
- 原生標題列：`Hash Checker - File Hash Verification`。
- 從 ToolKeeper 工具列表或「工具番」桌面群組開啟；平台入口 URI 為 `toolkeeper://run/004`。
- 宿主持有模組視窗，再次開啟時喚回既有視窗；主視窗隱藏不影響模組工作，平台退出時一併關閉。
- 視窗只包含 HASH 功能，不載入或隱藏 ICO 工具。主視窗只保留工具列表。
- 使用 `ToolKeeper.UI.AppWindow`，支援繁中、英文、日文與共用風格／語言／關於；風格與語言切換保留輸入、結果、選取範圍及執行中的工作。
- 預設 760 × 660，最小 660 × 560；內容不足時可捲動。

## 功能範圍

- 從檔案總管拖入單一檔案，或使用檔案選擇對話框，計算 MD5、SHA-1、SHA-256。
- 三種演算法共用單次串流讀取；顯示進度，可取消或以另一個檔案取代目前工作，舊工作的結果不可覆蓋新結果。
- 結果可選取複製，「複製全部」包含演算法名稱。
- 貼上預期 HASH，自動辨識 32／40／64 位十六進位字串，忽略前後空白及大小寫；顯示相符、不相符或格式錯誤。
- 失敗、取消或更換檔案時清除前一次可複製結果，避免誤認為新檔案的 HASH。
- 拖放只接受 Copy；多檔、非檔案或 Move-only 拖放不啟動計算，不移動或刪除來源。
- 模組視窗真正關閉時取消自己的計算；關閉被宿主取消時保留工作。關閉本模組不取消其他模組工作。

全部處理在本機完成，不上傳檔案、不使用付費 API。此工具檢查的是雜湊值是否一致，不替檔案提供安全認證。

## 實作與驗證

- 視窗：[HashCheckerWindow.xaml](../../src/ToolKeeper/Modules/HashCheckerWindow.xaml)，類別 `ToolKeeper.Modules.HashCheckerWindow`。
- 公開工作入口：`Task LoadHashFileAsync(string path)`；雜湊服務沿用 `FileHashService`。
- UI 測試使用真正的獨立 HASH 視窗，沿用計算、比對、取代、取消、失敗清除及拖放回歸案例，並檢查三語與最小尺寸離屏渲染。
- 本輪拆分的建置／測試結果統一記錄於 [ToolKeeper 實作報告](../TOOLKEEPER-IMPLEMENTATION.md)。真實對話框、剪貼簿占用、Explorer 拖放與多螢幕 DPI 仍需人工驗收。
