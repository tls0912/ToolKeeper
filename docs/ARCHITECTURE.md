# ToolKeeper Architecture

## 目標

ToolKeeper 採用 **Monorepo + Standalone Apps**。

每一個工具都是可獨立建置、獨立上架的 Windows App；ToolKeeper Launcher 本身不承載工具功能。

目前第一個產品為 **001 — MarkPad**。

## Repository Structure

```text
ToolKeeper/
├─ docs/
│  ├─ PRODUCT_VISION.md
│  ├─ ARCHITECTURE.md
│  └─ products/
│     └─ 001_MarkPad.md
│
├─ src/
│  └─ MarkPad/
│     ├─ Models/
│     ├─ Rendering/
│     ├─ Resources/
│     ├─ Services/
│     ├─ App.xaml
│     ├─ App.xaml.cs
│     ├─ MainWindow.xaml
│     ├─ MainWindow.xaml.cs
│     ├─ app.manifest
│     └─ MarkPad.csproj
│
├─ Directory.Build.props
└─ ToolKeeper.sln
```

## 核心原則

### 1. 一個產品先維持一個 Project

MarkPad V1 只使用：

```text
src/MarkPad/MarkPad.csproj
```

不預先拆成：

- MarkPad.Domain
- MarkPad.Application
- MarkPad.Infrastructure
- MarkPad.Contracts
- MarkPad.Common

除非未來真的出現獨立部署、明確共用或測試隔離需求。

### 2. 不預先建立 Shared Project

第二、第三個工具出現前，不建立 `Shared`、`Common`、`Core`。

只有當至少兩個產品已經出現**實際重複程式碼**，且抽取後能降低維護成本，才考慮共用元件。

> 先重複，再抽象；不要為想像中的未來抽象。

### 3. MarkPad 內部分工

- **Models**：文件 Tab、設定狀態等簡單資料模型
- **Services**：檔案、設定、Recovery、Windows 整合等實際服務
- **Rendering**：Markdown → Preview 的轉換與 Preview 相關邏輯
- **Resources**：Theme、Localization、Icon 等 UI Resource
- **MainWindow**：組合 UI 與協調使用者操作

V1 不要求所有 UI 都套完整 MVVM。若某區塊複雜度真的提高，再局部引入 ViewModel。

### 4. 第三方套件隔離

預計使用的第三方技術：

- AvalonEdit
- Markdig
- WebView2

加入時盡量集中在對應功能區，不讓第三方 API 擴散到整個程式。

例如：

```text
Rendering/MarkdownRenderer.cs
```

負責包住 Markdig。

### 5. 產品規格優先於架構

V1 已 Freeze 的產品規格：

```text
docs/products/001_MarkPad.md
```

架構只為了完成產品規格存在。

若架構設計與「簡單、順手、快速開啟」衝突，優先簡化架構。

## Future Products

未來工具直接新增：

```text
src/
├─ MarkPad/
├─ Product002/
├─ Product003/
└─ ...
```

每個產品仍以 Standalone App 為原則。

ToolKeeper Launcher 需要實作時，再建立自己的 App Project，不先建立空殼。
