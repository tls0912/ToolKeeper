# Rendering

Markdown Preview 相關邏輯集中在這裡。

`MarkdownRenderer` 以 Markdig 解析 CommonMark、GFM table/task/strike/autolink、footnote，並保留原始行號。Emoji shortcode 由傳入的預覽偏好控制；MarkPad 自 0.1.6 起預設開啟轉換。

`PreviewPane` 使用 `WebView2CompositionControl`，讓 WPF 搜尋浮窗可以顯示在預覽上方。此控制項需要專案使用 `net10.0-windows10.0.17763.0` 或更新的 Windows TFM。

## API

- `ShowAsync(markdown, filePath, options, scroll, sourceLine)`：背景產生 HTML、導覽預覽、恢復位置；較舊的非同步結果不會覆蓋新文件。
- `PrepareAsync(markdown, filePath, options)`：編輯停止後只預先產生 HTML。它不啟動 WebView2，也不改變目前畫面。
- `Suspend()`：切換文件或模式前，停止舊預覽傳回事件。需要記住位置時，先呼叫 `GetScrollAsync()`。
- `FindAsync(text, matchCase, backwards, restart)`：literal 搜尋；空字串會清除標記。支援跨 inline formatting 搜尋。
- `GetSelectionAsync()`、`GetScrollAsync()`、`CloseOverlayAsync()`、`GoToAnchorAsync()`。
- `PreviewOptions`：暗色、閱讀字型、字級（8–72）、code 行號、emoji、語系、唯讀。後者停用 task checkbox。

`MessageReceived` 以 `PreviewMessage` 傳回 `link`、`edit`、`task`、`copy`、`copy-markdown`、`copy-link`、`search`、`scroll`、`shortcut`、`overlay`、`ready`、`error`。`Line` 與搜尋 `Index` 為 1-based；沒有結果時 `Index=0`。搜尋 `Flag` 表示已循環，task `Flag` 表示勾選狀態，overlay `Flag` 表示開啟。主視窗負責檔案操作、剪貼簿與外部瀏覽器。

## 安全與離線

Markdown HTML 先經 HtmlSanitizer 元素／屬性／URI allowlist；文件無法加入 script、style、event handler、iframe、SVG DOM、表單或 host object。產生的 trusted script/style 使用隨機 CSP nonce；JSON 使用預設的安全編碼，避免原文終止 script element。

本機圖片只接受文件資料夾樹內的允許圖片副檔名，拒絕 UNC、絕對路徑、`..` 逃逸與 reparse point，單張最多 20 MB。圖片轉為每份 render 獨立的 synthetic URL，由 `WebResourceRequested` 精確供應，不公開整個資料夾。遠端圖片顯示離線 placeholder；其他資源要求全部拒絕。任意導覽、下載、另開 WebView 視窗、權限要求一律阻擋。外部 HTTP(S) 或 mailto 只傳回主視窗處理。

`Resources/Preview.css` 與 `Preview.js` 以 embedded resource 打包，沒有 CDN 或外部 JavaScript。程式碼使用本機輕量 tokenizer；支援常見語言的 keyword/string/number/comment，其餘語言保留原始文字。Preview 提供 heading fold/anchor、code copy、table 自身捲動、圖片放大與 Ctrl+wheel/拖曳、純文字／Markdown 複製與右鍵選單。

可選的 `PreviewPane(browserDataFolder)` 讓 smoke test 使用獨立的 WebView2 profile，不接觸使用者資料。
