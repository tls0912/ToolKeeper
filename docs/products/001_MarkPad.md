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
6. **Window Size / 視窗尺寸（↔）**
7. **Font / 字型（Aa）**
8. **Light / Dark Theme Toggle**
9. **Lan / UI Language**
10. **More / 其他低頻功能**

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

### 6. 視窗尺寸調整

左側工具列提供 **↔** 視窗尺寸入口。

原則：

- MarkPad 預設開啟寬度為 **900 px**
- 900 px 為建議閱讀寬度，不是最大限制
- 使用者仍可直接拖曳視窗邊框自由調整
- 點擊 **↔** 後可快速選擇常用視窗寬度／尺寸
- 尺寸調整不可永久占用文件可視範圍
- 後續可考慮提供「恢復 900 px 預設寬度」快速動作

### 7. 字型選擇

左側工具列提供 **Aa** 按鈕作為字型入口。

原則：

- 預設不要求使用者設定，先依語系自動選擇合理字型
- 點擊 **Aa** 後以小型 Popup / Flyout 顯示可用字型
- 選擇後立即套用 Preview Mode
- 字型設定屬於全域 UI 閱讀偏好，不修改 Markdown 文件內容
- 字型選擇不可永久占用文件可視範圍

### 8. Light / Dark Theme 為互斥模式

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

### 9. UI 語系切換

左側工具列提供 **Lan** 語系入口，用於切換 MarkPad 本身的 UI 語言。

語系切換只影響：

- 按鈕文字
- Tooltip
- 選單
- 對話框
- 系統提示

不修改 Markdown 文件內容。

### 10. 左側工具列自動縮合

左側工具列平常維持窄版，只顯示 Icon。

例如：

```text
│ 👁 │
│ 📂 │
│ 💾 │
│ 🔍 │
│ ✕  │
│ ↔  │
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
│ ↔  Size   │
│ Aa Font   │
│ ☀️ Theme  │
│ Lan Language │
```

滑鼠離開後自動縮回。

目標是兼顧：

- 新使用者可理解
- 熟悉後保持最大內容空間
- 不讓功能名稱永久占用畫面

### 11. 操作

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
- **MarkPad 預設開啟視窗寬度：900 px**
- 900 px 的設計基準為 1920 px 螢幕扣除左側工具列後，可並排兩個接近 A4 閱讀寬度的 MarkPad 視窗並保留少量留白
- 900 px 是預設值，不是最大限制；使用者可自由調整視窗大小
- Preview 內容左右留白採 **自適應**，但最少 **24 px**
- Preview 內容上下留白：**32 px**
- 文件內容區保留 **1 px 細框線 + 4 px 小圓角 + 無陰影**
- Light / Dark 均使用中性框線色，讓兩個文件並排時能清楚辨識邊界
- 預設字體依 UI／文件語系自動選擇：英文優先 Segoe UI、繁體中文優先微軟正黑體、日文優先 Yu Gothic
- **Preview 本文預設字級：16 px**
- **Preview 本文預設行高：1.6**
- **Preview 段落間距：0.8em**
- 字體、行高、段落間距需優先考慮長時間閱讀舒適度
- H1～H6 採 **GitHub 類似風格**：H1/H2 有分隔線，其餘主要靠字級與字重區分
- Code Block 採 **GitHub 類似風格**：淺灰／深灰底、小圓角、適度 padding，並支援 Syntax Highlight
- Inline Code 採 **GitHub 類似風格**：淡灰／深灰底、小圓角、等寬字體
- Table 採 **GitHub 類似風格**：細框線、Header 淡底色、整體清楚緊湊
- Blockquote 採 **GitHub 類似風格**：左側 4 px 灰色直線，文字稍淡
- 一般清單採 **稍微寬鬆** 的項目間距
- Task List 在 Preview Mode 顯示可互動 Checkbox；勾選狀態直接同步修改 Markdown 的 `[ ] / [x]`，並將文件標記為未儲存
- 水平分隔線 `---` 使用 **1 px 淡灰線 + 較大的上下留白**
- 圖片：小圖維持原尺寸，大圖自動縮至內容區最大寬度；點擊圖片可放大檢視
- 內部 `.md` 連結在 MarkPad 新 Tab 開啟
- 外部網址交給系統預設瀏覽器
- 一般連結樣式採 GitHub 類似的藍色連結
- Scrollbar 採一般可操作寬度；平常淡化，Hover 或捲動時變明顯
- Light / Dark 整體配色採 **GitHub Light / Dark 類似風格**
- Preview 背景與整體 Theme 維持一致，不額外製作紙張卡片陰影

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


## V1 搜尋與取代規格

### Search UI

- `Ctrl+F` 或左側 Search 按鈕叫出搜尋浮窗
- 搜尋浮窗固定在文件內容區左上角
- 不可拖曳
- 寬度：**320 px**
- 圓角：**4 px**
- 完全不透明
- 使用 **1 px 邊框 + 很淡的陰影**
- 不占固定版面，不把文件內容往下推
- 搜尋框有文字時顯示清除 `×` 按鈕
- 點擊清除後只清空搜尋文字與標記，不關閉浮窗

### Search 行為

- 所有命中結果淡色標記
- 目前命中結果高亮
- 顯示結果位置，例如 `3 / 12`
- `F3`：下一個結果
- `Shift+F3`：上一個結果
- 預設不區分大小寫
- 提供 `Aa` 切換大小寫敏感
- 記住上次 `Aa` 狀態
- 不支援 Whole Word
- 不支援 Regex
- 關閉搜尋後保留上一次搜尋字串
- 使用 `Ctrl+F` 重新開啟時自動全選搜尋框內容
- 若使用者已反白文字再按 `Ctrl+F`：
  - 單行反白：自動帶入搜尋框
  - 多行反白：只取第一行
- Preview Mode：從目前可視位置開始搜尋
- Edit Mode：從目前游標位置開始搜尋
- 搜到尾端後自動循環回開頭，並短暫提示「已從開頭繼續搜尋」
- 反向搜尋到底時同理提示「已從結尾繼續搜尋」

### Replace

- Preview Mode 不提供 Replace
- Edit Mode 可從 Search 浮窗展開 Replace
- 支援 `Replace`
- 支援 `Replace All`
- `Replace All` 前顯示「將取代 N 處，是否繼續？」
- 不支援 Regex
- `Esc` 行為：
  - Replace 展開時：第一次先收合 Replace
  - 再按一次：關閉整個 Search 浮窗
  - 若只有 Search：直接關閉

---

## V1 Edit Mode 詳細規格

- 預設字級：**16 px**
- 使用等寬字體
- 依系統可用字體自動選擇，優先 **Cascadia Mono**
- 行號預設顯示
- 目前行的行號與整行背景輕微高亮
- 自動換行預設開啟
- Tab 寬度：**4 spaces**
- 多行選取後 `Tab / Shift+Tab` 進行整批縮排／反縮排
- 支援 Undo / Redo
- 支援自動縮排
- 自動補成對括號，但不自動補引號
- `Ctrl+1 ~ Ctrl+6` 對應 H1～H6
- 選取文字後支援 Markdown 快捷包覆：
  - `Ctrl+B` → Bold
  - `Ctrl+I` → Italic
  - Inline Code
  - Link
- 選取文字時浮出小型 Markdown 工具列，只包含：
  - Bold
  - Italic
  - Inline Code
  - Link
- 選取文字後貼上網址，自動轉為 Markdown Link
- 左側工具列底部顯示游標位置，例如 `Ln 32, Col 8`

### Preview Render

- 編輯停止輸入 **300 ms** 後更新 Preview Render
- Edit / Preview 切換時盡量同步至同一段落／位置
- 每個 Tab 各自記住 Preview 捲動位置

---

## V1 圖片輸入與圖片檢視

### 貼上／拖曳圖片

- 從剪貼簿貼圖時：
  - 自動建立或沿用文件旁的 `images` 資料夾
  - 自動儲存圖片
  - 自動插入 Markdown 圖片語法
- 圖片檔名使用時間戳，例如：
  - `image-20260921-205812.png`
- Markdown 圖片路徑一律使用相對路徑
- 拖曳圖片進 Edit Mode 時採相同行為：
  - 自動複製到 `images`
  - 自動插入 Markdown
- 已存在 `images` 資料夾時直接沿用，不再詢問

### Preview 圖片

- 小圖維持原尺寸
- 大圖自動縮至內容區最大寬度
- 點擊圖片可進入放大檢視
- 放大檢視可由以下方式關閉：
  - 點背景
  - `Esc`
  - 右上角 `×`
- 放大後支援 `Ctrl + 滾輪` 縮放
- 圖片超出視窗尺寸時才允許拖曳移動

---

## V1 文件與視窗行為

### 開啟文件

- 拖曳 `.md` 到 MarkPad：
  - 預設在目前視窗開新 Tab
  - 按住 `Shift` 時開新 MarkPad 視窗
- 雙擊另一個 `.md`：
  - 預設加入既有 MarkPad 視窗
  - 按住 `Shift` 時開新視窗
- 已開啟的同一檔案再次被要求開啟時，切換至既有 Tab，不重複建立

### 無文件啟動

- MarkPad 無帶檔案啟動時：
  - 顯示空白畫面
  - 只提示「拖入或開啟 Markdown」
- 不顯示 Recent Files
- 不顯示 Dashboard / Workspace

### 新建文件

- `Ctrl+N` 建立新文件
- 新文件預設名稱：`Untitled.md`
- 第一次 `Ctrl+S` 開啟標準 Save As
- 預設副檔名為 `.md`
- 使用者未輸入副檔名時自動補 `.md`

### 關閉文件

- 關閉最後一個 Tab 後，MarkPad 視窗保留
- 回到「拖入或開啟 Markdown」空白畫面
- 有未儲存修改時：
  - 顯示 `Save / Don't Save / Cancel`
- 未儲存 Tab 在檔名前加 `●`

### 啟動與 Recent Files

- MarkPad 每次乾淨啟動，不自動恢復上次 Tabs
- Recent Files 保留 **20 筆**
- Recent Files 放在 `More` 裡，不占首頁

---

## V1 儲存、自動儲存與外部變更

### Auto Save

- 預設關閉
- 可在 `More` 開啟
- 開啟後停止輸入 **3 秒** 自動儲存

### 外部修改

- 若檔案被外部程式修改：
  - MarkPad 內沒有未儲存變更 → 自動 Reload
  - MarkPad 內有未儲存變更 → 詢問 `Reload / Keep Current`

### Read-only

- Read-only 文件可 Preview
- 禁止切換到 Edit Mode

### 檔案消失

- 外部刪除目前檔案：
  - 保留目前內容
  - 標示 `File missing`
  - 允許 Save As
- 外部重新命名／移動檔案：
  - 視為原檔消失
  - 標示 `File missing`

---

## V1 Encoding 與 Large File

### Encoding

- 左側底部顯示目前 Encoding，例如 `UTF-8`
- 非 UTF-8 文件：
  - 自動偵測常見 Encoding
  - 儲存時保留原 Encoding
- UTF-8 BOM 必須正確處理

### Large File Mode

- 超過 **10 MB** 視為 Large File
- 顯示 `Large file mode` 提示
- 優先進 Edit Mode
- 不預先 Render Preview
- 只有使用者切到 Preview 時才進行 Render

---

## V1 Preview 補充互動規格

### Heading / Anchor

- Heading Anchor 跳轉採平滑捲動
- Heading Hover 時顯示 Anchor Link
- Anchor 不直接複製
- 右鍵才提供 `Copy Link`

### Code Block

- Copy 按鈕永遠顯示
- 按下 Copy 後短暫顯示 `Copied` 約 1～2 秒，再恢復
- 有指定語言時顯示語言名稱，例如 `csharp`

### Table

- Table 太寬時由 Table 自己出現水平 Scrollbar
- 不讓整個頁面水平捲動
- 長 Table 使用 Sticky Header
- 儲存格內容預設自動換行
- Inline Code 在 Table 中維持不換行

### HTML

- 支援常見 Inline HTML / Block HTML
- 禁止 `<script>` 與其他可執行內容
- 僅 Render 安全 HTML

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
