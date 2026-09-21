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
- Edit / Preview 快速切換
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

### 啟動

使用者雙擊：

```text
README.md
```

應直接進入：

```text
README.md - MarkPad
```

不要先出現：

- 首頁
- Dashboard
- Workspace 選擇
- 工具商城

### 操作

使用者不需要閱讀說明書就應該知道如何：

- 打開
- 編輯
- 預覽
- 儲存
- 關閉

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
   ├─ Yes → 啟動 Markdown App
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
