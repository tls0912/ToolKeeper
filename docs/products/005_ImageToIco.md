# 005｜Image → ICO

更新日期：2026-09-28。產品類型：ToolKeeper 內建模組。

Image → ICO 將圖片轉為多尺寸 Windows 圖示，已從 ToolKeeper 主視窗拆成自己的模組視窗。由 `ToolKeeper.exe` 宿主開啟，不產生另一個獨立執行檔，不提供另外安裝或取得的入口。

## 入口與視窗

- 產品 ID：`005`；正式名稱：`Image → ICO`；副標題：`Icon Converter`。
- 原生標題列：`Image → ICO - Icon Converter`。
- 從 ToolKeeper 工具列表或「工具番」桌面群組開啟；平台入口 URI 為 `toolkeeper://run/005`。
- 宿主持有模組視窗，再次開啟時喚回既有視窗；主視窗隱藏不影響轉換，平台退出時一併關閉。
- 視窗只包含 ICO 功能，不載入或隱藏 HASH 工具。主視窗只保留工具列表。
- 使用 `ToolKeeper.UI.AppWindow`，支援繁中、英文、日文與共用風格／語言／關於；切換時保留轉換結果及執行中的批次。
- 預設 760 × 660，最小 660 × 560；結果列表可捲動。

## 功能範圍

- 拖入或多選 PNG、JPG／JPEG、BMP，逐張處理；忙碌時不接受第二批。
- 產生含 16、24、32、48、64、128、256 px 七個尺寸的 ICO，維持比例與透明背景，以透明邊界補齊正方形，支援 JPEG EXIF 旋轉／鏡像方向。
- 輸出到來源圖片所在資料夾；同名檔案加編號，不覆寫原圖或既有 ICO。
- 先寫同目錄暫存檔，成功後發布；失敗或合作式取消清理暫存檔。
- 結果逐筆顯示成功路徑或失敗原因；一張失敗不阻止其他圖片。「在資料夾中顯示」定位成功輸出。
- 可取消批次，已完成的 ICO 保留。模組真正關閉時取消剩餘工作，關閉被宿主取消時保留工作；不影響其他模組。
- 拖放只接受 Copy，不移動或刪除來源。

全部處理在本機完成，不上傳圖片、不使用付費 API。單檔限制沿用服務：64 MiB、3,200 萬像素、任一邊 16,384 像素；不支援 GIF、SVG、HEIC、WebP 或資料夾。Windows 原生解碼期間需等待該步完成才能回應取消。

## 實作與驗證

- 視窗：[ImageToIcoWindow.xaml](../../src/ToolKeeper/Modules/ImageToIcoWindow.xaml)，類別 `ToolKeeper.Modules.ImageToIcoWindow`。
- 公開工作入口：`Task ConvertImagesAsync(IEnumerable<string> paths)`；轉換服務沿用 `IconConversionService`。
- UI 測試使用真正的獨立 ICO 視窗，沿用混合成功／失敗批次、來源保留、同名輸出、忙碌拖放與語言切換案例，並檢查三語與最小尺寸離屏渲染。
- 本輪拆分的建置／測試結果統一記錄於 [ToolKeeper 實作報告](../TOOLKEEPER-IMPLEMENTATION.md)。真實多選對話框、Explorer 拖放、輸出定位及多螢幕 DPI 仍需人工驗收。
