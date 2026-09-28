# 隱私權原則欄位

使用者提供的 Partner Center 畫面有兩個選項：「提供隱私權原則 URL」及「提供隱私權原則文字」。目前選到 URL，才會要求輸入網址。可以改選下方的文字選項，貼入完整原則；不應填入不存在的網址，也不要用 Microsoft 的隱私權網址代替汗青自己的政策。

## 目前文件狀態

以下是依汗青 1.0.2.0 實作整理的三語文字，已依發行者要求移除聯絡 Email，沒有待補標記或虛構聯絡資料。

- [繁體中文](listing/zh-TW/privacy-policy.txt)
- [English](listing/en-US/privacy-policy.txt)
- [日本語](listing/ja-JP/privacy-policy.txt)

將適用語言的全文貼入「提供隱私權原則文字」欄位。如果畫面只有一個政策欄位，可使用主要商店語言的版本；其他語言保留供對應商店頁面或日後公開政策使用。保存後以 Partner Center 的實際驗證結果為準。

這些文件說明文件及圖片的本機處理、偏好與最近路徑、復原草稿、日誌、WebView2 資料、Store 授權、剪貼簿及使用者自行開啟的外部網站。它們不宣稱程式完全不處理個人資訊，也不宣稱所有資料皆已加密。

## 依據與範圍

文字選項依本次使用者提供的 Partner Center 畫面；Microsoft 的 [MSIX 產品屬性說明](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/enter-app-properties#privacy-policy) 另指出，即使未向外傳送個資，也可能因套件宣告的能力而要求隱私權原則。請以後台實際驗證為準，提供原則本身，而非為了消除提示改填不符程式行為的答案。

程式碼核對來源：`SettingsService`、`RecoveryService`、`LocalLog`、`PaidLicenseCache`、`StoreLicenseService`、`MarkdownRenderer`、`PreviewPane` 及 `MainWindow.OpenLinkAsync`。未安裝或執行應用程式，本輪只更新商店文件與素材包，MSIX 不變。
