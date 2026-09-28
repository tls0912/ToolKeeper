# 汗青 Microsoft Store MSIX

此流程將現有 WPF 應用程式發佈成 self-contained MSIX，包含 .NET/WPF runtime、第三方授權與多尺寸圖示。套件由 Microsoft Store 在通過認證後簽章；此流程不購買憑證、不安裝或註冊應用程式，也不變更本機憑證信任。

## 目前封裝狀態（2026-09-28）

- 最新 **1.0.2.0 / x64 MSIX**：`artifacts/msix/Hanqing-1.0.2.0-x64-store/Hanqing_1.0.2.0_x64.msix`，保留 0.1.19 的啟動授權功能並封入竹材 M 新圖示。檔案大小 83,378,899 bytes（約 79.5 MiB）。正式 EXE 檔案版本為 1.0.2.0，「關於」顯示 1.0.2；開發版專案仍為 0.1.19。
- SHA256：`81396DD45383A00F2186915DA1904A89545CFBDC503098DF6F20376621226023`。
- 新包通過 MakeAppx 完整語意驗證、`Test-MarkPadMsix.ps1` 的身分／內容核對及 5 項 PE 授權設定檢查；版本、必要授權標記與商品識別正確。說明包 `products/MarkPad/assets/msix-package/` 的 23 張 PNG 已逐一核對，與新包圖示相同。
- 0.1.19 授權功能先前已通過 157 項汗青測試（含 5 項真實 Windows DPAPI 隔離測試）、96 項隔離 WPF 介面檢查及 5 項發佈組件授權設定核對；紀錄見 `artifacts/license-checks`。這些本機檢查不等同真實 Store 帳號授權驗收。
- 舊 1.0.1.0 MSIX 保留原樣，含啟動授權功能與舊圖示；本輪交付使用 1.0.2.0。
- 先前 0.1.18 原始碼已產生 `artifacts/msix/Hanqing-1.0.0.0-x64-store/Hanqing_1.0.0.0_x64.msix`，使用本機設定的 Partner Center 正式識別。該舊包未包含啟動授權檢查，不作為本輪上傳套件。
- 本次助手未安裝或上傳 1.0.2.0 新包。使用者曾回報 Partner Center 的 runFullTrust 接受度警告，實際提交進度以後台為準。WACK、安裝／更新／解除安裝、乾淨環境及真實 Store 正式／試用授權驗收仍待完成。後續正式包使用新的輸出資料夾與符合商店版本規則的版本號。

## 1. 填入 Partner Center 產品識別

建立 **MSIX / PWA** 類型產品、保留「汗青 - Markdown Writer」名稱後，開啟「產品管理 → 產品識別」。帳號註冊完成不會自動建立產品。若先前已建立 MarkPad 產品，請在同一產品新增／保留新名稱，並確認套件顯示名稱與該產品保留的名稱一致。

複製 `StoreIdentity.example.json` 為同資料夾下的 `StoreIdentity.json`，填入：

| JSON 欄位 | Partner Center 欄位 |
| --- | --- |
| `Name` | `Package/Identity/Name` |
| `Publisher` | `Package/Identity/Publisher`，完整 `CN=...` 值 |
| `PublisherDisplayName` | `Package/Properties/PublisherDisplayName` |
| `PackageFamilyName` | 套件系列名稱（PFN），供啟動時核對實際套件身分 |
| `StoreId` | Store 識別碼，供購買提示開啟對應商品頁 |

五項值均必填，必須與 Partner Center 相符。不要自行用作者暱稱代替 Publisher。範例檔刻意留空；未填寫時腳本會停止。實際設定檔已列入 `.gitignore`。目前正式產品的 `StoreId` 為 `9NHF764PXW9C`；`PackageFamilyName` 與 `StoreId` 由封裝流程傳入程式的授權設定，不取代前三項 Manifest 欄位。

若 Partner Center 複製出的發布者顯示名稱含 `&#x20;`，它代表一個空格，不應當成字面名稱。Windows Manifest 規則不接受顯示名稱首尾空白；MakeAppx 會以 `C00CE16A`／`0x80080204` 拒絕。遇到這種情況須核對 Partner Center 值後更正，不繞過驗證或由腳本偷偷刪除。本產品已依使用者確認，將發布者顯示名稱設為「不告訴你」，不含尾端空格。

更名只調整對外品牌與檔名，不根據新名稱推導或改寫 Partner Center 的 `Name`、`Publisher` 或 `PublisherDisplayName`。內部專案、封裝資料夾與腳本名稱仍為 MarkPad。

## 2. 建置

需要 Windows 與 .NET 10 SDK。在儲存庫根目錄執行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-MarkPadMsix.ps1 -IdentityFile .\packaging\MarkPad\StoreIdentity.json -PackageVersion 1.0.2.0
```

上述命令產生 **x64 / 1.0.2.0 / Release** 套件，也是腳本目前的預設架構與版本。可指定 `-Runtime win-arm64`、新的 `-PackageVersion`、`-OutputDirectory <空資料夾>`；`-MaxVersionTested` 預設為 SDK 目標 `10.0.26100.0`，應依實際支援／驗證的 Windows 版本調整。

商店版本採四段數字、第一段不得為 0，最後一段固定 0。封裝時將版本傳給 .NET publish，所以執行檔與「關於」版本和套件一致；不修改可攜版專案的開發版本。

首次建置會從 NuGet.org 還原應用程式相依套件與固定版本 `Microsoft.Windows.SDK.BuildTools` 至儲存庫 `.packages/`。只還原建置工具，不執行機器層級 SDK 安裝。已有 Windows SDK 者可用 `-SdkBinDirectory 'C:\Program Files (x86)\Windows Kits\10\bin\<版本>\x64'`。完整還原過相同架構後可用 `-NoRestore` 離線建置。

輸出位於 `artifacts/msix/Hanqing-<runtime>-<時間>/`：

- `Hanqing_1.0.2.0_x64.msix`：上傳至對應 Partner Center 產品的檔案。
- `*.sha256`：套件 SHA256。
- `PACKAGE-INFO.txt`：套件身分、執行環境需求與驗證界線。
- `Layout/`：可檢查的套件內容、`Hanqing.exe` 與已填入身分的 `AppxManifest.xml`。
- `priconfig.xml`：建置用資源索引設定，不放入套件。
- `ResourceInput/` 與 `resources.dump.xml`：圖示索引輸入與可檢查的索引內容，不放入套件。

每次使用新的或空的輸出目錄，避免混入舊檔。套件不包含可攜版的登錄腳本、私鑰、PDB、身分設定檔或開發工具。重跑圖示產生器會從既有 SVG 更新圖示；所有縮放資源收在同一個 `resources.pri`，不需要獨立資源套件。

## 3. 套件行為

- MSIX 宣告 `.md`「開啟檔案」候選，不強制修改預設程式。
- 商店與套件顯示名稱為「汗青 - Markdown Writer」，執行檔為 `Hanqing.exe`。
- `toolkeeper-markpad:` 開啟／喚起汗青，不解析 URI 中的檔案或命令；保留既有協定與 `Application Id="MarkPad"`，維持相容性。
- 可攜版仍使用 `Register-MarkPad.ps1`／`Unregister-MarkPad.ps1`，登錄中對外 `ApplicationName` 為「汗青」。既有所有權標記、ProgID、capabilities 路徑與 `RegisteredApplications` 識別維持不變，重跑登錄可將原本路徑更新至 `Hanqing.exe`。
- 檔案參數有引號保護，可處理空格與中文路徑；通過啟動授權檢查後才進行單一實例轉送。Store 版與可攜版使用分開的實例識別，避免開檔要求轉送到另一種發行版本。
- 使用 `runFullTrust` 執行 WPF。送審若要求說明，可寫明：桌面 Markdown 編輯器需要開啟／儲存使用者選擇的本機文件、處理本機圖片與匯出 PDF；不需要系統管理員權限。
- 介面語言宣告為英文、繁體中文、日文。
- 技術最低版本沿用專案的 Windows 10 build 17763。支援聲明還須符合 [.NET 10 的 OS 支援與生命週期](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)，不能只憑此數值承諾所有舊版 Windows 都受支援。

### 啟動正式／試用授權（0.1.19）

MSIX 發佈以 `StoreLicenseRequired=true` 編譯，啟動時核對套件身分。在顯示文件視窗、處理復原草稿、開啟文件及轉送至既有實例前，依以下規則處理授權：

- 試用版每次啟動透過 `StoreContext.GetAppLicenseAsync()` 查詢；Store 回傳有效試用授權時，正常進入應用程式。
- 首次查得有效正式授權（`IsActive && !IsTrial`）後，保存本機受保護的正式授權紀錄。之後同一 Windows 使用者／電腦啟動不再查詢 Store；換機、紀錄遺失或失效時，須重新向 Store 確認。
- 授權無效時，提示使用者前往 Microsoft Store 購買；選擇前往商店或取消，均結束本次啟動。只有授權無效且有明確過期日期時，才顯示「到期」；其餘顯示沒有有效授權。
- 查詢錯誤或逾時時，提供重試或關閉，不開啟編輯器，也不將錯誤當成試用到期。離線可否使用以 Store 回傳的快取授權為準，不自行延長試用天數。
- 一般未封裝的開發／可攜版不查 Store 授權；從正式 MSIX 解壓出的 EXE 仍保有必要授權設定，缺少正確套件身分時無法繼續啟動。
- 本次檢查只發生於程序啟動，沒有加入使用途中到期的儲存、購買與停用編輯流程。正式授權紀錄有效後不再查詢，因此本應用程式不會自動偵測後續退款或授權撤銷。

購買提示只開啟產品 `9NHF764PXW9C` 的 Store 頁面，不代替使用者下單。使用者已在 Partner Center 設定 **7 天免費試用**；本次未登入後台查驗。程式以 Store 授權為準，不從首次執行日期另算七天，MSIX 也不會變更商店設定。真實 Store 帳號、正式／試用／到期與離線行為仍須透過 Store 測試發佈驗收，側載簽章或單元測試無法取代此驗收。

## WebView2

**WebView2 Evergreen Runtime 不包含在此 MSIX 中。** 預覽和 PDF 匯出使用電腦上安裝的 Runtime；NuGet WebView2 SDK/Loader DLL 不是 Runtime。本流程沿用應用程式既有的缺少 Runtime 提示。送審前應在乾淨的支援 Windows 環境驗證，並在商店系統需求中註明。

不要把 `win32dependencies:ExternalDependency` 當成商店會自動安裝 WebView2 的保證：該宣告只由 App Installer 處理，其他安裝機制會忽略。若產品日後要求不依賴已安裝的 Runtime，須另外規劃 Fixed Version 的包入、授權與更新流程。

## 4. 驗證與提交

建置會執行 MakeAppx 完整語意驗證（不使用 `/nv`），並檢查最終 ZIP/MSIX 中的身分、啟動宣告、runtime、第三方授權與禁止夾帶的檔案。這不等同實際 Store 使用授權驗收。可重跑：

```powershell
.\scripts\Test-MarkPadMsix.ps1 -PackagePath '<套件完整路徑>' -IdentityFile .\packaging\MarkPad\StoreIdentity.json
```

上述檢查不等於 Microsoft Store 認證或安裝驗收。送審前仍需：

1. 使用測試環境完成安裝／升級／解除安裝，確認設定、復原草稿與使用者文件行為。
2. 從開始功能表、`.md` 開啟方式及 URI 啟動；包含中文／空格路徑、多檔與已開啟實例。測試時關閉可攜版，並另行驗證兩種版本共存情境。
3. 驗證 WebView2 預覽、PDF、IME、拖曳分頁及混合 DPI。
4. 核對使用者已設定的 7 天免費試用，透過 Store 測試發佈驗證正式授權首次確認與後續免查詢、正式紀錄遺失／失效後重查、有效試用、到期、尚未取得授權、離線快取與查詢失敗；確認拒絕啟動時不開啟文件或清除復原草稿，購買連結指向本產品。
5. 執行 Windows App Certification Kit；此工具不包含於本專案的 BuildTools 還原流程。
6. 在對應的 Partner Center **MSIX** 產品上傳套件、完成價格／試用及商店資料後提交認證。

產出的 MSIX 未簽章，不能當作已簽章的側載安裝包直接散布。若要本機安裝測試，應使用隔離的測試簽章流程或 Store 測試發佈；本建置不會自動信任自簽憑證。測試 fixture 的假身分套件僅供工具驗證，不能拿去正式提交。

## 參考

- [MSIX Store 套件與簽章要求](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [MakeAppx](https://learn.microsoft.com/en-us/windows/msix/package/create-app-package-with-makeappx-tool)
- [桌面應用程式封裝擴充](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions)
- [WebView2 部署](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution)
- [ExternalDependency 限制](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-win32dependencies-externaldependency)
- [使用 Microsoft Store 授權實作試用](https://learn.microsoft.com/en-us/windows/uwp/monetize/implement-a-trial-version-of-your-app)
