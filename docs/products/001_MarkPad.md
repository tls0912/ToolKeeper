# 001｜MarkPad

## 產品代號

**001**

## 產品定位

Windows 上極簡、快速、順手的 Markdown 文件閱讀與編輯工具。

核心需求不是建立知識庫，而是解決最直接的使用情境：

> 雙擊 `.md` → 直接預覽 → 需要時切換編輯 → 儲存 → 關閉。

產品必須比大型筆記／知識管理工具更快進入工作狀態。

---

## 品牌目標

**MarkPad** 不只是「另一個 Markdown Editor」。

長期目標是讓使用者形成直接聯想：

> **要開 .md？就用 MarkPad。**

理想狀態是讓產品名稱本身成為使用習慣，就像 Notepad++ 對純文字編輯的品牌記憶一樣。

---

## 核心價值

- 快速開啟 Markdown 檔案
- 閱讀體驗舒服
- 修改 Markdown 不費力
- 不要求建立 Workspace / Vault
- 不要求登入
- 不依賴雲端
- 不加入與 Markdown 編輯無關的功能

核心原則：

> **開檔就能用。**

---

## 目標售價

原則上不超過 NT$50。

預計價格區間：

- NT$39
- NT$49

最終售價依 Microsoft Store 可用價格級距決定。

---

## V1 功能

- 雙擊 `.md` 直接以 **Preview Mode** 開啟
- 多分頁
- Markdown 原始碼編輯
- 即時預覽
- 預設 Preview Mode
- Preview / Edit 單一互斥切換
- Ctrl+F
- Ctrl+S
- Light / Dark Theme
- 拖曳 `.md` 開檔
- 最近開啟
- CommonMark / GFM
- 程式碼區塊 Syntax Highlight

---

## V1 不做

- Account
- Login
- Cloud
- Sync
- Plugin System
- Graph View
- AI
- Database
- Vault
- Calendar
- Todo System
- Publish
- 協作
- 大型 Workspace / Project 系統

---

## V1.1 候選功能

### Markdown Table Editor

Markdown Table 是常見摩擦點。

目標：

- 偵測 Markdown Table
- 提供表格式 UI 編輯
- 新增／刪除 Row
- 新增／刪除 Column
- 儲存後自動產生 Markdown Table

原則：

> 不改變 Markdown 格式，只降低編輯表格的麻煩。

---

## UX 原則

### 1. 啟動

使用者雙擊：

```text
README.md
```

應直接進入 MarkPad，並以 **Preview Mode** 顯示文件。

不要先出現：

- 首頁
- Dashboard
- Workspace 選擇
- 資料夾樹
- 工具商城

### 2. 可視範圍最大化

MarkPad 的 UI 應優先把畫面空間留給文件內容。

核心配置：

```text
┌───────────────────────────────────────────────────────────────┐
│ MarkPad │ README.md │ AGENTS.md │ Notes.md        —  □  ×  │
├────┬──────────────────────────────────────────────────────────┤
│ 👁 │                                                          │
│────│                                                          │
│ 📂 │                                                          │
│ 💾 │                  Markdown Content                        │
│ 🔍 │                                                          │
│ ✕  │                                                          │
│    │                                                          │
│ ⋯  │                                                          │
└────┴──────────────────────────────────────────────────────────┘
```

整體分成三區：

- **Top = Documents**
- **Left = Actions**
- **Center = Content**

不另外建立傳統水平 Toolbar。

### 3. 頂部標題列

頂部標題列同時承擔：

- 軟體名稱：MarkPad
- 文件分頁
- Windows 最小化
- Windows 最大化／還原
- Windows 關閉

文件分頁放在標題列中，避免額外占用一整行。

分頁規則：

- 顯示檔名
- 多文件以 Tab 呈現
- 未儲存文件需有清楚狀態標記
- Tab 可保留關閉按鈕，但不作為主要關閉方式

### 4. 左側直立工具列

左側工具列放置高頻文件操作。

V1 預計包含：

1. **Preview / Edit Toggle**
2. **Open**
3. **Save**
4. **Search**
5. **Close Current Document**
6. **Font / 字型（Aa）**
7. **Light / Dark Theme Toggle**
8. **Lan / UI Language**
9. **More / 其他低頻功能**

關閉文件即使 Tab 已有 `×` 仍保留左側按鈕，因為 Tab 關閉按鈕點擊範圍小，不應成為唯一關閉方式。

### 5. Preview / Edit 為互斥模式

Preview 與 Edit 不使用兩顆獨立按鈕。

使用單一 Toggle：

```text
👁 Preview
   ↓ click
✎ Edit
   ↓ click
👁 Preview
```

兩者為互斥狀態：

- **👁 = Preview Mode**
- **✎ = Edit Mode**

MarkPad 啟動與開檔時預設為 **Preview Mode**。

V1 不預設採用左右雙欄 Editor + Preview，避免犧牲內容可視寬度。

### 6. 字型選擇

左側工具列提供 **Aa** 按鈕作為字型入口。

原則：

- 預設不要求使用者設定，先依語系自動選擇合理字型
- 點擊 **Aa** 後以小型 Popup / Flyout 顯示可用字型
- 選擇後立即套用 Preview Mode
- 字型設定屬於全域 UI 閱讀偏好，不修改 Markdown 文件內容
- 字型選擇不可永久占用文件可視範圍

### 7. Light / Dark Theme 為互斥模式

亮版與暗版不使用兩顆獨立按鈕。

使用單一 Toggle：

```text
☀️ Light
   ↓ click
🌙 Dark
   ↓ click
☀️ Light
```

兩者為互斥狀態：

- **☀️ = Light Mode**
- **🌙 = Dark Mode**

目前使用中的 Theme 決定顯示的圖示；點擊後立即切換至另一個 Theme。

Theme 切換屬於全域 UI 設定，不影響文件內容。

### 8. UI 語系切換

左側工具列提供 **Lan** 語系入口，用於切換 MarkPad 本身的 UI 語言。

語系切換只影響：

- 按鈕文字
- Tooltip
- 選單
- 對話框
- 系統提示

不修改 Markdown 文件內容。

### 9. 左側工具列自動縮合

左側工具列平常維持窄版，只顯示 Icon。

例如：

```text
│ 👁 │
│ 📂 │
│ 💾 │
│ 🔍 │
│ ✕  │
│ Aa │
│ ☀️ │
│ Lan│
│ ⋯  │
```

滑鼠移入或需要操作時，可展開文字說明：

```text
│ ✎  Edit   │
│ 📂 Open   │
│ 💾 Save   │
│ 🔍 Search │
│ ✕  Close  │
│ Aa Font   │
│ ☀️ Theme  │
│ Lan Language │
```

滑鼠離開後自動縮回。

目標是兼顧：

- 新使用者可理解
- 熟悉後保持最大內容空間
- 不讓功能名稱永久占用畫面

### 10. 操作

使用者不需要閱讀說明書就應該知道如何：

- 打開
- 閱讀
- 切換編輯
- 儲存
- 搜尋
- 關閉目前文件

---


## V1 詳細行為規格

### Preview Mode

Preview Mode 是 MarkPad 的預設模式，也是產品第一印象的核心。

規格原則：

- 文件內容置中顯示
- 內容最大寬度限制，避免超寬螢幕造成行長過長
- **Preview 內容最大寬度：930 px**
- 930 px 的設計基準為 1920 px 螢幕扣除左側工具列後，可容納兩個接近 A4 閱讀區並保留少量留白
- 預設字體依 UI／文件語系自動選擇：英文優先 Segoe UI、繁體中文優先微軟正黑體、日文優先 Yu Gothic
- 字體、行高、段落間距需優先考慮長時間閱讀舒適度
- H1～H6 必須有清楚層級
- Code Block 必須清楚區隔並支援 Syntax Highlight
- Table 必須保持可讀性
- 圖片依內容區寬度自適應，不超出可視範圍
- Link 可直接點擊開啟
- 捲軸應盡量採 Overlay / 自動淡出方式，降低固定 UI 占用

Preview Mode 不顯示 Markdown 原始語法，重點是閱讀。

### Edit Mode

Edit Mode 顯示 Markdown 原始碼。

V1 原則：

- 使用等寬字體
- 支援 Markdown Syntax Highlight
- 支援行號
- 支援自動縮排
- Tab / Shift+Tab 可進行縮排與反縮排
- 支援 Undo / Redo
- 編輯內容變更後更新內部 Preview Render 結果
- 切回 Preview Mode 時立即顯示最新內容

Edit Mode 不預設與 Preview 並排。

### 文件分頁行為

- 多個 `.md` 文件預設在同一個 MarkPad 視窗內以 Tab 開啟
- 已經開啟的檔案再次被要求開啟時，切換至既有 Tab，不重複建立
- 未儲存文件需有清楚狀態標記
- 關閉未儲存文件時必須詢問 Save / Don't Save / Cancel
- Tab 過多時不可無限壓縮到無法辨識，應提供合理的 Overflow / Scroll 行為
- `Ctrl+Tab` 切換下一個文件
- `Ctrl+Shift+Tab` 切換上一個文件

### 檔案行為

- 預設支援 UTF-8
- 必須正確處理 UTF-8 BOM
- 儲存時原則上保留原檔 Encoding 狀態，避免無意義改寫
- 檔案被外部程式修改時，MarkPad 應提示使用者重新載入或保留目前內容
- 檔案被刪除或搬移時應提示狀態，不可靜默失敗
- Read-only 檔案應顯示唯讀狀態
- 超大 Markdown 檔案不得造成 UI 長時間無回應；必要時採延遲 Render 或降級行為

### V1 快捷鍵

| 快捷鍵 | 功能 |
|---|---|
| `Ctrl+O` | Open |
| `Ctrl+S` | Save |
| `Ctrl+F` | Search |
| `Ctrl+W` | Close Current Document |
| `Ctrl+Tab` | Next Tab |
| `Ctrl+Shift+Tab` | Previous Tab |
| `Ctrl+E` | Toggle Preview / Edit |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `F11` | Full Screen |

快捷鍵原則：

- 優先沿用 Windows 使用者既有習慣
- 不建立大量自創快捷鍵
- 高頻操作必須可以不碰滑鼠完成

### V1 UI 語系

第一版至少支援：

- English
- 繁體中文
- 日本語

UI 語系只影響 MarkPad 本身，不修改文件內容。

---

## 技術方向

暫定：

- .NET
- WPF
- AvalonEdit
- Markdig
- WebView2

資料流程：

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

視窗層預計使用 WPF 自訂 Title Bar / WindowChrome，以保留 Windows 視窗行為的同時，將標題列空間用於文件分頁。

---

## 架構原則

本產品是低價微型工具，不採大型企業架構。

避免：

- 過度 Clean Architecture
- 多餘 Project
- 為未來假設提前抽象
- 大量 Interface / Factory / Provider
- 不必要的 DI 層級
- 為「可能有一天會用到」建立擴充點

原則：

> **先把產品做好，再讓真正出現的需求決定抽象。**

---

## ToolKeeper 整合

本產品是獨立 Microsoft Store App。

ToolKeeper 本身不包含 Markdown 功能。

ToolKeeper 的行為：

```text
MarkPad
   ↓
URI Handler 可用？
   │
   ├─ Yes → 啟動 MarkPad
   └─ No  → 開啟 Microsoft Store 商品頁
```

MarkPad 應註冊自己的 URI Protocol。

暫定：

```text
toolkeeper-markpad:
```

Protocol 名稱在正式上架前可以調整。

---

## 成功條件

V1 不以功能數量衡量成功。

優先觀察：

- 是否可以數秒內開啟並開始使用
- 是否比現有 Markdown 工具更順手
- 是否把最大畫面空間留給文件內容
- Preview / Edit 切換是否直覺
- 是否有使用者願意付低價購買
- Store 評價與實際使用回饋
- 是否需要大量客服／維護

如果使用者的感受是：

> 「就這樣？但真的比較好用。」

這就是正確方向。

---

## 文件編號規則

本文件為產品 **001**。

後續產品文件固定使用三位數流水號：

```text
001_MarkPad.md
002_...
003_...
004_...
```

編號一旦建立，不因產品重新命名、暫停或下架而重新排序。
