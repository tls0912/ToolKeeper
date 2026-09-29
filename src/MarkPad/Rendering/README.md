# Rendering

Markdown Preview 相關邏輯集中在這裡。

`MarkdownRenderer` 以 Markdig 解析 CommonMark、GFM table/task/strike/autolink、footnote，並保留原始行號。Emoji shortcode 由傳入的預覽偏好控制；MarkPad 自 0.1.6 起預設開啟轉換。

`PreviewPane` 使用 `WebView2CompositionControl`，讓 WPF 搜尋浮窗可以顯示在預覽上方。此控制項需要專案使用 `net10.0-windows10.0.17763.0` 或更新的 Windows TFM。

## API

- `ShowAsync(markdown, filePath, options, scroll, sourceLine)`：背景產生 HTML、導覽預覽、恢復位置；較舊的非同步結果不會覆蓋新文件。
- `PrepareAsync(markdown, filePath, options)`：僅預先產生 HTML 的選用 API；編輯模式自 0.1.15 起直接使用 `ShowAsync` 更新右側即時預覽。
- `Headings`／`HeadingsChanged`：目前成功顯示頁面的 H1–H6 清單。標題含唯一 ID、純文字名稱、層級與原稿行號，由清理過的 DOM 產生，供原生章節列表使用。
- `IsShowing(markdown, filePath)`：確認已顯示的頁面與原稿完全一致；主視窗使用此檢查拒絕即時預覽更新前的舊待辦勾選事件。
- `InvalidateDisplay()`：互動元件已改變 DOM 時，要求下次顯示重新導覽。待辦點擊不論接受或拒絕都會立即從原稿重繪，避免快速 Undo 或過期點擊留下錯誤勾選狀態。
- `FindAsync(text, matchCase, backwards, restart)`：literal 搜尋；空字串會清除標記。支援跨 inline formatting 搜尋。
- `GetSelectionAsync()`、`GetScrollAsync()`、`CloseOverlayAsync()`、`GoToAnchorAsync()`。
- `ScrollToPositionAsync(position)`：依小數原稿行號連續對齊編輯器；捲至頂端或底端時同步到另一側的文件邊界。
- `ExportPdfAsync(markdown, filePath, options, outputPath, overwrite, outlineLevels)`：專用匯出頁產生 A4 白底 PDF，含可跳轉的章節書籤。`outlineLevels` 未指定時收錄 H1–H6，空集合關閉書籤；排除的標題仍完整印出。
- `PreviewOptions`：暗色、閱讀字型、字級（8–72）、code 行號、emoji、語系、唯讀。後者停用 task checkbox。

`MessageReceived` 以 `PreviewMessage` 傳回 `link`、`edit`、`task`、`copy`、`copy-markdown`、`copy-link`、`search`、`scroll`、`shortcut`、`overlay`、`ready`、`error`。`Line` 與搜尋 `Index` 為 1-based；沒有結果時 `Index=0`。搜尋 `Flag` 表示已循環，task `Flag` 表示勾選狀態，overlay `Flag` 表示開啟。scroll 的 `SourcePosition` 是視窗頂端對應的小數原稿行號，`ScrollProgress` 是 0–1 捲動比例，`Flag` 標示同步或恢復位置產生的事件，以防止回授。主視窗負責檔案操作、剪貼簿與外部瀏覽器。

每個分頁保留獨立的預覽與編輯器。閱讀模式左側為原生章節列表，編輯模式左側為 AvalonEdit；右側共用此預覽。輸入停止後在背景轉譯（一般 300 ms、大型檔案 750 ms），導覽前保存目前捲動位置，舊請求不能覆蓋新頁面。章節跳轉會展開折疊內容，並透過 `scroll` 的原稿行號更新大綱目前項目。

編輯模式由 `DocumentView` 雙向同步兩側捲動，依原文行號與區塊高度插值，支援不同字級與自動換行；同步不移動游標或選取範圍。快速捲動合併至最新位置，重新轉譯後以編輯器目前位置對齊；隱藏分頁、閱讀模式與尚未追上原稿的預覽不會反向移動編輯器。

## 安全與離線

Markdown HTML 先經 HtmlSanitizer 元素／屬性／URI allowlist；文件無法加入 script、style、event handler、iframe、SVG DOM、表單或 host object。產生的 trusted script/style 使用隨機 CSP nonce；JSON 使用預設的安全編碼，避免原文終止 script element。

本機圖片只接受文件資料夾樹內的允許圖片副檔名，拒絕 UNC、絕對路徑、`..` 逃逸與 reparse point，單張最多 20 MB。圖片轉為每份 render 獨立的 synthetic URL，由 `WebResourceRequested` 精確供應，不公開整個資料夾。遠端圖片顯示離線 placeholder；其他資源要求全部拒絕。任意導覽、下載、另開 WebView 視窗、權限要求一律阻擋。外部 HTTP(S) 或 mailto 只傳回主視窗處理。

`Resources/Preview.css` 與 `Preview.js` 以 embedded resource 打包，沒有 CDN 或外部 JavaScript。程式碼使用本機輕量 tokenizer；支援常見語言的 keyword/string/number/comment，其餘語言保留原始文字。Preview 提供 heading fold/anchor、code copy、table 自身捲動、圖片放大與 Ctrl+wheel/拖曳、純文字／Markdown 複製與右鍵選單。

可選的 `PreviewPane(browserDataFolder)` 讓 smoke test 使用獨立的 WebView2 profile，不接觸使用者資料。

PDF 匯出透過 WebView2 的 CDP `Page.printToPDF` 產生 tagged PDF 與 document outline，再循序 `IO.read` 寫入同目錄暫存檔；完成後才替換目標，失敗保留原 PDF。沿用 A4、15 mm 邊界、白底、圖片及字型載入等待。標題名稱沿用清理後的純文字大綱；只在隔離的匯出頁將未收錄層級改為 `role=presentation`，不更動 Markdown 或閱讀頁。CDP 呼叫及串流讀取設有逾時，`IO.close` 在成功或失敗時都嘗試執行。

「更多 → PDF章節大綱」可獨立勾選 H1–H6，預設全開。`AppSettings.PdfOutlineLevels` 保存選擇；讀取設定時過濾非法層級並去重、排序，空集合保留為全部關閉。設定僅影響後續 PDF 匯出。

捲動回歸檢查：`dotnet test tests/MarkPad.Tests/MarkPad.Tests.csproj` 包含真實 WPF／WebView2 連動；標記為 `Category=WebView2` 的測試需要已安裝 Runtime 且允許啟動瀏覽器子程序。`node --test tests/MarkPad.Tests/PreviewScroll.test.cjs` 驗證預覽位置插值、文件邊界與延遲事件抑制。
