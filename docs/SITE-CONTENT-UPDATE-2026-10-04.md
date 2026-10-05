# 工具番網站內容更新（2026-10-04）

目標網站：https://toolkeeper.tls0912.chatgpt.site

Sites 專案：`appgprj_6ab1314d655481919a9589d7b912dce0`。保留既有深色／琥珀品牌、站點識別與私人存取設定；內容依本機目前工作樹（含既有未提交修改）整理。

## 變更

- 將舊版「MarkPad 設計中」與候選工具概念頁擴充為 001–007 產品目錄。
- 加入實作功能、內建／獨立身分、開發狀態、取得方式、六種使用情境及六項常見問題。
- 用產品資料包中的汗青竹子亮色閱讀／編輯截圖取代示意畫面。
- 修正「所有工具都是獨立 Store App」；不再列出未核實售價或全系列不需登入／連線的宣稱。
- 汗青僅提供已配置的 Store 商品頁連結，不宣稱特定新版本已上架；其餘工具不新增未確認的下載網址。
- HistoLens 明示歷史日線、最新公布本益比與相似分數的限制；TransLamp 說明完整離線包隨附基本中英資源及其他語言按需安裝。

## 主要程式依據

| 網頁內容 | 實作依據 |
| --- | --- |
| 七項目錄與取得狀態 | `src/ToolKeeper/Services/ProductCatalogService.cs`、`ProductLauncherService.cs` |
| 宿主、系統匣與桌面生命週期 | `src/ToolKeeper/PlatformController.cs` |
| 汗青閱讀／編輯、分頁、搜尋、PDF | `src/MarkPad/DocumentView.cs`、`MainWindow.Tabs.cs`、`MainWindow.Search.cs`、`MainWindow.Pdf.cs` |
| 汗青復原與偏好 | `src/MarkPad/Services/DocumentSessionService.cs`、`MainWindow.Preferences.cs`、共用語系目錄 |
| 桌面分類與群組 | `src/ToolKeeper.Desktop/Services/ClassificationService.cs`、`Views/GroupWindow.cs`、`Views/SettingsWindow.cs` |
| 編碼與位元組 | `src/ConvAnvil/Services/EncodingCatalog.cs`、`EncodingConversionService.cs`、`FileConversionService.cs` |
| 雜湊與 ICO | `src/ToolKeeper/Services/FileHashService.cs`、`IconConversionService.cs`、兩個模組視窗 |
| 歷史資料、相似度與本益比 | `src/HistoLens/MainWindow.Data.cs`、`MainWindow.Similarity.cs`、`MainWindow.Valuation.cs` |
| 翻譯與語言包 | `src/TransLamp/Core/TranslationEngine.cs`、`LanguagePackService.cs`、`LanguagePackCatalog.cs`、`packaging/TransLamp/README.md` |

## 驗證

- 靜態 HTML 網站，無需編譯；未執行 .NET Build／Test。
- 本機預覽 HTTP 200；瀏覽器確認七張產品卡、兩張圖片成功載入、頁內連結無缺失。
- 桌面與 390 px 手機寬度檢查，無水平溢出；FAQ 展開正常；無瀏覽器警告／錯誤。
- 網站來源 `git diff --check` 通過。
- 公開上架狀態及桌面 App 實機行為不是本輪網站驗證範圍。

## 本機網站來源

`sites-toolkeeper-source/` 是由 Sites 官方 helper 取得的獨立 Git 儲存庫，與原有 .NET 專案變更分開；網站檔案為 `dist/index.html` 與 `dist/assets/hanqing-*.png`。

初次開啟受到沙箱網路與 Git 擁有者差異影響；僅本次 helper 程序使用明確路徑的 `safe.directory`，未改全域 Git 設定。`sites-toolkeeper/` 保留初次未完成初始化的 Git 資料。

效率紀錄：TaskRunId 1004、AgentRunId 1687。子代理已 completed 並已確認狀態；環境沒有 close/delete agent API，`ClosedNormally` 留空。

## 部署結果

- 狀態：`succeeded`，2026-10-04 19:57（Asia/Taipei）。
- 來源 commit：`4b136ab5d77cf1ebc0221344dd44519c572c5ee5`。
- Version ID：`appgprj_6ab1314d655481919a9589d7b912dce0~appgver_18a69ab9945c8191b0d9ce940372c13e`。
- Deployment ID：`appgdep_6ac23f3190f08191be47fc0f20f8ce1c`。
- 使用 owner-private 發布流程；未更改訪客或分享設定。
- 發布後已在瀏覽器開啟正式網址，確認新版七項工具及所有新增區塊存在。
- 官方封裝 helper 在 Windows 需要本程序加入既有 Git Bash 路徑，並設 `TAR_OPTIONS=--force-local`；沒有安裝軟體或改永久環境設定。
