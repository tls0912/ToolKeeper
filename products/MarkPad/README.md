# 汗青 — Microsoft Store 八語素材包

對應 **汗青 - Markdown Writer／MSIX 1.0.3.0（x64）**。製作日期：2026-10-04。開發版版本仍為 0.1.19，正式程式顯示版本為 1.0.3。

先開啟 [素材預覽頁](index.html)，依語言複製文案及檢視截圖。交付 ZIP 為 `products/MarkPad-StoreListing-1.0.3.0.zip`，附同名 `.sha256`；解壓後開啟 `MarkPad/index.html` 即可離線瀏覽。ZIP 是素材交接包，Partner Center 文案、截圖及圖示須放入各自欄位；程式更新另使用 `Hanqing_1.0.3.0_x64.msix`。

## 八種語言

| 語言 | 素材資料夾 | 真實程式截圖 |
| --- | --- | --- |
| 繁體中文（台灣） | [zh-TW](listing/zh-TW/description.txt) | [5 張](screenshots/zh-TW/01-preview-bamboo-light.png) |
| English（United States） | [en-US](listing/en-US/description.txt) | [5 screenshots](screenshots/en-US/01-preview-bamboo-light.png) |
| 日本語 | [ja-JP](listing/ja-JP/description.txt) | [5 枚](screenshots/ja-JP/01-preview-bamboo-light.png) |
| 简体中文 | [zh-CN](listing/zh-CN/description.txt) | [5 张](screenshots/zh-CN/01-preview-bamboo-light.png) |
| Español | [es-ES](listing/es-ES/description.txt) | [5 capturas](screenshots/es-ES/01-preview-bamboo-light.png) |
| العربية（標準阿拉伯文） | [ar-SA](listing/ar-SA/description.txt) | [5 صور](screenshots/ar-SA/01-preview-bamboo-light.png) |
| Français | [fr-FR](listing/fr-FR/description.txt) | [5 captures](screenshots/fr-FR/01-preview-bamboo-light.png) |
| 한국어 | [ko-KR](listing/ko-KR/description.txt) | [5장](screenshots/ko-KR/01-preview-bamboo-light.png) |

語言代碼與 1.0.3.0 套件宣告一致。阿拉伯文文案及預覽頁文字框採由右至左排列；程式 Markdown 原始編輯器維持由左至右。

## Partner Center 欄位對照

各語言提供以下 **8 個 UTF-8 純文字檔**，共 64 個。不要把檔名或這份 README 貼入產品說明。

| 欄位 | 檔案 | 用法 |
| --- | --- | --- |
| Description／說明 | `description.txt` | 全文貼入 |
| Short description／簡短說明 | `short-description.txt` | 全文貼入 |
| What's new／此版本新增功能 | `release-notes-update-only.txt` | 本次 1.0.3 更新可貼入；首次提交才留白 |
| Product features／產品功能 | `features.txt` | 每行各填一格，不自行加項目符號 |
| Keywords／搜尋關鍵字 | `keywords.txt` | 每行一個詞組，各填一格 |
| Additional system requirements | `system-requirements.txt` | 每行一項 |
| Privacy policy／隱私權原則文字 | `privacy-policy.txt` | 依後台提供的文字欄位貼入全文；不列聯絡 Email |
| Screenshot captions／截圖說明 | `screenshot-captions.txt` | 每行為檔名、Tab、說明；只貼入 Tab 後的說明 |

文案涵蓋目前本機 Markdown 功能、八語介面、可選的重新開啟文件、共用預覽／PDF 章節層級及三篇內建閱讀文章。7 天免費試用依發行者先前確認的設定，售價及可用資格以 Microsoft Store 為準；本次未登入後台改動或查驗設定。

## 40 張程式截圖

每語言各 **5 張 1600×1000 PNG**，上傳至相同語言的 Desktop／PC 截圖欄位：

1. `01-preview-bamboo-light.png`：竹子亮色、宣紙底色及章節導覽。
2. `02-preview-bamboo-dark.png`：竹子深色文件預覽。
3. `03-preview-light.png`：一般亮色文件預覽。
4. `04-preview-dark.png`：一般深色文件預覽。
5. `05-edit-bamboo-light.png`：竹子亮色，左側 Markdown 編輯、右側即時預覽及上方格式工具列。

截圖取自目前真實 WPF／AvalonEdit／WebView2 程式介面，使用各語言的原創範例文件，沒有疊加行銷文字或替換程式畫面。擷取器隔離偏好、復原資料及 WebView2 profile；不呼叫真實啟動授權流程、不使用私人文件。截圖不代表 Store 正式／試用授權驗收。

## 共用圖示與宣傳圖

| 素材 | 尺寸 | 欄位／用途 |
| --- | --- | --- |
| [app-icon-300.png](assets/app-icon-300.png) | 300×300 | 1:1 App tile icon |
| [hero-art-1920x1080.png](assets/optional-art/hero-art-1920x1080.png) | 1920×1080 | 選填 16:9 Super hero art；無文字及程式畫面 |
| [box-art-1080.png](assets/optional-art/box-art-1080.png) | 1080×1080 | 備用方形宣傳圖；遊戲向欄位，汗青無須填 |
| [poster-art-720x1080.png](assets/optional-art/poster-art-720x1080.png) | 720×1080 | 備用直式宣傳圖；遊戲向欄位，汗青無須填 |
| `assets/msix-package/` | 23 種尺寸 | 1.0.3.0 MSIX 套件圖示副本，逐檔核對 SHA256；不當作商店截圖上傳 |
| `assets/source-brand/` | SVG、ICO | 既有竹材 M 品牌原始檔 |

無文字圖資可八語共用，各商店語言頁仍須分別選取。程式截圖請使用各語言版本。舊 1.0.2.0 ZIP 與 MSIX 保留。

## 驗證與送審

- [verification/validation.json](verification/validation.json)：八語完整性、文字長度、圖片尺寸、MSIX 語言及圖示相符檢查。
- [verification/screenshots.txt](verification/screenshots.txt)：真實程式擷取結果。
- [file-manifest.json](file-manifest.json)：素材檔案大小與 SHA256；建包時逐檔核對 ZIP 內容。
- [submission-notes.md](submission-notes.md)：runFullTrust、認證備註及實際 Store 授權驗收說明。
- [privacy-submission.md](privacy-submission.md)：隱私權原則欄位填寫方式。
- [source/README.md](source/README.md)：素材與截圖重建命令；`samples/` 收錄八語範例。

本次素材製作未修改 MSIX、安裝程式、上傳或提交商店。WACK、已安裝套件及真實 Store 授權仍待驗收。支援網址與公開政策網址未提供；本包不代填虛構網址。

## 規格依據

2026-10-04 核對 Microsoft MSIX 文件：說明最多 10,000 字元、功能最多 20 項且每項 200 字元、更新說明最多 1,500 字元；本包將簡短說明控制在 270 字元內。桌面截圖為 PNG，至少 1366×768、每張不超過 50 MB，說明不超過 200 字元。關鍵字最多 7 個詞組、每項 40 字元、合計 21 個詞。

- [商店文字與語言欄位](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)
- [截圖與圖像規格](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
- [關鍵字規格](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-additional-information)
