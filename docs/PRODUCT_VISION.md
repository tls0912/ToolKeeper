# ToolKeeper｜工具番

## 1. 專案定位

**ToolKeeper（工具番）** 是一個 Windows 小工具品牌與 Launcher。

核心目標不是製作大型軟體，而是持續推出：

- 小而專一
- 開啟就能用
- 幾乎不需要學習
- 價格低
- 維護成本低
- 能明顯降低工作摩擦

的 Windows 小工具。

產品核心價值不是「功能很多」，而是：

> **讓原本很煩的小事，變得順手。**

很多工作上的低效率不是因為缺少功能，而是：

- 多點幾下
- 多切幾次視窗
- 多等幾秒
- 多做幾次複製貼上
- 每天重複忍受同一個小麻煩

單次看起來都不嚴重，但長期會持續消耗時間與情緒。

ToolKeeper 的產品哲學，就是把這些摩擦一個一個拿掉。

---

## 2. 品牌名稱

### 英文名稱

**ToolKeeper**

### 中文名稱

**工具番**

「工具番」的命名概念來自日文「番」所帶有的管理、看守、值班之意，也帶一點《倉庫番》式的復古電腦文化趣味。

但品牌視覺、Logo、UI、商品宣傳不模仿《倉庫番》的：

- 兔子
- 箱子
- 倉庫
- 字體
- 遊戲畫面
- 視覺風格

「工具番」只保留俏皮的語感與工具管理概念。

### 2.1 軟體標題與副標題（全域規則）

ToolKeeper 旗下所有軟體（包含 ToolKeeper 本體、既有產品與未來新增產品）都必須具備主標題與副標題，並在視窗標題列中顯示，統一格式為：

```text
MainName - SubName
```

- `MainName`：軟體的正式產品名稱。
- `SubName`：產品副標題，可為用途說明或品牌中文名稱，須於各產品規格中明確定義。
- 分隔符號固定使用半形連字號 `-`，左右各一個半形空格。
- 原生或自訂標題列都必須顯示完整主標題與副標題；視窗的 `Title` 亦使用相同文字。
- 此規則自 2026-09-28 起適用；既有產品規格若僅要求顯示產品名稱，應依此規則更新。

已定義名稱的範例：

- `ToolKeeper - 工具番`
- `汗青 - Markdown Writer`
- `CabiDock - Desktop Organizer`
- `ConvAnvil - Text, Encoding & Byte Converter`

### 2.2 全系列共用介面與產品布局

2026-10-01 起，ToolKeeper 本體與 001–005 共用 `ToolKeeper.UI` 的介面骨架，以汗青既有的自訂視窗外觀為基準。汗青也是共用介面的使用者；產品之間的差異由布局和功能內容表達。

- 共用外框、標題列、產品圖示、最小化／最大化／關閉操作、主題材質、介面字型及基本控制項。
- 標題列統一顯示 `MainName - SubName`，並與系統取得的 `Window.Title` 一致；不再額外放置一個 30 DIP 的重複產品大標題。
- 一般工具在共用骨架中提供簡介、偏好入口、可選操作及工作區；汗青在同一骨架中提供文件分頁、側欄與編輯／預覽布局。
- 風格、語言與關於採用共用元件；各產品依布局安排入口。汗青的閱讀／編輯字型及文件專用功能仍由汗青管理。
- 共用介面不持有產品資料或跨產品全域設定。各產品保留既有設定路徑、同步範圍、未儲存狀態、關閉確認及系統匣行為。
- CabiDock 嵌入桌面的群組保留群組互動布局，使用共用材質與元件；不加上一般應用程式的視窗操作列。
- 後續外觀與互動修正優先修改共用來源；產品不再各自複製一份標題列或材質實作。

本體主標題為 `ToolKeeper`、副標題為 `工具番`；002 為 `CabiDock` 與 `Desktop Organizer`；003 為 `ConvAnvil` 與 `Text, Encoding & Byte Converter`。實作邊界見[架構說明](ARCHITECTURE.md#2-依實際需求抽取共用介面)。

### 2.3 全系列竹子風格

2026-10-01 起，竹子風格以 001 汗青的竹材外觀為全系列基準，適用於 ToolKeeper 本體、001 汗青、002 CabiDock、003 ConvAnvil、004 Hash Checker、005 Image → ICO，以及後續新增的產品。選擇竹子風格時，介面必須呈現可辨識的竹材紋理與竹節，不能僅更換為綠色或米色調色盤。

- **竹子（亮色）**：沿用汗青的淺色去皮竹材、纖維與淡竹節，搭配淺色紙紋工作面。
- **竹子（深色）**：沿用汗青的深綠竹皮、縱向纖維與明顯竹節，搭配深色紙紋工作面。
- 視窗背景、側緣及外框使用縱向竹紋；內容標題區與工具列使用橫向竹紋。工作區底面與功能卡片使用低對比紙紋，輸入框與表格內層可保留配套純色以維持內容可讀性；按鈕、選單與狀態色需配合主題。
- CabiDock 的桌面群組與群組預覽也遵守相同材質規則，包含一般分類及「工具番」群組的標題與外框。
- 紋理以固定 DIP 尺寸拼貼，視窗縮放、調整大小或群組展開時不拉伸竹節與纖維。
- 一般亮色、深色與跟隨系統風格維持純色，不顯示竹紋或紙紋。切換風格不重建工作內容，也不清除正在操作的輸入與結果。

共用介面、竹紋與紙紋由 `ToolKeeper.UI` 統一維護；汗青保留分頁與編輯器布局，各產品仍保留既有設定位置與同步範圍。統一介面骨架不要求各工具採用相同功能配置。接入方式見 [ToolKeeper.UI 開發指南](../src/ToolKeeper.UI/README.md)。

---

## 3. 商業模式

### 3.1 價格策略

所有工具原則上維持低價。

目標售價：

- NT$29
- NT$39
- NT$49

**原則上不超過 NT$50。**

產品不靠單一高價軟體獲利，而是建立大量低價、低維護、長尾型產品。

核心模式：

> **多產品、小投入、快速驗證、長期累積。**

單一產品不要求成為主力收入來源。

真正目標是建立一個產品組合。

---

## 4. ToolKeeper 本體的角色

ToolKeeper 本體以免費工具目錄、Launcher 與平台入口為主，另外提供少量免費小工具。自 2026-09-28 起，002 CabiDock、004 Hash Checker、005 Image → ICO 在 `ToolKeeper.exe` 內執行，各有獨立工具視窗；**只有單獨上架的產品才有自己的 EXE**，目前為 001 汗青與 003 ConvAnvil。工具有自己的編號、入口與視窗，不等於需要獨立程序。

ToolKeeper 本體負責：

- 集中展示工具番產品，讓使用者瀏覽與發現工具。
- 啟動已安裝的獨立工具；尚未安裝時開啟 Microsoft Store 商品頁。
- 提供少量免費、範圍明確的小工具功能。
- 載入並管理 CabiDock 桌面模組，提供桌面分類、桌面群組與 Explorer 整合。
- 在桌面提供「工具番」專屬群組，集中呈現已安裝或可啟動的工具番產品。

ToolKeeper 不負責下載模組、管理授權、內購或自行安裝獨立工具；安裝與更新由 Microsoft Store 處理。

**2026-09-28 整併實作：** `ToolKeeper.exe` 已引用 `ToolKeeper.Desktop`，由本體管理桌面模組、各工具視窗與系統匣。主視窗只保留工具列表，桌面設定由 002 列表入口或系統匣開啟。產品目錄與「工具番」桌面群組列出 001 至 005，以 `toolkeeper://run/<三位產品編號>` 統一派發。002 開啟桌面設定；004、005 各自開啟 Hash Checker 與 Image → ICO 視窗。CabiDock 的開發薄殼 EXE 已退役。整併後的實機驗收仍待完成，細節見 [本體實作報告](TOOLKEEPER-IMPLEMENTATION.md)。

### ToolKeeper 本體的免費小工具

ToolKeeper 本體免費提供以下兩項小工具：

- **005 — Image → ICO**：將 JPG、BMP 或 PNG 圖片拖入介面，在原圖片所在資料夾產生 ICO 檔。
- **004 — Hash Checker**：計算 MD5、SHA-1、SHA-256；可比對雜湊值並驗證檔案。

**JSON Viewer 已歸入 003 — ConvAnvil**，不列為 ToolKeeper 本體內建功能。其他獨立工具仍各自是 Windows App。

### CabiDock 桌面模組

002 CabiDock 的定位調整為 ToolKeeper 的桌面能力模組。使用者端以 `ToolKeeper.exe` 作為單一主入口；ToolKeeper 啟動後，可依設定啟動 CabiDock 的桌面掃描、分類、監看、Explorer 接管、桌面群組與恢復能力。

此整併採「產品合體、程式模組化」原則：

```text
ToolKeeper.exe
    │
    ├─ ToolKeeper 本體
    │   ├─ 工具目錄 / Launcher
    │   ├─ 004 Hash Checker 獨立視窗
    │   ├─ 005 Image → ICO 獨立視窗
    │   └─ 工具狀態與平台整合
    │
    └─ CabiDock / ToolKeeper.Desktop 模組
        ├─ 桌面掃描
        ├─ 分類引擎
        ├─ Desktop Watcher
        ├─ Explorer 接管
        ├─ 桌面群組
        └─ Recovery
```

CabiDock 的桌面程式碼仍保持清楚模組邊界，不直接散落於 ToolKeeper 主視窗。002／004／005 由宿主開啟各自視窗；汗青、ConvAnvil 與未來單獨上架的產品才保留自己的 App EXE。

目前已抽出 `ToolKeeper.Desktop` 類別庫，沿用 `CabiDock` 命名空間與原有桌面資料位置。首次使用與無平台偏好的既有使用者預設啟用桌面功能，之後保存啟停狀態；平台偏好損毀且無健康備份時預設停用並保留原檔。主視窗關閉或最小化會隱藏至系統匣，設定視窗關閉只隱藏；系統匣退出與登出會停止整合並恢復原生桌面。恢復助手可以是由主程式啟動的獨立短用途程序，並非第二個常駐主入口。

---

## 5. Standalone App + ToolKeeper 雙軌策略

獨立產品可以各自上架 Microsoft Store，與免費的 ToolKeeper 本體並行。

例如：

```text
Microsoft Store
│
├─ ToolKeeper          免費（工具目錄、Launcher 與免費小工具）
├─ MarkPad             獨立產品
├─ ConvAnvil           獨立產品
└─ 其他工具            依個別產品規劃
```

ToolKeeper 本體提供目錄、啟動入口與 002／004／005 內建工具，各工具有自己的視窗；001／003 等單獨上架的產品才獨立使用與發行，不為內建工具另建 EXE。

這樣做的好處：

### 獨立 App

負責：

- Microsoft Store 搜尋曝光
- 直接購買
- 最低使用摩擦
- 單一功能體驗

### ToolKeeper

負責：

- 品牌集合
- 工具導覽
- 已安裝工具快速啟動
- 未安裝工具導向 Microsoft Store
- 讓既有使用者發現其他工具
- 提供上方列出的免費小工具：Image → ICO 與 Hash Checker

---

### 5.1 工具番桌面群組

ToolKeeper 透過 CabiDock 桌面模組提供一個特殊的 **「工具番」桌面群組**。

此群組不是以副檔名分類桌面檔案，而是由 ToolKeeper 的產品目錄與安裝／啟動狀態驅動：

```text
工具番
├─ 001 汗青
├─ 002 CabiDock
├─ 003 ConvAnvil
├─ 004 Hash Checker
├─ 005 Image → ICO
└─ ...
```

本輪已實作的行為：

- 群組使用與主視窗相同的產品目錄，列出 001 汗青、002 CabiDock、003 ConvAnvil、004 Hash Checker、005 Image → ICO。002／004／005 為宿主內的視窗；001／003 隨安裝或移除更新可用狀態。
- 每個入口使用自己的 `toolkeeper://run/001` 至 `toolkeeper://run/005`，由 ToolKeeper 判斷開啟宿主內工具視窗或獨立產品。
- 工具不可用或尚未安裝時，有正式 Store ID 才顯示「取得」，否則顯示停用的「未提供」。
- 不要求使用者先建立傳統桌面捷徑，也不需要靠一般 CabiDock 副檔名規則辨識工具番產品。
- 未來產品數量增加時，桌面仍維持單一清楚的工具番群組，而不是散落大量捷徑。

狀態每十秒、主視窗重新啟用與系統匣開啟時刷新；點擊入口時再次確認。群組本身已接入桌面與預覽，固定以大的展開模式顯示，不因點標題、滑鼠離開或展開其他分類而收合；可拖移、調整大小與保存，並限制在主螢幕。一般七分類維持原本收合規則。完整實機互動仍列入整併驗收。

因此，CabiDock 對 ToolKeeper 的角色不只是「桌面檔案分類器」，而是 ToolKeeper 的 **Desktop Experience / Desktop Layer**。

## 6. ToolKeeper 啟動工具的方式

所有使用者入口先使用 `toolkeeper://run/<三位產品編號>`，目前涵蓋 `001` 至 `005`，由 ToolKeeper 本體驗證並派發。002／004／005 在同一宿主開啟各自視窗，不建立新工具程序。001／003 才進入下列獨立產品啟動判斷；產品自己的 protocol 是派發後的啟動方式，不取代工具番入口。

宿主派發到獨立產品時，產品 protocol 例如：

```text
toolkeeper-<product>:
```

ToolKeeper 點擊某項獨立產品時重新偵測，依序選擇：

```text
已知 URI Handler 可用 → 開啟 App
   否則
已知本機路徑有完整 App → 開啟 App
   否則
已有正式 Store 商品 ID → 開啟 Microsoft Store 商品頁
   否則
顯示「未提供」，停用入口
```

目前汗青沿用 `toolkeeper-markpad:` 與既有正式 Store ID `9NHF764PXW9C`；ConvAnvil 尚無正式 protocol／Store ID，僅提供可確認的本機版本啟動。偵測不搜尋任意磁碟、不自動安裝、不替產品註冊 protocol，也不建立推測的商店網址。

ToolKeeper 不需要管理：

- License
- DRM
- 付款
- 更新
- 安裝
- 登入

這些全部交給 Microsoft Store 與各獨立 App。

---

## 7. 產品選題規則

一個題目適合做成工具番產品，最好符合以下條件：

1. 問題會重複出現
2. 現有解法不是不能用，而是不好用
3. 功能可以一句話說明
4. 開啟後數秒內可以開始工作
5. 不需要登入
6. 不需要雲端
7. 不需要教學最好
8. 離線優先
9. 後端能不要就不要
10. 使用者第一次使用就能感覺「這比較順」

不追求 Engagement。

工具的目標是：

> **幫使用者更快把事情做完，然後關掉程式。**

---

## 8. 開發原則

### 8.1 小

每一個工具只處理一件事情。

不要因為「順便」而持續加功能。

### 8.2 快

第一版產品開發時間應有限制。

建議：

- Idea 評估：2 小時以內
- Prototype：1 天
- MVP：3 天左右
- Polish / Store：1～2 天

理想狀態：

> **一週完成一個，最多兩週。**

若兩週仍無法完成，優先：

1. 砍功能
2. 縮小產品
3. 放棄題目

### 8.3 低維護

優先避免：

- Account
- Login
- Cloud
- Backend
- Subscription
- Multiplayer
- Sync
- 長期客服負擔

### 8.4 不過度架構

低價微型產品不需要大型企業架構。

原則：

> **用最少的結構完成穩定、可維護的程式。**

避免為了「架構漂亮」把 NT$49 的軟體做成企業級系統。

---

## 9. 第一款產品：Markdown Reader / Editor

第一款工具預定為：

**Windows Markdown Reader / Editor**

產品出發點：

> 目前很多 Markdown 工具功能很多，但單純「打開、閱讀、修改一個 .md 檔案」的體驗不夠順。

第一版定位：

> **就是一個很好用的 Markdown 閱讀與編輯器。**

不是：

- 知識庫
- 第二大腦
- Vault
- Wiki
- Plugin 平台
- AI 平台
- 專案管理工具

### V1 預計功能

- 雙擊 `.md` 直接開啟
- 多分頁
- Markdown 原始碼編輯
- 即時預覽
- 閱讀模式
- Edit / Preview 快速切換
- Ctrl+F
- Ctrl+S
- Light / Dark
- 拖曳開檔
- 最近開啟
- CommonMark / GFM
- 程式碼區塊 Highlight

### V1 暫不做

- Account
- Cloud
- Sync
- Plugin
- Graph
- AI
- Database
- Vault
- Calendar
- Todo System
- Publish
- 協作

### V1.1 候選重點

**Markdown Table Editor**

讓使用者可以用表格 UI 編輯 Markdown Table，再自動轉回 Markdown。

目標不是增加大型功能，而是解決 Markdown 本身不好用的地方。

---

## 10. Markdown App 技術方向

暫定技術：

- .NET
- WPF
- AvalonEdit
- Markdig
- WebView2

概念：

```text
.md
 ↓
AvalonEdit
 ↓
Markdig
 ↓
HTML
 ↓
WebView2
```

第一版架構應保持小而直接。

避免：

- 過度 Clean Architecture
- 過多 Project
- 過多抽象層
- 為未來假設提前設計大量基礎建設

---

## 11. 後續工具候選

其他可考慮的獨立工具：

- XML Viewer
- CSV Viewer
- Quick Diff
- Batch Rename
- Folder Size
- Clipboard Cleaner / Plain Paste
- Wake Lock
- File Organizer
- Encoding Converter
- Log Viewer

這些不是固定 Roadmap。

任何工具只有在符合「順手、低摩擦、低維護」原則時才值得做。

---

## 12. 遊戲方向

遊戲與工具品牌分開。

ToolKeeper 不混入遊戲。

遊戲同樣遵守：

- 小
- 價格低
- 單一玩法
- 快速完成
- 不做大型遊戲

但娛樂產品與工具產品的需求不同，因此不共用同一 Launcher 品牌。

---

## 13. 最核心的產品哲學

ToolKeeper 不追求：

> 做最大的軟體。

而是追求：

> **做最好用的小工具。**

不增加工作流程。

只刪除工作流程。

工具可以很多。

但每個工具都必須保持簡單。

> **工具番可以越來越大，但每個工具本身只能保持簡單、直接、順手。**
