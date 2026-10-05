# 隱私權原則欄位

先前使用者提供的 Partner Center 畫面有「提供隱私權原則 URL」及「提供隱私權原則文字」兩個選項。若本次後台仍提供文字選項，可貼入完整原則；若使用 URL，須填入實際公開的汗青政策網址，不以 Microsoft 的政策網址代替。

## 目前文件狀態

以下是依汗青 1.0.3.0 實作整理的八語文字，更新日期為 2026-10-04；依發行者要求不列聯絡 Email，沒有待補標記或虛構聯絡資料。

- [繁體中文](listing/zh-TW/privacy-policy.txt)
- [English](listing/en-US/privacy-policy.txt)
- [日本語](listing/ja-JP/privacy-policy.txt)
- [简体中文](listing/zh-CN/privacy-policy.txt)
- [Español](listing/es-ES/privacy-policy.txt)
- [العربية](listing/ar-SA/privacy-policy.txt)
- [Français](listing/fr-FR/privacy-policy.txt)
- [한국어](listing/ko-KR/privacy-policy.txt)

將適用語言的全文貼入「提供隱私權原則文字」欄位。如果畫面只有一個政策欄位，可使用主要商店語言的版本；其他語言保留供對應商店頁面或日後公開政策使用。保存後以 Partner Center 的實際驗證結果為準。

這些文件說明文件及圖片的本機處理、偏好與最近路徑、選用的保留開啟檔案路徑、復原草稿、日誌、WebView2 資料、Store 授權、剪貼簿及使用者自行開啟的外部網站。它們不宣稱程式完全不處理個人資訊，也不宣稱所有資料皆已加密。

## 依據與範圍

文字選項依本次使用者提供的 Partner Center 畫面；Microsoft 的 [MSIX 產品屬性說明](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/enter-app-properties#privacy-policy) 另指出，即使未向外傳送個資，也可能因套件宣告的能力而要求隱私權原則。請以後台實際驗證為準，提供原則本身，而非為了消除提示改填不符程式行為的答案。

程式碼核對來源：`SettingsService`、`DocumentSessionService`、`RecoveryService`、`LocalLog`、`PaidLicenseCache`、`StoreLicenseService`、`MarkdownRenderer`、`PreviewPane` 及 `MainWindow.OpenLinkAsync`。本輪以隔離的程式介面產生素材截圖，未安裝 Store 套件或呼叫 Store 授權；MSIX 不變。
