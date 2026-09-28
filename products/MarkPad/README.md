# 汗青 — Microsoft Store 產品說明包

對應 **汗青 - Markdown Writer／MSIX 1.0.2.0（x64）**，已包含竹材 M 新圖示。開發版仍為 0.1.19，正式程式「關於」顯示 1.0.2。製作日期：2026-09-28。

先開啟 [素材預覽頁](index.html)，可查看三語文案、程式截圖和圖示；文案檔都是可直接貼入 Partner Center 的 UTF-8 純文字。交接包為 `products/MarkPad-StoreListing-1.0.2.0.zip`，程式安裝包另使用 `Hanqing_1.0.2.0_x64.msix`。M 由四段帶皮青綠竹材構成，保留藍底與短線；程式圖示與 23 張套件 PNG 已封入新 MSIX，與本包素材一致。舊 1.0.1.0 包保留原樣。

## 三種語言

| Partner Center 語言 | 說明檔 | 實際程式截圖 |
| --- | --- | --- |
| 繁體中文（台灣） | [zh-TW](listing/zh-TW/description.txt) | [5 張截圖](screenshots/zh-TW/01-preview-bamboo-light.png) |
| English（United States） | [en-US](listing/en-US/description.txt) | [5 screenshots](screenshots/en-US/01-preview-bamboo-light.png) |
| 日本語 | [ja-JP](listing/ja-JP/description.txt) | [5 枚のスクリーンショット](screenshots/ja-JP/01-preview-bamboo-light.png) |

## 依你畫面中的欄位貼入

| Partner Center 欄位 | 對應語言資料夾中的檔案 | 用法 |
| --- | --- | --- |
| 說明／Description | `description.txt` | 全文貼入；這是必填欄位 |
| 此版本的新增功能／What's new | **首次上架留白** | `release-notes-update-only.txt` 只是後續更新備稿，核對當次變更後再用 |
| 產品功能／Product features | `features.txt` | 12 項功能，每行各填一格，不自行加項目符號 |
| 簡短說明／Short description | `short-description.txt` | 全文貼入 |
| 搜尋關鍵字／Keywords | `keywords.txt` | 每行一個詞組，各填一格 |
| 其他系統需求 | `system-requirements.txt` | 每行一項；確認實際支援環境後填寫 |
| 隱私權原則文字 | `privacy-policy.txt` | 選「提供隱私權原則文字」後貼入全文，依發行者要求不列聯絡 Email |
| 螢幕擷取畫面說明 | `screenshot-captions.txt` | 檔名後的文字就是該張截圖說明，不貼檔名 |

三語文案內容對齊，包含本機 Markdown 閱讀與編輯、章節列表、左右即時預覽、多分頁、搜尋取代、字型工具列、竹子亮／深色、PDF 匯出及 7 天免費試用。售價以商店設定為準，沒有代填未確認的價格。

## 截圖上傳順序

每種語言各 **5 張 1600×1000 PNG**，共 15 張，請上傳至相同語言的 Desktop／PC 截圖欄位。包含四種風格的預覽與竹子亮色編輯；程式選單仍使用「竹子（亮色）」名稱。

1. `01-preview-bamboo-light.png`：竹子亮色、宣紙底色與 H1–H6 章節導覽。
2. `02-preview-bamboo-dark.png`：竹子深色、宣紙底色與章節導覽。
3. `03-preview-light.png`：一般亮色風格的文件預覽。
4. `04-preview-dark.png`：一般深色風格的文件預覽。
5. `05-edit-bamboo-light.png`：竹子亮色，左編輯／右即時預覽，以及上方字型與格式工具列。

截圖取自目前真實 WPF／AvalonEdit／WebView2 介面，使用本包原創範例文件；沒有添加外部標語、浮水印或替換程式畫面。視窗採離屏擷取，設定、復原目錄與 WebView2 profile 全部隔離，未使用私人文件或修改使用者偏好。截圖展示文件功能，不代表真實 Store 授權驗收已完成。

## 圖示與宣傳圖要放哪裡

| 素材 | 尺寸 | 用途 |
| --- | --- | --- |
| [app-icon-300.png](assets/app-icon-300.png) | 300×300 | 商店 **1:1 App tile icon**，建議上傳；使用新的四段竹材 M 圖示 |
| [hero-art-1920x1080.png](assets/optional-art/hero-art-1920x1080.png) | 1920×1080 | **選填**的 16:9 Super hero art；純圖形，無標題、文字或程式 UI |
| [box-art-1080.png](assets/optional-art/box-art-1080.png) | 1080×1080 | 備用方形宣傳圖；Box art 主要供遊戲，汗青不必填 |
| [poster-art-720x1080.png](assets/optional-art/poster-art-720x1080.png) | 720×1080 | 備用 2:3 宣傳圖；Poster art 主要供遊戲，汗青不必填 |
| `assets/msix-package/` | 23 種套件檔名／尺寸 | 1.0.2.0 MSIX 的 Windows／套件圖示逐檔副本，已核對 SHA256；**不用逐張上傳到商店截圖欄位** |
| `assets/source-brand/` | SVG、ICO | 更新後的竹材 M 原始圖示備份，與程式原始碼資源一致 |

圖示、方形與直式宣傳圖使用共用向量來源 `scripts/MarkPadIconRenderer.cs`。圖示與無文字宣傳圖可三語共用，但每個語言的商店頁面仍要分別選取／上傳。所有 PNG 均小於 50 MB。宣傳圖與程式截圖已分開存放，請勿混用欄位。

## 送審還會用到

- [submission-notes.md](submission-notes.md)：`runFullTrust` 英文用途說明、認證備註及授權實測步驟。
- [product-info.json](product-info.json)：對外名稱、版本、Store ID 與已確認的套件識別。
- [verification/validation.json](verification/validation.json)：文字長度、圖片尺寸、大小與檢查結果。
- [file-manifest.json](file-manifest.json)：本包檔案與 SHA256，便於交接核對。
- `samples/`：三語範例文件；`source/`：可重跑的素材產生器。

**隱私權原則已有三語文字，可使用目前表單的「提供隱私權原則文字」選項；不列聯絡 Email。** [填寫說明](privacy-submission.md)。若改選 URL，仍須提供實際公開的政策網址；支援網址尚未提供。本包不包含虛構網址或尚未核准的上架聲明；WACK、已安裝套件與真實 Store 授權情境仍須驗收。1.0.2.0 MSIX 已完成封裝及內容核對；此次補入隱私權文件未修改 MSIX，也未變更 Partner Center 設定。使用者曾回報 runFullTrust 接受度警告，實際提交進度以後台為準。

## 微軟規格與核對日期

2026-09-28 核對 Microsoft 的 **MSIX** 文件：說明最多 10,000 字元；功能最多 20 項、每項 200 字元；每語言至少一張截圖，桌面 PNG 至少 1366×768、每張不超過 50 MB；截圖說明最多 200 字元。此包每語言提供 5 張實際截圖。

- [MSIX 商店文字與語言欄位](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)
- [MSIX 螢幕擷取畫面與圖像規格](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
- [關鍵字等其他資訊](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-additional-information)
- [受限制的功能用途說明](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/manage-submission-options#restricted-capabilities)
