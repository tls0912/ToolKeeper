# Services

汗青（內部專案 MarkPad）的服務放在這裡。

目前文件與設定服務包括：

- DocumentFileService
- SettingsService
- RecoveryService
- SingleInstanceService
- AtomicFile
- LocalLog

## 啟動授權（0.1.19）

- `StoreLicenseService`：套件身分、Store 查詢、授權判定與購買商品頁。
- `StartupLicenseGate`：開始查詢、重試、購買提示及關閉流程。
- `PaidLicensePolicy`：有效正式紀錄免查詢，只有 Store 確認正式授權才保存紀錄。
- `PaidLicenseCache`：以 Windows DPAPI 的目前使用者保護紀錄，綁定套件 PFN 與 Store 商品識別，保存於本機設定資料夾下的 `licensing/`。

商店版的啟動授權服務先核對套件身分。試用版每次透過 `StoreContext.GetAppLicenseAsync()` 查詢；首次查得有效正式授權（`IsActive && !IsTrial`）後保存本機受保護紀錄，同一 Windows 使用者／電腦後續不再查詢 Store。換機、紀錄遺失或失效時重新向 Store 確認。在建立文件主視窗、復原、開檔與 IPC 轉送前完成授權處理；一般未封裝開發／可攜版略過 Store 查詢。MSIX 以 `StoreLicenseRequired=true` 編譯，所以從正式套件取出的 EXE 缺少正確身分時仍會拒絕啟動。

授權無效時提示購買並結束本次啟動；只有無效且已知過期時間已過才稱為到期。查詢失敗或逾時提供重試／關閉，不以網路錯誤推論到期。沒有本機有效正式紀錄時，離線依 Store 快取授權判定。使用者已在 Partner Center 設定 7 天免費試用，本次未登入後台查驗；程式不自行建立試用期限。購買入口只開啟 Store 商品頁，不執行交易。

此服務不處理使用途中到期。有效正式紀錄後不再查詢，應用程式不會自動偵測後續退款或授權撤銷。Store 與可攜版使用分開的 `SingleInstanceService` 識別，避免跨版本轉送。被拒絕的啟動不得建立文件、改動原稿、清除復原資料或關閉已執行中的視窗。

授權決策測試應以明確的有效／無效、試用、到期時間與錯誤輸入驗證，並檢查正式紀錄保存、免查詢與紀錄失效後重新查詢；真實 Store 帳號驗收另行使用 Store 測試發佈完成。

0.1.19 的 `MarkPad.Tests` 157／157 通過，其中新增啟動授權流程 39 項與正式紀錄／策略 21 項，後者包含 5 項真實 Windows DPAPI 隔離測試。隔離 WPF 介面檢查 96／96、正式發佈組件的授權設定 PE 檢查 5／5 通過，紀錄見 `artifacts/license-checks`。MSIX 1.0.1.0／x64 已完成建置與套件核對；尚未安裝、上傳、執行 WACK 或以真實 Store 帳號驗收。

若 Store 已確認正式授權，但本機紀錄寫入失敗，本次仍可進入；錯誤寫入本機紀錄，下一次啟動重新查詢 Store。設定重設不應清除正式授權紀錄。

DPAPI 保護的是本機使用者資料；這份紀錄不是 Microsoft Store 簽署的授權憑證，也不宣稱能防止同一使用者自行修改程式或偽造本機資料。正式授權免重查是產品選擇，其界線包括不再偵測退款與撤銷。

不要先建立 Interface / Factory；只有真的出現替換需求或測試隔離需求時再抽象。
