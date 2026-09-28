# 汗青 Microsoft Store 送審備註

供發行者與認證人員使用，不要整份貼到產品 Description。對外名稱統一為「汗青 - Markdown Writer」；Store 識別碼 `9NHF764PXW9C`，MSIX 版本 `1.0.2.0 / x64`，開發版 `0.1.19`，正式程式「關於」顯示 `1.0.2`。

四段帶皮青綠竹材組成的 M 保留藍底與短線；程式圖示、商店素材與 23 張套件 PNG 共用 `scripts/MarkPadIconRenderer.cs` 向量來源，已封入 `Hanqing_1.0.2.0_x64.msix`。`assets/msix-package/` 是新包 23 張 PNG 的逐檔副本，SHA256 核對一致；三語共 15 張截圖使用更新後的程式介面。舊 1.0.1.0 包保留原樣。

## 三語文案欄位

`listing/zh-TW`、`listing/en-US`、`listing/ja-JP` 的文案內容對齊，均為可直接貼入的純文字。

| 檔案 | Partner Center 欄位 | 用法 |
| --- | --- | --- |
| description.txt | Description | 整段貼入 |
| short-description.txt | Short description | 整段貼入 |
| features.txt | Product features | 每行一項，分別貼入，不加項目符號 |
| keywords.txt | Keywords／Search terms | 每行一個詞組，分別貼入 |
| privacy-policy.txt | 提供隱私權原則文字 | 三語全文，依發行者要求不列聯絡 Email；使用目前表單的文字選項 |
| release-notes-update-only.txt | What's new in this version | 僅供後續更新參考；首次上架留白，之後核對該次實際變更再使用 |

本包按描述 ≤10,000 字元、簡短描述 ≤270 字元、每項功能 ≤200 字元且最多 20 項、更新說明 ≤1,500 字元檢查。官方要求首次提交的「What's new」留白；簡短描述上限為 1,000 字元，建議控制在 270 字元內。[Microsoft Learn：MSIX 商店資訊](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/add-and-edit-store-listing-info)

每語言提供 7 項關鍵字，每項少於 30 字元，英文分詞不超過 21 個不重複詞，亦低於每項 40 字元的檢查門檻。實際填寫仍以目前 Partner Center 的欄位驗證為準。

收費文案為 7 天免費試用、正式版一次購買。使用者已表示在 Partner Center 設定好 7 天免費試用，本次未登入後台查驗。售價、幣別、市場及試用資格依 Store 設定；文案未代填固定價格。

## runFullTrust：英文可直接貼入

```text
汗青 - Markdown Writer is a Windows desktop Markdown reader and editor built with WPF. It uses runFullTrust to run its desktop executable and to open, edit, and save local documents selected by the user. It also reads local images referenced by those documents, stores local preferences and recovery drafts, and exports PDF files beside the source documents. The app does not require administrator privileges. Document processing and preview are performed locally. Microsoft Store services are used for app licensing and the optional purchase-page link.
```

## 認證備註：英文可貼入

提交前請核對此段中的版本與試用設定；以下版本對應已封入竹材 M 圖示的 1.0.2.0 新包。

```text
Product: 汗青 - Markdown Writer
Store product ID: 9NHF764PXW9C
Package version: 1.0.2.0 (x64)

The app reads and edits local Markdown files. There is no app-specific account or sign-in. The Store build requires a valid Microsoft Store trial or full app license. The publisher has configured a 7-day free trial; Microsoft Store controls the trial period.

Install the Store-associated package and launch with an active trial or full license. Open a local .md file. Reading mode lists H1-H6 headings on the left; selecting a heading jumps to that section. Press Ctrl+E to edit on the left with a live preview on the right. Edit and save, then use PDF export: the app saves the Markdown source before creating a timestamped PDF beside it.

Trial licenses are queried at each process launch. A full purchase, once confirmed by Microsoft Store, is remembered for the current Windows user and computer. Subsequent launches do not query the Store again. A missing or invalid local purchase record causes a new Store check. Use separate, isolated Windows user profiles and appropriate Store entitlements for full, trial, and expired-license cases.

An expired or otherwise inactive license shows a purchase prompt. Opening the Store page or dismissing the prompt ends that launch before a document window opens. A failed or timed-out query offers retry or close. Licensing is checked at startup only; this version does not interrupt an already running editing session when a trial expires.

No license bypass or app-specific test login is provided. An executable extracted from the MSIX without its installed package identity cannot be used for this test. Preview and PDF export require Microsoft Edge WebView2 Runtime, which is not bundled in the MSIX.
```

## 首次送審前的授權驗收

以下是真實 Store 情境的待辦，並非已完成結果。使用 Store 提供的授權狀態，不以修改電腦日期代替試用到期測試。

1. 核對本產品的 7 天免費試用、一次購買價格、市場設定與套件識別。
2. 在隔離的受支援 Windows x64 環境安裝 Store 關聯套件，確認 WebView2 Runtime 可用。一般可攜版不檢查 Store 授權，不能代替此步。
3. 有效試用：啟動、開啟與儲存本機 Markdown，再關閉重開。應可進入文件介面；每次程序啟動均查詢 Store。
4. 已過期／未持有授權：使用獨立測試情境重新啟動，應提示到期或沒有有效授權；商品頁須指向 `9NHF764PXW9C`。取消或前往商店都應結束本次啟動，不開文件、不清除復原草稿。
5. 正式授權：在尚未保存正式紀錄的隔離 Windows 使用者環境首次啟動。Store 確認後可使用；同一 Windows 使用者／電腦後續啟動不再查詢。換機、紀錄遺失或失效時須重新確認。
6. 離線與錯誤：分別驗證 Store 快取授權可用、無法確認授權及查詢逾時。有本機有效正式紀錄時略過 Store；沒有時依 Store 回覆判斷，查詢錯誤提供重試／關閉，不誤稱到期。
7. 檔案啟動：測試 `.md` 開啟方式、多檔參數與重複啟動。授權未通過不能轉送文件；Store 版與可攜版各自協調實例。
8. 文件保護：授權遭拒時，既有文件與復原草稿保持原樣；另一個新啟動遭拒不會關閉已執行中的編輯視窗。

正式授權保存後不再查詢，因此應用程式不會自動偵測後續退款或撤銷。切換 Microsoft Store 帳號不等於更換 Windows 使用者；請使用獨立環境測試不同授權狀態。

0.1.19 已通過 157 項汗青測試與 96 項隔離 WPF 介面檢查；1.0.2.0 新包通過 5 項發佈組件授權設定核對、MakeAppx 與套件內容檢查。本次助手未安裝或上傳新包，也未執行 WACK 或上述真實 Store 帳號驗收。使用者已回報 Partner Center 的 runFullTrust 接受度警告，實際提交進度請以後台顯示為準。

## 發行者尚須提供的資料

| 項目 | 目前狀態 | 送出前需完成 |
| --- | --- | --- |
| 支援 URL | 尚未提供 | 提供公開可存取、由發行者維護的實際支援網址，確認聯絡方式可用 |
| 隱私權原則 | 已備三語文字，不列聯絡 Email | 依目前表單選「提供隱私權原則文字」並貼入全文；若改選 URL，才需提供實際公開政策網址 |
| 產品網站 URL | 未提供，可依表單需要選填 | 僅填實際存在且由發行者管理的網站 |
| 最終售價與市場 | 本文案未代填 | 核對 Partner Center；規格中的價格構想不代表已設定售價 |

本包未虛構網址或聯絡方式。請使用各項對應欄位，不要將待辦文字或網址貼入 Description。隱私權政策另見各語言的 `privacy-policy.txt` 及 [填寫說明](privacy-submission.md)；本篇認證備註本身不是隱私權政策。

## 環境與文件入口

- 本次套件為 x64。Windows 支援範圍須配合實際驗收與 .NET 10 支援版本確認，不以 Manifest 最低 build 直接承諾所有舊版 Windows。
- WebView2 Runtime 為預覽與 PDF 的必要條件，已在三語產品說明揭露。
- 規格與實作狀態：`docs/products/001_MarkPad.md`、`docs/IMPLEMENTATION.md`。
- 套件與限制：`packaging/MarkPad/README.md`。
