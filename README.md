# ToolKeeper｜工具番

ToolKeeper（工具番）是一個以「超好用的小工具」為核心的 Windows 軟體品牌與 Launcher。

原則：

- 小而專一
- 開啟就能用
- 不增加工作流程，只刪除工作流程
- 低價、低維護、離線優先
- 每個工具都是獨立 App

## Products

### 001 — MarkPad

Markdown 文件閱讀與編輯工具。

- 產品規格：[`docs/products/001_MarkPad.md`](docs/products/001_MarkPad.md)
- 程式碼：[`src/MarkPad`](src/MarkPad)

目前已有可執行的 Windows 開發版：包含多分頁閱讀／編輯、搜尋取代、主題與三語 UI、檔案保護與崩潰復原，以及先儲存原稿、再於旁邊產生附有時間戳記 PDF 的匯出功能。

```powershell
dotnet run --project src/MarkPad/MarkPad.csproj -- ".\README.md"
```

建置、測試、可攜版打包與選用的 Windows 檔案關聯，見 [開發說明](docs/DEVELOPMENT.md)。
已實作範圍、驗證結果與待驗收項目，見 [實作狀態](docs/IMPLEMENTATION.md)。

### 002 — CabiDock

依類型與檔名關鍵字自動分類桌面；群組平時收合，點擊展開、滑鼠離開收合，保留檔案原始位置。

- 產品規格：[`docs/products/002_CabiDock.md`](docs/products/002_CabiDock.md)
- 程式碼：[`src/CabiDock`](src/CabiDock)
- 狀態：已建立可執行的分類與群組操作預覽；正式桌面整合尚未啟用

目前可使用七個預設分類、自訂副檔名與關鍵字規則、分類保存、桌面監看、群組拖曳與系統匣。群組顯示於獨立預覽視窗，保留原生桌面圖示；尚未完成 V1 的桌面接管驗收。

```powershell
dotnet run --project src/CabiDock/CabiDock.csproj
```

啟動與驗證方式見 [CabiDock 實作狀態](docs/CABIDOCK-IMPLEMENTATION.md)，桌面整合限制見 [桌面原型驗證](docs/CABIDOCK-DESKTOP.md)。

## Documents

- [Product Vision](docs/PRODUCT_VISION.md)
- [Architecture](docs/ARCHITECTURE.md)

> 工具番可以越來越大，但每個工具本身保持簡單、直接、順手。
