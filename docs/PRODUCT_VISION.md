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

ToolKeeper 本體**不是工具盒本身**。

它不：

- 內建其他工具功能
- 下載模組
- 管理授權
- 進行內購
- 自己安裝工具
- 承載 Markdown / JSON / Diff 等功能

ToolKeeper 的角色只有兩個：

1. **工具目錄**
2. **工具 Launcher**

概念：

```text
ToolKeeper
│
├─ Markdown Editor
│   ├─ 已安裝 → 直接啟動
│   └─ 未安裝 → 開啟 Microsoft Store 商品頁
│
├─ JSON Viewer
│   ├─ 已安裝 → 直接啟動
│   └─ 未安裝 → 開啟 Microsoft Store 商品頁
│
├─ Quick Diff
│   └─ ...
│
└─ Hash Checker
    └─ ...
```

因此每個工具都是完全獨立的 Windows App。

---

## 5. Standalone App + ToolKeeper 雙軌策略

每一個工具都可以單獨上架 Microsoft Store。

例如：

```text
Microsoft Store
│
├─ ToolKeeper          免費
├─ Markdown Editor     付費
├─ JSON Viewer         付費
├─ Quick Diff          付費
├─ Hash Checker        付費
└─ Batch Rename        付費
```

ToolKeeper 只是把這些工具串在一起。

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

---

## 6. ToolKeeper 啟動工具的方式

每個獨立工具註冊自己的 URI Protocol。

例如：

```text
toolkeeper-markdown:
toolkeeper-json:
toolkeeper-diff:
toolkeeper-hash:
```

ToolKeeper 點擊某項工具時：

```text
點擊工具
   ↓
檢查 URI 是否有 Handler
   ↓
┌───────────────┐
│               │
有              沒有
│               │
直接啟動 App     開啟 Microsoft Store
```

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

目前可考慮：

- JSON Viewer / Formatter
- XML Viewer
- CSV Viewer
- Quick Diff
- Hash Checker
- Batch Rename
- Folder Size
- Clipboard Cleaner / Plain Paste
- Image → ICO
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
