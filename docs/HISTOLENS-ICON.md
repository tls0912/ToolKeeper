# HistoLens 圖示

更新日期：2026-10-04。

006 使用竹綠鏡框與竹節握柄的放大鏡，鏡內青綠、赭金兩條相同形狀的折線代表歷史走勢比對。圖示不含文字；透明外圍與淺色鏡面讓它能用於亮竹、暗竹及一般暗色介面。

## 資產與重製

- 向量原稿：[HistoLensIcon.xaml](../src/HistoLens/Assets/HistoLensIcon.xaml)。
- 透明 PNG：[HistoLens.png](../src/HistoLens/Assets/HistoLens.png)，512 × 512。
- Windows 圖示：[HistoLens.ico](../src/HistoLens/Assets/HistoLens.ico)，包含 16、20、24、32、40、48、64、128、256 px 九個透明 PNG 畫格。

修改向量原稿後，從專案根目錄執行：

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File scripts/New-HistoLensIcon.ps1
```

腳本只使用 Windows WPF，產出 PNG 與 ICO，不需外部繪圖套件。一般建置直接使用已提交的 ICO。

## 接線與驗證

ICO 以 WPF Resource 包含於 HistoLens 類別庫。`ProductIcon.Source` 載入並凍結最大畫格，供 HistoLens 的 `Window.Icon` 及工具番首頁 006 名稱旁的圖示共用。共用標題列與關於視窗沿用 `Window.Icon`；宿主的預設圖示處理只補沒有專用圖示的視窗。

本輪 Release 建置通過（0 警告、0 錯誤），9 項宿主測試及 3 項 HistoLens 主題畫面測試通過。宿主測試涵蓋載入後保留 006 圖示、其他視窗仍得到預設圖示，以及首頁最低尺寸版面。另已解碼確認九種 ICO 尺寸、角落透明及鏡面不透明，目視檢查小尺寸亮暗底色與三種主題截圖。

圖示設計驗證輸出位於 `artifacts/histolens-icon-verification/`。沒有啟動實際 Windows 桌面視窗檢查工作列；006 目前由 ToolKeeper.exe 承載，宿主 EXE、系統匣及純文字的桌面工具清單維持原設定。

## 確認後的程式整合

使用者於 2026-10-04 確認採用此版圖示。已以既有 `scripts/Publish-HistoLensPreview.ps1` 更新 `artifacts/histolens-preview/`；539 項 HistoLens 測試及 94 項宿主測試全部通過，發佈成功。此次發佈證據位於 `artifacts/histolens-icon-integration-verification/`。

發佈腳本會一併複製 `HistoLens.ico` 並建立帶專用圖示的 `HistoLens.lnk`，直接啟動 006，沿用該預覽目錄的 `profile` 與 `empty-desktop`。捷徑使用本機發佈位置；若移動或複製整個資料夾，請使用 `Start-HistoLens.cmd` 或重新發佈以重建捷徑。既有資料與偏好保留，執行中的其他預覽實例沒有被關閉。
